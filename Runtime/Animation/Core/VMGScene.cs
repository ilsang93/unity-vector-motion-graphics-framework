using System;
using System.Collections.Generic;
using UnityEngine;
using VMG.Core;
using VMG.UI;
using VMG.World;

namespace VMG.Animation.Core
{
    // Code-driven scene composition. Created via VMGFx.Scene(root). Each
    // .Add(name, descriptor) materialises a GameObject + the appropriate
    // renderer (VectorImageGraphic if root is RectTransform, otherwise
    // VectorSpriteRenderer) and returns the scene for chaining.
    //
    // Scene takes ownership of GameObjects it creates: Remove(name) and
    // Clear() destroy them. Children that already exist with the given
    // name (e.g. from a previous Add() in an Edit-mode reload) are reused
    // and updated in place — this is the idempotency contract that lets
    // OnEnable-driven script-mode (Round 6) re-run safely.
    public sealed class VMGScene
    {
        readonly Transform m_Root;
        readonly bool m_IsRectRoot;
        readonly Dictionary<string, Component> m_Children = new Dictionary<string, Component>();
        readonly Dictionary<string, VMGScene> m_Groups = new Dictionary<string, VMGScene>();

        internal VMGScene(Transform root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            m_Root = root;
            m_IsRectRoot = root is RectTransform;
        }

        public Transform Root => m_Root;

        // Indexer: returns the renderer Component for the named child, or
        // null if it has been destroyed externally / never existed. Destroyed
        // entries are evicted lazily so the dictionary doesn't hold stale
        // references.
        public Component this[string name]
        {
            get
            {
                if (m_Children.TryGetValue(name, out var c))
                {
                    if (c == null) { m_Children.Remove(name); return null; }
                    return c;
                }
                return null;
            }
        }

        // Typed lookup. Useful when the child has multiple Components and
        // the renderer isn't the one you want (e.g. fetching a Transform).
        public T Get<T>(string name) where T : Component
        {
            var c = this[name];
            if (c == null) return null;
            if (c is T tc) return tc;
            return c.GetComponent<T>();
        }

        // Add (or update) a named child under this scene's root.
        // Idempotent: if a child with this name already exists (in the scene's
        // dict or as a GameObject under root), it is reused and the
        // descriptor's HasXxx-flagged values are written into it.
        public VMGScene Add(string name, VMGShapeDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var go = FindOrCreateChild(name);
            Component renderer;
            if (m_IsRectRoot)
            {
                var g = go.GetComponent<VectorImageGraphic>();
                if (g == null) g = go.AddComponent<VectorImageGraphic>();
                ApplyDescriptor(go, g, descriptor);
                renderer = g;
            }
            else
            {
                var r = go.GetComponent<VectorSpriteRenderer>();
                if (r == null) r = go.AddComponent<VectorSpriteRenderer>();
                ApplyDescriptor(go, r, descriptor);
                renderer = r;
            }
            m_Children[name] = renderer;
            return this;
        }

        // Return (and lazily create) a sub-scene whose root is an existing
        // child added via Add(). This is the hook that `add ... in=parent`
        // in .vmgfx uses to nest one shape under another, letting the child
        // ride the parent's transform/sizeDelta chain (CSS-like cascade).
        // Throws if `name` hasn't been added yet, since asking for a parent
        // that doesn't exist is a script ordering bug worth surfacing.
        internal VMGScene ChildScene(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
            if (m_Groups.TryGetValue(name, out var sub) && sub != null && sub.m_Root != null) return sub;
            if (!m_Children.TryGetValue(name, out var c) || c == null)
                throw new InvalidOperationException($"VMGScene: no child '{name}' to nest into. Add the parent before referencing it via in=.");
            sub = new VMGScene(c.transform);
            m_Groups[name] = sub;
            return sub;
        }

        // Nest a sub-scene under a named child Transform. The lambda receives
        // a VMGScene whose root is the group GameObject. Re-running with the
        // same name reuses the existing group; children inside follow the
        // same idempotent Add rules.
        public VMGScene Group(string name, Action<VMGScene> build)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
            if (build == null) throw new ArgumentNullException(nameof(build));

            if (!m_Groups.TryGetValue(name, out var sub) || sub == null || sub.m_Root == null)
            {
                var go = FindOrCreateChild(name);
                // Groups under a Rect root keep RectTransform so descendants
                // inherit the UI placement chain; otherwise plain Transform.
                sub = new VMGScene(go.transform);
                m_Groups[name] = sub;
            }
            build(sub);
            return this;
        }

        public VMGScene Remove(string name)
        {
            if (m_Children.TryGetValue(name, out var c))
            {
                m_Children.Remove(name);
                if (c != null) DestroySafely(c.gameObject);
            }
            if (m_Groups.TryGetValue(name, out var sub))
            {
                m_Groups.Remove(name);
                if (sub != null && sub.m_Root != null) DestroySafely(sub.m_Root.gameObject);
            }
            return this;
        }

