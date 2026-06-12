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
    /// (Node00..Node63) plus activeNodeCount. Unity exposes named
    /// struct fields to the Animation window's Add Property tree but
    /// NOT List<T> or T[] element fields, so a flat layout is the only
    /// way to make per-node values keyframable. Reorder is unsupported;
    /// add/remove happens at the end (activeNodeCount++/--).
    [Serializable]
    public struct PrimitiveShapeSource : IShapeSource
    {
        public const int MaxFreeNodes = 64;

        [Tooltip("Primitive type. Keyframable but switching mid-animation snaps mesh topology — prefer blending between ShapeStack slots for smooth transitions.")]
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
        [Tooltip("Corner radii (x = horizontal, y = vertical) for RoundedRectangle. Set x and y differently for elliptical corners (CSS `border-radius: Xpx / Ypx`). Keyframable.")]
        public Vector2 cornerRadii;

        // Tessellation density for curved primitives.
        [Range(8, 256)]
        [Tooltip("Tessellation density for curved primitives (circle/ellipse/rounded rect). Keyframable but raises vertex cost.")]
        public int circleSegments;

        // Free path
        [Tooltip("Whether the FreePath wraps from last back to first node. Keyframable (bool).")]
        public bool freeClosed;
        [Range(2, 64)]
        [Tooltip("Bezier tessellation budget per cubic segment. Adaptive subdivision uses fewer samples on near-straight curves and more on tight curves, up to this cap. Keyframable.")]
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
        // Animation window surfaces Node00.position.x, .inTangent.y,
        // etc. as keyframable channels. The repetition is the price for
        // Unity's "no list-element keyframes" constraint.
        public FlatNode Node00; public FlatNode Node01; public FlatNode Node02; public FlatNode Node03;
        public FlatNode Node04; public FlatNode Node05; public FlatNode Node06; public FlatNode Node07;
        public FlatNode Node08; public FlatNode Node09; public FlatNode Node10; public FlatNode Node11;
        public FlatNode Node12; public FlatNode Node13; public FlatNode Node14; public FlatNode Node15;
        public FlatNode Node16; public FlatNode Node17; public FlatNode Node18; public FlatNode Node19;
        public FlatNode Node20; public FlatNode Node21; public FlatNode Node22; public FlatNode Node23;
        public FlatNode Node24; public FlatNode Node25; public FlatNode Node26; public FlatNode Node27;
        public FlatNode Node28; public FlatNode Node29; public FlatNode Node30; public FlatNode Node31;
        public FlatNode Node32; public FlatNode Node33; public FlatNode Node34; public FlatNode Node35;
        public FlatNode Node36; public FlatNode Node37; public FlatNode Node38; public FlatNode Node39;
        public FlatNode Node40; public FlatNode Node41; public FlatNode Node42; public FlatNode Node43;
        public FlatNode Node44; public FlatNode Node45; public FlatNode Node46; public FlatNode Node47;
        public FlatNode Node48; public FlatNode Node49; public FlatNode Node50; public FlatNode Node51;
        public FlatNode Node52; public FlatNode Node53; public FlatNode Node54; public FlatNode Node55;
        public FlatNode Node56; public FlatNode Node57; public FlatNode Node58; public FlatNode Node59;
        public FlatNode Node60; public FlatNode Node61; public FlatNode Node62; public FlatNode Node63;

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
                case  0: return Node00; case  1: return Node01; case  2: return Node02; case  3: return Node03;
                case  4: return Node04; case  5: return Node05; case  6: return Node06; case  7: return Node07;
                case  8: return Node08; case  9: return Node09; case 10: return Node10; case 11: return Node11;
                case 12: return Node12; case 13: return Node13; case 14: return Node14; case 15: return Node15;
                case 16: return Node16; case 17: return Node17; case 18: return Node18; case 19: return Node19;
                case 20: return Node20; case 21: return Node21; case 22: return Node22; case 23: return Node23;
                case 24: return Node24; case 25: return Node25; case 26: return Node26; case 27: return Node27;
                case 28: return Node28; case 29: return Node29; case 30: return Node30; case 31: return Node31;
                case 32: return Node32; case 33: return Node33; case 34: return Node34; case 35: return Node35;
                case 36: return Node36; case 37: return Node37; case 38: return Node38; case 39: return Node39;
                case 40: return Node40; case 41: return Node41; case 42: return Node42; case 43: return Node43;
                case 44: return Node44; case 45: return Node45; case 46: return Node46; case 47: return Node47;
                case 48: return Node48; case 49: return Node49; case 50: return Node50; case 51: return Node51;
                case 52: return Node52; case 53: return Node53; case 54: return Node54; case 55: return Node55;
                case 56: return Node56; case 57: return Node57; case 58: return Node58; case 59: return Node59;
                case 60: return Node60; case 61: return Node61; case 62: return Node62; case 63: return Node63;
                default: return default;
            }
        }

        public void SetSlot(int i, FlatNode v)
        {
            switch (i)
            {
                case  0: Node00 = v; break; case  1: Node01 = v; break; case  2: Node02 = v; break; case  3: Node03 = v; break;
                case  4: Node04 = v; break; case  5: Node05 = v; break; case  6: Node06 = v; break; case  7: Node07 = v; break;
                case  8: Node08 = v; break; case  9: Node09 = v; break; case 10: Node10 = v; break; case 11: Node11 = v; break;
                case 12: Node12 = v; break; case 13: Node13 = v; break; case 14: Node14 = v; break; case 15: Node15 = v; break;
                case 16: Node16 = v; break; case 17: Node17 = v; break; case 18: Node18 = v; break; case 19: Node19 = v; break;
                case 20: Node20 = v; break; case 21: Node21 = v; break; case 22: Node22 = v; break; case 23: Node23 = v; break;
                case 24: Node24 = v; break; case 25: Node25 = v; break; case 26: Node26 = v; break; case 27: Node27 = v; break;
                case 28: Node28 = v; break; case 29: Node29 = v; break; case 30: Node30 = v; break; case 31: Node31 = v; break;
                case 32: Node32 = v; break; case 33: Node33 = v; break; case 34: Node34 = v; break; case 35: Node35 = v; break;
                case 36: Node36 = v; break; case 37: Node37 = v; break; case 38: Node38 = v; break; case 39: Node39 = v; break;
                case 40: Node40 = v; break; case 41: Node41 = v; break; case 42: Node42 = v; break; case 43: Node43 = v; break;
                case 44: Node44 = v; break; case 45: Node45 = v; break; case 46: Node46 = v; break; case 47: Node47 = v; break;
                case 48: Node48 = v; break; case 49: Node49 = v; break; case 50: Node50 = v; break; case 51: Node51 = v; break;
                case 52: Node52 = v; break; case 53: Node53 = v; break; case 54: Node54 = v; break; case 55: Node55 = v; break;
                case 56: Node56 = v; break; case 57: Node57 = v; break; case 58: Node58 = v; break; case 59: Node59 = v; break;
                case 60: Node60 = v; break; case 61: Node61 = v; break; case 62: Node62 = v; break; case 63: Node63 = v; break;
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
                case ShapeKind.RoundedRectangle: BuildRoundedRect(outPath, size, cornerRadii); break;
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

        private void BuildRoundedRect(VectorPath p, Vector2 s, Vector2 r)
        {
            Vector2 h = s * 0.5f;
            // Clamp each axis to half its extent. Going past collapses the
            // straight side between two adjacent arcs.
            float rx = Mathf.Clamp(r.x, 0f, h.x);
            float ry = Mathf.Clamp(r.y, 0f, h.y);

            // Degenerate to ellipse when BOTH radii reach the half-extent —
            // the four arcs meet and the straight edges vanish.
            if (rx >= h.x - 1e-4f && ry >= h.y - 1e-4f)
            {
                BuildEllipse(p, h.x, h.y);
                return;
            }
            if (rx <= 0f && ry <= 0f) { BuildRect(p, s); return; }

            int arcSeg = Mathf.Max(2, circleSegments / 8);

            // Four corner arcs starting from bottom-right, CCW. Each arc
            // center sits (rx, ry) inside the corresponding rectangle corner;
            // points on the arc use elliptical (rx, ry) sweep.
            AddEllipticalArc(p, center + new Vector2( h.x - rx, -h.y + ry), rx, ry, -Mathf.PI * 0.5f, 0f,            arcSeg);
            AddEllipticalArc(p, center + new Vector2( h.x - rx,  h.y - ry), rx, ry, 0f,              Mathf.PI * 0.5f, arcSeg);
            AddEllipticalArc(p, center + new Vector2(-h.x + rx,  h.y - ry), rx, ry, Mathf.PI * 0.5f, Mathf.PI,        arcSeg);
            AddEllipticalArc(p, center + new Vector2(-h.x + rx, -h.y + ry), rx, ry, Mathf.PI,        Mathf.PI * 1.5f, arcSeg);
            p.closed = true;
        }

        private static void AddEllipticalArc(VectorPath p, Vector2 c, float rx, float ry, float from, float to, int seg)
        {
            for (int i = 0; i <= seg; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)seg);
                p.Add(c + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry));
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
