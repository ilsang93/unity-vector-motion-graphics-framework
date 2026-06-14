using System;
using System.Collections.Generic;
using UnityEngine;
using VMG.Core;

namespace VMG.Svg
{
    /// One imported SVG = one asset. SVG has many <path>/<rect>/... siblings
    /// so this holds a flat list of sub-shapes that the renderer draws in
    /// order. Coordinates are already normalized to the SVG's viewBox at
    /// import time so the asset is unit-agnostic.
    [Serializable]
    public sealed class VMGSubShape
    {
        public string id;
        public List<VectorNode> nodes = new List<VectorNode>();
        public bool closed = true;
        public FillStyle fill = new FillStyle { enabled = false, color = Color.white };
        public StrokeStyle stroke = new StrokeStyle
        {
            enabled = false,
            color = Color.black,
            width = 1f,
            alignment = StrokeAlignment.Center,
            cap = LineCap.Butt,
            join = LineJoin.Miter,
            miterLimit = 4f,
        };
    }

    [CreateAssetMenu(fileName = "VectorShape", menuName = "VMG/Vector Shape Asset", order = 100)]
    public sealed class VMGShapeAsset : ScriptableObject
    {
        public List<VMGSubShape> subShapes = new List<VMGSubShape>();
        /// SVG viewBox size in user units. Used by renderers to size/scale
        /// the asset into the target rect or world-space dimensions.
        public Vector2 viewBoxSize = new Vector2(100f, 100f);

        // Bezier-tessellated polyline per (subShape, bezSamples). Renderers
        // copy from this and apply their own origin/scale, leaving the
        // cached path untouched. Lazy-built on first GetTessellation; goes
        // away on domain reload (NonSerialized). Stays cheap because the
        // sampler value rarely varies across a project (typically 16).
        [System.NonSerialized] private VectorPath[][] m_Tessellation;
        [System.NonSerialized] private int[] m_TessellationKeys;

        /// Returns a tessellated polyline for one sub-shape at the given
        /// per-segment bezier-sample budget. Mutates the cache only — the
        /// returned VectorPath must NOT be mutated by callers; CopyFrom it
        /// onto a renderer-owned path before applying transforms. Returns
        /// null for invalid indices or sub-shapes with too few nodes.
        public VectorPath GetTessellation(int subShapeIndex, int bezSamples)
        {
            if (subShapeIndex < 0 || subShapeIndex >= subShapes.Count) return null;
            var sub = subShapes[subShapeIndex];
            if (sub == null || sub.nodes.Count < 2) return null;

            // Lazily size parallel arrays: m_TessellationKeys[i] = the
            // bezSamples value that m_Tessellation[i][subIndex] was built
            // with. We keep only the most-recent key per slot since
            // bezSamples is almost always one project-wide constant; if
            // two slots are in flight (e.g. one renderer set 16, another
            // 24), each gets its own slot via a linear probe.
            EnsureCacheCapacity();

            int slot = FindOrAllocSlot(bezSamples);
            var perSub = m_Tessellation[slot];
            var cached = perSub[subShapeIndex];
            if (cached != null) return cached;

            cached = new VectorPath();
            BezierTessellator.Tessellate(sub.nodes, sub.closed, bezSamples, cached);
            perSub[subShapeIndex] = cached;
            return cached;
        }

        /// Drops every tessellated polyline. Call after mutating any
        /// VMGSubShape.nodes / closed in code so the next renderer rebuild
        /// re-tessellates. VectorImageGraphic.SetMeshDirty() / VectorSprite
        /// Renderer.SetMeshDirty() both forward to this for convenience.
        public void ClearTessellationCache()
        {
            m_Tessellation = null;
            m_TessellationKeys = null;
        }

        private const int MaxCachedKeys = 4;
        private void EnsureCacheCapacity()
        {
            if (m_Tessellation == null)
            {
                m_Tessellation = new VectorPath[MaxCachedKeys][];
                m_TessellationKeys = new int[MaxCachedKeys];
            }
        }

        private int FindOrAllocSlot(int bezSamples)
        {
            for (int i = 0; i < MaxCachedKeys; i++)
            {
                if (m_Tessellation[i] != null && m_TessellationKeys[i] == bezSamples) return i;
            }
            for (int i = 0; i < MaxCachedKeys; i++)
            {
                if (m_Tessellation[i] == null)
                {
                    m_Tessellation[i] = new VectorPath[subShapes.Count];
                    m_TessellationKeys[i] = bezSamples;
                    return i;
                }
            }
            // All slots taken with different keys — evict slot 0. Rare in
            // practice; only happens if 5+ distinct bezSamples values are
            // live at once.
            m_Tessellation[0] = new VectorPath[subShapes.Count];
            m_TessellationKeys[0] = bezSamples;
            return 0;
        }
    }
}
