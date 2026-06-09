using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Converts a node list with cubic Bezier tangents into a flat polyline.
    /// Segments between two consecutive nodes whose tangents are zero collapse
    /// to a straight line; otherwise the cubic A, A+outTangent, B+inTangent, B
    /// is subdivided adaptively until each chord is straight enough relative
    /// to the segment's chord length.
    ///
    /// All builders/modifiers downstream of the source operate on the flat
    /// polyline produced here — Bezier control points exist only at authoring
    /// time.
    public static class BezierTessellator
    {
        private const float MinTangentSqr = 1e-8f;

        // Flatness threshold: a sub-curve is considered straight enough to
        // emit as a single edge when both control points sit within this
        // fraction of the chord length from the chord line. 0.005 ≈ half a
        // percent of chord length — tight enough that the visible chord
        // gap stays under one pixel at typical zoom levels.
        private const float FlatnessRatio = 0.005f;

        /// Tessellates nodes into outPath as a polyline. `samplesPerSegment`
        /// controls the *maximum* subdivision depth of curved segments (the
        /// budget cap; adaptive subdivision uses fewer samples on near-flat
        /// curves). Straight segments stay as a single edge.
        public static void Tessellate(IList<VectorNode> nodes, bool closed, int samplesPerSegment, VectorPath outPath)
        {
            outPath.nodes.Clear();
            int n = nodes.Count;
            // A "closed" path with fewer than 3 nodes degenerates: closing 2
            // nodes traces the same curve back and forth (looks like a leaf),
            // closing 1 node is a point. Demote those cases to open.
            if (n < 3) closed = false;
            outPath.closed = closed;
            if (n == 0) return;
            if (n == 1) { outPath.Add(nodes[0].position); return; }

            int segCount = closed ? n : n - 1;

            // Map the legacy "samples per segment" knob to a recursion-depth
            // cap. Log2(samples) keeps the worst-case sample count comparable
            // to the old uniform-stepping budget — samples=16 → depth 4 → up
            // to 16 emitted points per segment.
            int maxDepth = MaxDepthFromSamples(samplesPerSegment);

            outPath.Add(nodes[0].position);
            for (int i = 0; i < segCount; i++)
            {
                VectorNode a = nodes[i];
                VectorNode b = nodes[(i + 1) % n];
                bool curved = a.outTangent.sqrMagnitude > MinTangentSqr
                              || b.inTangent.sqrMagnitude > MinTangentSqr;

                bool dropEndpoint = closed && i == segCount - 1;

                if (!curved)
                {
                    if (!dropEndpoint) outPath.Add(b.position);
                    continue;
                }

                Vector2 p0 = a.position;
                Vector2 p1 = a.position + a.outTangent;
                Vector2 p2 = b.position + b.inTangent;
                Vector2 p3 = b.position;
                SubdivideAdaptive(p0, p1, p2, p3, maxDepth, outPath);
                if (!dropEndpoint) outPath.Add(p3);
            }
        }

        // Recursive de Casteljau subdivision. Emits intermediate points
        // (NOT the endpoints) into outPath so the caller controls seam
        // placement. Stops when the curve is flat enough OR depth is
        // exhausted — the depth cap prevents pathological self-overlapping
        // curves from blowing up the vertex budget.
        private static void SubdivideAdaptive(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
                                              int depthRemaining, VectorPath outPath)
        {
            if (depthRemaining <= 0 || IsFlatEnough(p0, p1, p2, p3))
            {
                return;
            }

            // de Casteljau split at t=0.5.
            Vector2 m01 = (p0 + p1) * 0.5f;
            Vector2 m12 = (p1 + p2) * 0.5f;
            Vector2 m23 = (p2 + p3) * 0.5f;
            Vector2 m012 = (m01 + m12) * 0.5f;
            Vector2 m123 = (m12 + m23) * 0.5f;
            Vector2 m = (m012 + m123) * 0.5f;

            SubdivideAdaptive(p0, m01, m012, m, depthRemaining - 1, outPath);
            outPath.Add(m);
            SubdivideAdaptive(m, m123, m23, p3, depthRemaining - 1, outPath);
        }

        // A sub-curve is flat enough if both control points lie within
        // FlatnessRatio × chord length of the chord line. Uses squared
        // distances to skip the sqrt on the common path.
        private static bool IsFlatEnough(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            Vector2 chord = p3 - p0;
            float chordLenSqr = chord.sqrMagnitude;
            if (chordLenSqr < 1e-10f)
            {
                // Degenerate chord (endpoints coincide — a loop). Use the
                // distance from each control point to the shared endpoint
                // as a proxy for "is the loop tiny enough to skip."
                return (p1 - p0).sqrMagnitude < 1e-10f && (p2 - p0).sqrMagnitude < 1e-10f;
            }
            float tolSqr = FlatnessRatio * FlatnessRatio * chordLenSqr;
            return PerpDistSqrToChord(p1, p0, chord, chordLenSqr) <= tolSqr
                && PerpDistSqrToChord(p2, p0, chord, chordLenSqr) <= tolSqr;
        }

        // Perpendicular squared distance from `p` to the infinite line
        // through `a` with direction `dir` (whose squared length is
        // `dirLenSqr`). Standard projection identity.
        private static float PerpDistSqrToChord(Vector2 p, Vector2 a, Vector2 dir, float dirLenSqr)
        {
            Vector2 ap = p - a;
            float crossZ = ap.x * dir.y - ap.y * dir.x;
            return crossZ * crossZ / dirLenSqr;
        }

        private static int MaxDepthFromSamples(int samples)
        {
            int s = Mathf.Max(2, samples);
            // ceil(log2(s)). samples=2 → 1, 4 → 2, 8 → 3, 16 → 4, 32 → 5,
            // 64 → 6. Depth 6 emits up to 64 intermediate points per
            // segment, matching the old worst-case budget.
            int depth = 0;
            int v = s - 1;
            while (v > 0) { depth++; v >>= 1; }
            return Mathf.Max(1, depth);
        }

        public static Vector2 EvalCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            return uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3;
        }
    }
}
