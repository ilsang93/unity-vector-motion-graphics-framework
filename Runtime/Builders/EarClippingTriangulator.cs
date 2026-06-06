using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Self-contained ear clipping triangulator for simple polygons.
    /// O(n^2). Handles concave polygons. Does NOT handle holes or
    /// self-intersections — sufficient for typical motion-graphics shapes.
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

            s_indices.Clear();
            if (reversed)
                for (int i = n - 1; i >= 0; i--) s_indices.Add(i);
            else
                for (int i = 0; i < n; i++) s_indices.Add(i);

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
                if (!earFound) break; // degenerate polygon
            }

            if (s_indices.Count == 3)
            {
                outTris.Add(vertexOffset + s_indices[0]);
                outTris.Add(vertexOffset + s_indices[1]);
                outTris.Add(vertexOffset + s_indices[2]);
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
