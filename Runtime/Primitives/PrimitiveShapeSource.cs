using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Single serializable shape descriptor that emits a VectorPath
    /// at evaluation time. Every numeric field is keyframable from
    /// Animator / Timeline.
    ///
    /// IMPORTANT: this is a struct, not a class. Reasons:
    /// (1) Unity's AnimationClip binding only walks into struct-typed
    ///     [SerializeField] members; class-typed members are treated as
    ///     opaque object references and their inner fields don't surface
    ///     as keyframable properties.
    /// (2) Struct fields can't have field initializers, so factory
    ///     defaults are applied lazily via Normalize() on first Build().
    ///
    /// FreePath node storage: 64 individual FlatNode fields
    /// (m_Node00..m_Node63) plus activeNodeCount. Unity exposes named
    /// struct fields to the Animation window's Add Property tree but
    /// NOT List<T> or T[] element fields, so a flat layout is the only
    /// way to make per-node values keyframable. Reorder is unsupported;
    /// add/remove happens at the end (activeNodeCount++/--).
    [Serializable]
    public struct PrimitiveShapeSource : IShapeSource
    {
        public const int MaxFreeNodes = 64;

        [Tooltip("Primitive type. Keyframable but switching mid-animation snaps mesh topology — prefer PathMorphModifier for shape transitions.")]
        public ShapeKind kind;
        [Tooltip("Shape center in local space. Keyframable.")]
        public Vector2 center;

        // Circle / Ellipse / RoundedRect / Polygon
        [Tooltip("Shape size. For Circle and Polygon only x is used (as diameter). Keyframable.")]
        public Vector2 size;

        // Polygon
        [Range(3, 64)]
        [Tooltip("Polygon side count. Keyframable (integer interpolation).")]
        public int sides;

        // RoundedRect
        [Min(0f)]
        [Tooltip("Corner radius for RoundedRectangle. Keyframable.")]
        public float cornerRadius;

        // Tessellation density for curved primitives.
        [Range(8, 256)]
        [Tooltip("Tessellation density for curved primitives (circle/ellipse/rounded rect). Keyframable but raises vertex cost.")]
        public int circleSegments;

        // Free path
        [Tooltip("Whether the FreePath wraps from last back to first node. Keyframable (bool).")]
        public bool freeClosed;
        [Range(2, 64)]
        [Tooltip("Bezier tessellation density per cubic segment. Keyframable.")]
        public int bezierSamplesPerSegment;
        [Range(0, MaxFreeNodes)]
        [Tooltip("How many of the 64 FreePath node slots are active. Keyframable so the active count can animate over time.")]
        public int activeNodeCount;

        // Legacy: pre-flattening List<VectorNode>. Kept for scene
        // migration only — Normalize() copies any data here into the
        // flat slots on first access then clears the list, so by the
        // time anything else runs the slots are the single source of
        // truth.
        [HideInInspector]
        public List<VectorNode> freeNodesLegacy;

        // 64 flat node slots. Each is a [Serializable] struct field, so
        // Animation window surfaces m_Node00.position.x, .inTangent.y,
        // etc. as keyframable channels. The repetition is the price for
        // Unity's "no list-element keyframes" constraint.
        public FlatNode m_Node00; public FlatNode m_Node01; public FlatNode m_Node02; public FlatNode m_Node03;
        public FlatNode m_Node04; public FlatNode m_Node05; public FlatNode m_Node06; public FlatNode m_Node07;
        public FlatNode m_Node08; public FlatNode m_Node09; public FlatNode m_Node10; public FlatNode m_Node11;
        public FlatNode m_Node12; public FlatNode m_Node13; public FlatNode m_Node14; public FlatNode m_Node15;
        public FlatNode m_Node16; public FlatNode m_Node17; public FlatNode m_Node18; public FlatNode m_Node19;
        public FlatNode m_Node20; public FlatNode m_Node21; public FlatNode m_Node22; public FlatNode m_Node23;
        public FlatNode m_Node24; public FlatNode m_Node25; public FlatNode m_Node26; public FlatNode m_Node27;
        public FlatNode m_Node28; public FlatNode m_Node29; public FlatNode m_Node30; public FlatNode m_Node31;
        public FlatNode m_Node32; public FlatNode m_Node33; public FlatNode m_Node34; public FlatNode m_Node35;
        public FlatNode m_Node36; public FlatNode m_Node37; public FlatNode m_Node38; public FlatNode m_Node39;
        public FlatNode m_Node40; public FlatNode m_Node41; public FlatNode m_Node42; public FlatNode m_Node43;
        public FlatNode m_Node44; public FlatNode m_Node45; public FlatNode m_Node46; public FlatNode m_Node47;
        public FlatNode m_Node48; public FlatNode m_Node49; public FlatNode m_Node50; public FlatNode m_Node51;
        public FlatNode m_Node52; public FlatNode m_Node53; public FlatNode m_Node54; public FlatNode m_Node55;
        public FlatNode m_Node56; public FlatNode m_Node57; public FlatNode m_Node58; public FlatNode m_Node59;
        public FlatNode m_Node60; public FlatNode m_Node61; public FlatNode m_Node62; public FlatNode m_Node63;

        // Shared scratch buffer for BuildFree. Calls are main-thread
        // and sequential (the modifier stack runs `target.Build` AFTER
        // the source's Build() returns), so a static reused buffer is
        // safe and avoids per-Build allocations even when the struct
        // gets boxed via IShapeSource.
        private static readonly List<VectorNode> s_BuildScratch = new List<VectorNode>(MaxFreeNodes);

        // Magic-but-effective default fixup. Structs can't have field
        // initializers, so freshly-deserialized instances start zeroed.
        // Call this from Build() to set sane defaults without
        // overwriting user-set values: only zeroed fields get touched.
        public void Normalize()
        {
            if (size.x <= 0f && size.y <= 0f) size = new Vector2(100f, 100f);
            if (sides < 3) sides = 6;
            if (circleSegments < 8) circleSegments = 64;
            if (bezierSamplesPerSegment < 2) bezierSamplesPerSegment = 16;

            // Migrate legacy List<VectorNode> into flat slots so old
            // scenes upgrade transparently. Slots are only the source of
            // truth from now on; clear the list to prevent re-migration.
            if (freeNodesLegacy != null && freeNodesLegacy.Count > 0)
            {
                int n = Mathf.Min(freeNodesLegacy.Count, MaxFreeNodes);
                for (int i = 0; i < n; i++) SetSlot(i, FlatNode.From(freeNodesLegacy[i]));
                activeNodeCount = n;
                freeNodesLegacy = null;
            }
        }

        /// Factory: a new instance with all fields at their authoring
        /// defaults. Used when explicitly instantiating from code.
        public static PrimitiveShapeSource Default()
        {
            var s = new PrimitiveShapeSource();
            s.Normalize();
            return s;
        }

        // Slot accessors. Hand-written switch instead of reflection so
        // the inner loop in BuildFree stays allocation-free and fast.
        public FlatNode GetSlot(int i)
        {
            switch (i)
            {
                case  0: return m_Node00; case  1: return m_Node01; case  2: return m_Node02; case  3: return m_Node03;
                case  4: return m_Node04; case  5: return m_Node05; case  6: return m_Node06; case  7: return m_Node07;
                case  8: return m_Node08; case  9: return m_Node09; case 10: return m_Node10; case 11: return m_Node11;
                case 12: return m_Node12; case 13: return m_Node13; case 14: return m_Node14; case 15: return m_Node15;
                case 16: return m_Node16; case 17: return m_Node17; case 18: return m_Node18; case 19: return m_Node19;
                case 20: return m_Node20; case 21: return m_Node21; case 22: return m_Node22; case 23: return m_Node23;
                case 24: return m_Node24; case 25: return m_Node25; case 26: return m_Node26; case 27: return m_Node27;
                case 28: return m_Node28; case 29: return m_Node29; case 30: return m_Node30; case 31: return m_Node31;
                case 32: return m_Node32; case 33: return m_Node33; case 34: return m_Node34; case 35: return m_Node35;
                case 36: return m_Node36; case 37: return m_Node37; case 38: return m_Node38; case 39: return m_Node39;
                case 40: return m_Node40; case 41: return m_Node41; case 42: return m_Node42; case 43: return m_Node43;
                case 44: return m_Node44; case 45: return m_Node45; case 46: return m_Node46; case 47: return m_Node47;
                case 48: return m_Node48; case 49: return m_Node49; case 50: return m_Node50; case 51: return m_Node51;
                case 52: return m_Node52; case 53: return m_Node53; case 54: return m_Node54; case 55: return m_Node55;
                case 56: return m_Node56; case 57: return m_Node57; case 58: return m_Node58; case 59: return m_Node59;
                case 60: return m_Node60; case 61: return m_Node61; case 62: return m_Node62; case 63: return m_Node63;
                default: return default;
            }
        }

        public void SetSlot(int i, FlatNode v)
        {
            switch (i)
            {
                case  0: m_Node00 = v; break; case  1: m_Node01 = v; break; case  2: m_Node02 = v; break; case  3: m_Node03 = v; break;
                case  4: m_Node04 = v; break; case  5: m_Node05 = v; break; case  6: m_Node06 = v; break; case  7: m_Node07 = v; break;
                case  8: m_Node08 = v; break; case  9: m_Node09 = v; break; case 10: m_Node10 = v; break; case 11: m_Node11 = v; break;
                case 12: m_Node12 = v; break; case 13: m_Node13 = v; break; case 14: m_Node14 = v; break; case 15: m_Node15 = v; break;
                case 16: m_Node16 = v; break; case 17: m_Node17 = v; break; case 18: m_Node18 = v; break; case 19: m_Node19 = v; break;
                case 20: m_Node20 = v; break; case 21: m_Node21 = v; break; case 22: m_Node22 = v; break; case 23: m_Node23 = v; break;
                case 24: m_Node24 = v; break; case 25: m_Node25 = v; break; case 26: m_Node26 = v; break; case 27: m_Node27 = v; break;
                case 28: m_Node28 = v; break; case 29: m_Node29 = v; break; case 30: m_Node30 = v; break; case 31: m_Node31 = v; break;
                case 32: m_Node32 = v; break; case 33: m_Node33 = v; break; case 34: m_Node34 = v; break; case 35: m_Node35 = v; break;
                case 36: m_Node36 = v; break; case 37: m_Node37 = v; break; case 38: m_Node38 = v; break; case 39: m_Node39 = v; break;
                case 40: m_Node40 = v; break; case 41: m_Node41 = v; break; case 42: m_Node42 = v; break; case 43: m_Node43 = v; break;
                case 44: m_Node44 = v; break; case 45: m_Node45 = v; break; case 46: m_Node46 = v; break; case 47: m_Node47 = v; break;
                case 48: m_Node48 = v; break; case 49: m_Node49 = v; break; case 50: m_Node50 = v; break; case 51: m_Node51 = v; break;
                case 52: m_Node52 = v; break; case 53: m_Node53 = v; break; case 54: m_Node54 = v; break; case 55: m_Node55 = v; break;
                case 56: m_Node56 = v; break; case 57: m_Node57 = v; break; case 58: m_Node58 = v; break; case 59: m_Node59 = v; break;
                case 60: m_Node60 = v; break; case 61: m_Node61 = v; break; case 62: m_Node62 = v; break; case 63: m_Node63 = v; break;
            }
        }

        public void Build(VectorPath outPath)
        {
            Normalize();
            switch (kind)
            {
                case ShapeKind.Circle: BuildEllipse(outPath, size.x * 0.5f, size.x * 0.5f); break;
                case ShapeKind.Ellipse: BuildEllipse(outPath, size.x * 0.5f, size.y * 0.5f); break;
                case ShapeKind.Rectangle: BuildRect(outPath, size); break;
                case ShapeKind.RoundedRectangle: BuildRoundedRect(outPath, size, cornerRadius); break;
                case ShapeKind.Polygon: BuildPolygon(outPath, size.x * 0.5f, sides); break;
                case ShapeKind.FreePath: BuildFree(outPath); break;
            }
        }

        private void BuildEllipse(VectorPath p, float rx, float ry)
        {
            int seg = Mathf.Max(8, circleSegments);
            for (int i = 0; i < seg; i++)
            {
                float t = (i / (float)seg) * Mathf.PI * 2f;
                p.Add(center + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry));
            }
            p.closed = true;
        }

        private void BuildRect(VectorPath p, Vector2 s)
        {
            Vector2 h = s * 0.5f;
            p.Add(center + new Vector2(-h.x, -h.y));
            p.Add(center + new Vector2( h.x, -h.y));
            p.Add(center + new Vector2( h.x,  h.y));
            p.Add(center + new Vector2(-h.x,  h.y));
            p.closed = true;
        }

        private void BuildRoundedRect(VectorPath p, Vector2 s, float r)
        {
            Vector2 h = s * 0.5f;
            float maxR = Mathf.Min(h.x, h.y);
            // Cap strictly below half-extent so the four corner centers never
            // coincide. When r >= maxR the shape collapses to an ellipse, so
            // emit one as a clean special case.
            if (r >= maxR - 1e-4f)
            {
                BuildEllipse(p, h.x, h.y);
                return;
            }
            r = Mathf.Max(0f, r);
            if (r <= 0f) { BuildRect(p, s); return; }

            int arcSeg = Mathf.Max(2, circleSegments / 8);

            // Four corner arcs starting from bottom-right, CCW.
            AddArc(p, center + new Vector2( h.x - r, -h.y + r), r, -Mathf.PI * 0.5f, 0f,          arcSeg);
            AddArc(p, center + new Vector2( h.x - r,  h.y - r), r, 0f,               Mathf.PI*0.5f, arcSeg);
            AddArc(p, center + new Vector2(-h.x + r,  h.y - r), r, Mathf.PI*0.5f,    Mathf.PI,     arcSeg);
            AddArc(p, center + new Vector2(-h.x + r, -h.y + r), r, Mathf.PI,         Mathf.PI*1.5f, arcSeg);
            p.closed = true;
        }

        private static void AddArc(VectorPath p, Vector2 c, float r, float from, float to, int seg)
        {
            for (int i = 0; i <= seg; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)seg);
                p.Add(c + new Vector2(Mathf.Cos(t) * r, Mathf.Sin(t) * r));
            }
        }

        private void BuildPolygon(VectorPath p, float radius, int n)
        {
            n = Mathf.Max(3, n);
            float offset = -Mathf.PI * 0.5f; // first vertex points up
            for (int i = 0; i < n; i++)
            {
                float t = offset + (i / (float)n) * Mathf.PI * 2f;
                p.Add(center + new Vector2(Mathf.Cos(t) * radius, Mathf.Sin(t) * radius));
            }
            p.closed = true;
        }

        private void BuildFree(VectorPath p)
        {
            int count = Mathf.Clamp(activeNodeCount, 0, MaxFreeNodes);
            if (count == 0) { p.closed = freeClosed; return; }

            s_BuildScratch.Clear();
            for (int i = 0; i < count; i++) s_BuildScratch.Add(GetSlot(i).ToVectorNode());

            // Tessellate any cubic Bezier handles into a flat polyline so
            // every downstream modifier and builder sees uniform input.
            BezierTessellator.Tessellate(s_BuildScratch, freeClosed, bezierSamplesPerSegment, p);
        }
    }
}
