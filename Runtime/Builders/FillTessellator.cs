using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Fills a closed polyline. Uses the even-odd rule, which matches the
    /// user-facing definition "the region geometrically enclosed by the
    /// stroke": a point is inside iff a ray from it crosses the stroke
    /// an odd number of times. Independent of CW/CCW direction.
    ///
    /// Dispatch:
    ///   • Simple (non-self-intersecting) paths → ear-clipping, which is
    ///     allocation-light and emits one vertex per node.
    ///   • Self-intersecting paths → trapezoidal scanline decomposition
    ///     between consecutive Y coordinates of vertices + edge crossings.
    ///     This handles self-touching topology natively (the rule is just
    ///     "count edge crossings of a ray, parity decides fill"), which
    ///     ear-clipping doesn't.
    ///
    /// API: callers push the source polyline in; the tessellator either
    /// (a) reuses source vertices 1:1 (ear-clip path), or (b) emits its
    /// own augmented set of vertices (trapezoid path). After Triangulate
    /// returns, the caller iterates `GetEmittedVertices()` to push them
    /// into its mesh buffer.
    public static class FillTessellator
    {
        private static readonly List<Vector2> s_emitted = new List<Vector2>(128);
        private static readonly List<int> s_rawTris = new List<int>(256);

        // Scanline scratch.
        private static readonly List<float> s_ys = new List<float>(128);
        private static readonly List<int> s_active = new List<int>(64);
        private static readonly List<EdgeX> s_eventX = new List<EdgeX>(64);

        private struct EdgeX { public float x; public int edge; }

        /// Tessellates `source` (a closed polyline) into fill triangles.
        /// Appends triangles (with `firstVert` offset) to `outTris`.
        /// After return, the caller MUST iterate `GetEmittedVertices()` and
        /// append each as a mesh vertex — triangle indices assume those
        /// vertices land at firstVert..firstVert+count-1.
        public static void Triangulate(IList<Vector2> source, List<int> outTris, int firstVert)
        {
            s_emitted.Clear();
            int n = source.Count;
            if (n < 3) return;

            if (!HasSelfIntersection(source))
            {
                // Fast path: copy source vertices, run ear-clipper directly.
                for (int i = 0; i < n; i++) s_emitted.Add(source[i]);
                s_rawTris.Clear();
                EarClippingTriangulator.Triangulate(s_emitted, s_rawTris, 0);
                for (int i = 0; i < s_rawTris.Count; i++) outTris.Add(firstVert + s_rawTris[i]);
                return;
            }

            // Slow path: scanline trapezoid decomposition.
            BuildTrapezoids(source, outTris, firstVert);
        }

        public static IReadOnlyList<Vector2> GetEmittedVertices() => s_emitted;

        // O(n^2) edge-pair test. Stops at the first crossing — only the
        // boolean result is needed for the fast-path branch.
        private static bool HasSelfIntersection(IList<Vector2> p)
        {
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a1 = p[i];
                Vector2 a2 = p[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    // Skip the edge that wraps back to i.
                    if (i == 0 && j == n - 1) continue;
                    Vector2 b1 = p[j];
                    Vector2 b2 = p[(j + 1) % n];
                    if (SegmentsCross(a1, a2, b1, b2)) return true;
                }
            }
            return false;
        }

        private static bool SegmentsCross(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            Vector2 r = a2 - a1, s = b2 - b1;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-10f) return false;
            Vector2 d = b1 - a1;
            float t = (d.x * s.y - d.y * s.x) / denom;
            float u = (d.x * r.y - d.y * r.x) / denom;
            const float Eps = 1e-6f;
            return t > Eps && t < 1f - Eps && u > Eps && u < 1f - Eps;
        }

        // -----------------------------------------------------------------
        // Trapezoidal scanline decomposition.
        //
        // Cut the plane into horizontal strips at every Y that's either a
        // path vertex Y or a path-edge crossing Y. Inside each strip, every
        // active edge crosses cleanly top-to-bottom (no vertex sits inside
        // the strip's interior), so each edge contributes one X at the top
        // and one at the bottom of the strip.
        //
        // Sorting the active edges by their top-X (equivalently bottom-X,
        // since they don't swap inside the strip) and pairing them (0–1,
        // 2–3, …) gives the even-odd fill regions as trapezoids. Each
        // trapezoid emits 4 unique vertices + 2 triangles.
        // -----------------------------------------------------------------
        private static void BuildTrapezoids(IList<Vector2> source, List<int> outTris, int firstVert)
        {
            int n = source.Count;

            // 1. Gather scanline Y values: all vertex Ys + all edge-edge
            //    crossing Ys. Sort + dedupe.
            s_ys.Clear();
            for (int i = 0; i < n; i++) s_ys.Add(source[i].y);
            for (int i = 0; i < n; i++)
            {
                Vector2 a1 = source[i], a2 = source[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    Vector2 b1 = source[j], b2 = source[(j + 1) % n];
                    if (TryIntersect(a1, a2, b1, b2, out Vector2 ip))
                        s_ys.Add(ip.y);
                }
            }
            s_ys.Sort();
            DedupeSorted(s_ys, 1e-6f);

            int stripCount = s_ys.Count - 1;
            if (stripCount <= 0) return;

            // 2. Process each strip independently. The strip's edges are
            //    those whose Y range fully spans [yLo, yHi]. (Because we
            //    cut at every vertex Y, no edge starts/ends inside a strip.)
            for (int si = 0; si < stripCount; si++)
            {
                float yLo = s_ys[si];
                float yHi = s_ys[si + 1];
                float yMid = (yLo + yHi) * 0.5f;

                s_active.Clear();
                s_eventX.Clear();
                for (int e = 0; e < n; e++)
                {
                    Vector2 a = source[e], b = source[(e + 1) % n];
                    float eyLo = Mathf.Min(a.y, b.y);
                    float eyHi = Mathf.Max(a.y, b.y);
                    // Edge crosses this strip iff its Y range fully covers
                    // the strip interior. A horizontal edge (eyLo == eyHi)
                    // can't contribute either rail of a trapezoid.
                    if (eyLo > yLo + 1e-7f) continue;
                    if (eyHi < yHi - 1e-7f) continue;
                    if (eyHi - eyLo < 1e-7f) continue;
                    s_active.Add(e);
                    float xMid = XAt(a, b, yMid);
                    s_eventX.Add(new EdgeX { x = xMid, edge = e });
                }
                if (s_eventX.Count < 2) continue;

                // 3. Sort active edges by their X at strip mid-Y; pair
                //    adjacent edges (0–1, 2–3, …) into fill trapezoids.
                //    Insertion sort — typical strips have a handful of
                //    active edges and Sort()'s comparer allocates.
                for (int k = 1; k < s_eventX.Count; k++)
                {
                    var key = s_eventX[k];
                    int m = k - 1;
                    while (m >= 0 && s_eventX[m].x > key.x) { s_eventX[m + 1] = s_eventX[m]; m--; }
                    s_eventX[m + 1] = key;
                }

                for (int p = 0; p + 1 < s_eventX.Count; p += 2)
                {
                    int eL = s_eventX[p].edge;
                    int eR = s_eventX[p + 1].edge;
                    Vector2 aL = source[eL], bL = source[(eL + 1) % n];
                    Vector2 aR = source[eR], bR = source[(eR + 1) % n];
                    float xLLo = XAt(aL, bL, yLo);
                    float xLHi = XAt(aL, bL, yHi);
                    float xRLo = XAt(aR, bR, yLo);
                    float xRHi = XAt(aR, bR, yHi);

                    // Trapezoid vertices: bottom-left, bottom-right,
                    // top-right, top-left. CCW order.
                    int v0 = s_emitted.Count;
                    s_emitted.Add(new Vector2(xLLo, yLo));
                    s_emitted.Add(new Vector2(xRLo, yLo));
                    s_emitted.Add(new Vector2(xRHi, yHi));
                    s_emitted.Add(new Vector2(xLHi, yHi));
                    outTris.Add(firstVert + v0);
                    outTris.Add(firstVert + v0 + 1);
                    outTris.Add(firstVert + v0 + 2);
                    outTris.Add(firstVert + v0);
                    outTris.Add(firstVert + v0 + 2);
                    outTris.Add(firstVert + v0 + 3);
                }
            }
        }

        private static float XAt(Vector2 a, Vector2 b, float y)
        {
            float dy = b.y - a.y;
            if (Mathf.Abs(dy) < 1e-7f) return a.x;
            return a.x + (b.x - a.x) * ((y - a.y) / dy);
        }

        private static bool TryIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 ip)
        {
            ip = default;
            Vector2 r = a2 - a1, s = b2 - b1;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-10f) return false;
            Vector2 d = b1 - a1;
            float t = (d.x * s.y - d.y * s.x) / denom;
            float u = (d.x * r.y - d.y * r.x) / denom;
            const float Eps = 1e-6f;
            if (t <= Eps || t >= 1f - Eps) return false;
            if (u <= Eps || u >= 1f - Eps) return false;
            ip = a1 + r * t;
            return true;
        }

        private static void DedupeSorted(List<float> ys, float eps)
        {
            int w = 0;
            for (int i = 0; i < ys.Count; i++)
            {
                if (w == 0 || ys[i] - ys[w - 1] > eps) ys[w++] = ys[i];
            }
            if (w < ys.Count) ys.RemoveRange(w, ys.Count - w);
        }
    }
}
