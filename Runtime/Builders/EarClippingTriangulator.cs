using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Self-contained ear clipping triangulator for simple polygons.
    /// O(n^2). Handles concave polygons.
    ///
    /// Self-intersecting polygons: not formally supported. The fill they
    /// receive is whatever ear-clipping happens to emit before it stalls
    /// on a degenerate configuration (some sub-regions may be left empty,
    /// some may overdraw). The triangulator IS deterministic on such
    /// inputs — it rotates the index list to start at the geometrically
    /// leftmost-lowest vertex before the ear loop, so the same polygon
    /// shape always produces the same triangle set regardless of which
    /// node the caller happened to list first. That gives users a stable
    /// (if partial) fill they can design around, instead of one whose
    /// gaps shift each frame as a morph wiggles the input node order.
    public static class EarClippingTriangulator
    {
        // Reusable scratch buffer to avoid per-call GC.
        private static readonly List<int> s_indices = new List<int>(64);

        public static void Triangulate(IList<Vector2> polygon, List<int> outTris, int vertexOffset)
        {
            int n = polygon.Count;
            if (n < 3) return;

            // Ensure CCW winding so the ear test (cross > 0) is consistent.
            float area = SignedArea(polygon);
            bool reversed = area < 0f;

            // Build the working index list in CCW order.
            s_indices.Clear();
            if (reversed)
                for (int i = n - 1; i >= 0; i--) s_indices.Add(i);
            else
                for (int i = 0; i < n; i++) s_indices.Add(i);

            // Rotate so the leftmost-lowest vertex is first. Ear-clipping
            // is sensitive to starting index — same polygon, different
            // start, different triangle set. Locking the start to a
            // geometric anchor makes the output a pure function of the
            // polygon's shape, which stabilizes the fill of self-
            // intersecting morph intermediates frame-to-frame.
            int anchor = FindLeftmostLowest(polygon, s_indices);
            if (anchor > 0)
            {
                // Rotate s_indices in place: shift elements so [anchor]
                // becomes [0]. Done with a reverse-trio to avoid an
                // allocation; correctness comes from the classic
                // reverse(prefix) + reverse(suffix) + reverse(all) trick.
                ReverseRange(s_indices, 0, anchor - 1);
                ReverseRange(s_indices, anchor, s_indices.Count - 1);
                ReverseRange(s_indices, 0, s_indices.Count - 1);
            }

            int safety = n * n + 16;
            while (s_indices.Count > 3 && safety-- > 0)
            {
                bool earFound = false;
                int count = s_indices.Count;
                for (int i = 0; i < count; i++)
                {
                    int i0 = s_indices[(i - 1 + count) % count];
                    int i1 = s_indices[i];
                    int i2 = s_indices[(i + 1) % count];

                    Vector2 a = polygon[i0];
                    Vector2 b = polygon[i1];
                    Vector2 c = polygon[i2];

                    if (Cross(b - a, c - b) <= 0f) continue; // reflex vertex

                    bool containsOther = false;
                    for (int j = 0; j < count; j++)
                    {
                        int idx = s_indices[j];
                        if (idx == i0 || idx == i1 || idx == i2) continue;
                        if (PointInTriangle(polygon[idx], a, b, c))
                        {
                            containsOther = true;
                            break;
                        }
                    }
                    if (containsOther) continue;

                    outTris.Add(vertexOffset + i0);
                    outTris.Add(vertexOffset + i1);
                    outTris.Add(vertexOffset + i2);
                    s_indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
                // Degenerate (no valid ear). Fan out all remaining
                // vertices unconditionally. Downstream consumers (notably
                // FillTessellator) filter the result by winding number,
                // so the extra triangles the fan emits over "wrong"
                // regions are discarded anyway — what they buy is filling
                // in the genuine pockets that ear-clipping stalled on.
                // For consumers that DON'T post-filter (currently none in
                // this package), the fan may overdraw on self-intersecting
                // inputs; that's an acceptable trade for filling small
                // unfilled pockets that ear-clipping would otherwise miss.
                if (!earFound) { FanFallback(outTris, vertexOffset); return; }
            }

            if (s_indices.Count == 3)
            {
                outTris.Add(vertexOffset + s_indices[0]);
                outTris.Add(vertexOffset + s_indices[1]);
                outTris.Add(vertexOffset + s_indices[2]);
            }
        }

        // Fan triangulation of the remaining ring around its first vertex.
        // Last-resort path when the main ear loop can't make progress.
        private static void FanFallback(List<int> outTris, int vertexOffset)
        {
            int remaining = s_indices.Count;
            if (remaining < 3) return;
            int anchor = s_indices[0];
            for (int i = 1; i < remaining - 1; i++)
            {
                outTris.Add(vertexOffset + anchor);
                outTris.Add(vertexOffset + s_indices[i]);
                outTris.Add(vertexOffset + s_indices[i + 1]);
            }
        }

        // Returns the position in `indices` (not the vertex index itself)
        // of the polygon vertex that's leftmost, breaking ties by lowest y.
        // Deterministic anchor: same shape → same anchor regardless of
        // which node the caller listed first.
        private static int FindLeftmostLowest(IList<Vector2> polygon, List<int> indices)
        {
            int best = 0;
            Vector2 bestP = polygon[indices[0]];
            for (int i = 1; i < indices.Count; i++)
            {
                Vector2 p = polygon[indices[i]];
                if (p.x < bestP.x || (p.x == bestP.x && p.y < bestP.y))
                {
                    best = i;
                    bestP = p;
                }
            }
            return best;
        }

        private static void ReverseRange(List<int> list, int lo, int hi)
        {
            while (lo < hi)
            {
                int t = list[lo]; list[lo] = list[hi]; list[hi] = t;
                lo++; hi--;
            }
        }

        private static float SignedArea(IList<Vector2> p)
        {
            float s = 0f;
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = p[i];
                Vector2 b = p[(i + 1) % n];
                s += (b.x - a.x) * (b.y + a.y);
            }
            return -s * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }
    }
}