        public void Clear()
        {
            foreach (var kv in m_Children)
            {
                if (kv.Value != null) DestroySafely(kv.Value.gameObject);
            }
            m_Children.Clear();
            foreach (var kv in m_Groups)
            {
                if (kv.Value != null && kv.Value.m_Root != null)
                    DestroySafely(kv.Value.m_Root.gameObject);
            }
            m_Groups.Clear();
        }

        // ----- internals -----

        GameObject FindOrCreateChild(string name)
        {
            // Look up by Transform first. A previous session (Edit-mode
            // reload, script-mode re-entry) may have left the child in the
            // hierarchy without it being in our dict.
            var existing = m_Root.Find(name);
            if (existing != null) return existing.gameObject;

            var go = m_IsRectRoot ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
            go.transform.SetParent(m_Root, worldPositionStays: false);
            return go;
        }

        static void DestroySafely(GameObject go)
        {
            if (go == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEngine.Object.DestroyImmediate(go);
            else UnityEngine.Object.Destroy(go);
#else
            UnityEngine.Object.Destroy(go);
#endif
        }

        // Always-write semantics: every field on the descriptor is reflected
        // onto the renderer, including booleans like FillStyle.enabled. This
        // makes the Scene declarative — "what you set is what you get". A
        // shape that doesn't call .Fill() comes up with fill disabled, not
        // with the renderer's own default (which is enabled=true on UGUI).
        // The HasXxx flags on the descriptor are kept for future partial-
        // update APIs but aren't gated on here.
        static void ApplyDescriptor(GameObject go, VectorImageGraphic g, VMGShapeDescriptor d)
        {
            ApplyShared(go, d);

            g.FitToRect = d.m_FitToRect;

            // SVG-backed descriptors take the renderer's SvgAsset path,
            // which short-circuits the procedural ShapeStack pipeline. We
            // still copy Fill/Stroke/etc. since SVG assets can be tinted
            // by the renderer's color, but slot 0 is left to the asset.
            if (d is VMGSvgDescriptor svg)
            {
                g.SvgAsset = svg.m_SvgAsset;
            }
            else
            {
                g.SvgAsset = null;
                ApplyShapeStack(ref g.ShapeStack, d);
            }
            g.Fill = d.m_Fill;
            g.Stroke = d.m_Stroke;
            g.RoundCorners = d.m_RoundCorners;
            g.Trim = d.m_Trim;
            if (d.m_HasWiggle) g.Wiggle = d.m_Wiggle;

            var rt = go.transform as RectTransform;
            if (rt != null)
            {
                // Pivot and anchor both affect how sizeDelta / anchoredPosition
                // resolve, so apply them first. Only when the descriptor
                // explicitly set one — scripts that don't mention these keep
                // the RectTransform's authored values (typically 0.5, 0.5).
                if (d.m_HasPivot) rt.pivot = d.m_Pivot;
                if (d.m_HasAnchor) { rt.anchorMin = d.m_Anchor; rt.anchorMax = d.m_Anchor; }
                rt.sizeDelta = d.m_Size;
                rt.anchoredPosition = d.m_Position;
            }
        }

        // World renderer variant. Size lives inside PrimitiveShapeSource
        // (set on the descriptor's m_Slot0Shape by .Size()), Position maps
        // to localPosition.
        static void ApplyDescriptor(GameObject go, VectorSpriteRenderer r, VMGShapeDescriptor d)
        {
            ApplyShared(go, d);

            if (d is VMGSvgDescriptor svg)
            {
                r.SvgAsset = svg.m_SvgAsset;
            }
            else
            {
                r.SvgAsset = null;
                ApplyShapeStack(ref r.ShapeStack, d);
            }
            r.Fill = d.m_Fill;
            r.Stroke = d.m_Stroke;
            r.RoundCorners = d.m_RoundCorners;
            r.Trim = d.m_Trim;
            if (d.m_HasWiggle) r.Wiggle = d.m_Wiggle;

            go.transform.localPosition = new Vector3(d.m_Position.x, d.m_Position.y, go.transform.localPosition.z);
        }

        static void ApplyShared(GameObject go, VMGShapeDescriptor d)
        {
            go.transform.localEulerAngles = new Vector3(0f, 0f, d.m_RotationDeg);
        }

        // Slot 0 is always written; slots 1..3 only when the descriptor
        // explicitly set them via .Slot(i, ...).
        static void ApplyShapeStack(ref ShapeStack stack, VMGShapeDescriptor d)
        {
            stack.Slot0 = new ShapeSlot
            {
                shape = d.m_Slot0Shape,
                intensity = d.m_Slot0Intensity,
            };
            if (d.m_Slot1Shape.HasValue)
                stack.Slot1 = new ShapeSlot { shape = d.m_Slot1Shape.Value, intensity = d.m_Slot1Intensity };
            if (d.m_Slot2Shape.HasValue)
                stack.Slot2 = new ShapeSlot { shape = d.m_Slot2Shape.Value, intensity = d.m_Slot2Intensity };
            if (d.m_Slot3Shape.HasValue)
                stack.Slot3 = new ShapeSlot { shape = d.m_Slot3Shape.Value, intensity = d.m_Slot3Intensity };
        }
    }
}
