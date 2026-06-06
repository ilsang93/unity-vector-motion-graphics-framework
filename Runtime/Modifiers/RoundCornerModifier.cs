using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// AE "Round Corners" — modifies actual path geometry, not a shader fake.
    /// Each corner vertex is replaced by an arc tangent to both adjacent edges.
    ///
    /// Adjacent corners share an edge; the radius is therefore globally
    /// clamped so neighbouring arcs never overrun their shared segment.
    ///
    /// Struct (not class) so its fields surface to the Animation window
    /// as keyframable channels.
    [Serializable]
    public struct RoundCornerModifier : IPathModifier
    {
        [Tooltip("Whether corner rounding is applied. Keyframable.")]
        public bool enabled;
        [Min(0f)]
        [Tooltip("Corner radius in path-space units. Clamped per-corner so adjacent arcs do not overlap. Keyframable.")]
        public float radius;
        [Range(2, 32)]
        [Tooltip("Tessellation density of each rounded corner. Keyframable.")]
        public int segmentsPerCorner;

        /// Defaults applied on demand because structs can't have field
        /// initializers.
        public void Normalize()
        {
            if (segmentsPerCorner < 2) segmentsPerCorner = 6;
        }

        public static RoundCornerModifier Default()
        {
            var m = new RoundCornerModifier();
            m.Normalize();
            return m;
        }

        private static readonly List<VectorNode> s_buffer = new List<VectorNode>(64);
        // Per-corner cached geometry used across the multi-pass algorithm.
        private static float[] s_dist = new float[0];
        private static float[] s_angle = new float[0];
        private static Vector2[] s_dirPrev = new Vector2[0];
        private static Vector2[] s_dirNext = new Vector2[0];
        private static bool[] s_skip = new bool[0];

        public bool Enabled => enabled && radius > 0f;

        public void Apply(VectorPath path)
        {
            Normalize();
            int n = path.Count;
            if (n < 3) return;
            bool closed = path.closed;

            EnsureCapacity(n);

            // Pass 1: per-corner ideal tangent distance from corner along each edge.
            for (int i = 0; i < n; i++)
            {
                s_skip[i] = false;

                if (!closed && (i == 0 || i == n - 1))
                {
                    s_skip[i] = true;
                    s_dist[i] = 0f;
                    continue;
                }

                Vector2 p = path.nodes[i].position;
                Vector2 prev = path.nodes[(i - 1 + n) % n].position;
                Vector2 next = path.nodes[(i + 1) % n].position;
                Vector2 dP = prev - p;
                Vector2 dN = next - p;
                float lP = dP.magnitude;
                float lN = dN.magnitude;
                if (lP < 1e-5f || lN < 1e-5f) { s_skip[i] = true; s_dist[i] = 0f; continue; }
                dP /= lP; dN /= lN;

                float dot = Mathf.Clamp(Vector2.Dot(dP, dN), -1f, 1f);
                float ang = Mathf.Acos(dot);
                // Collinear / cusp: nothing to round.
                if (ang < 1e-4f || Mathf.PI - ang < 1e-4f) { s_skip[i] = true; s_dist[i] = 0f; continue; }

                s_dirPrev[i] = dP;
                s_dirNext[i] = dN;
                s_angle[i] = ang;
                s_dist[i] = radius / Mathf.Tan(ang * 0.5f);
            }

            // Pass 2: clamp distances so adjacent corners never together exceed
            // their shared edge length. Distribute proportionally.
            int edgeCount = closed ? n : n - 1;
            for (int e = 0; e < edgeCount; e++)
            {
                int a = e;
                int b = (e + 1) % n;
                float edgeLen = Vector2.Distance(path.nodes[a].position, path.nodes[b].position);
                float needA = s_skip[a] ? 0f : s_dist[a];
                float needB = s_skip[b] ? 0f : s_dist[b];
                float need = needA + needB;
                if (need > edgeLen && need > 1e-6f)
                {
                    float scale = edgeLen / need;
                    if (!s_skip[a]) s_dist[a] *= scale;
                    if (!s_skip[b]) s_dist[b] *= scale;
                }
            }

            // Pass 3: emit arcs using clamped distances.
            s_buffer.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 p = path.nodes[i].position;

                if (s_skip[i] || s_dist[i] <= 1e-5f)
                {
                    s_buffer.Add(VectorNode.Corner(p));
                    continue;
                }

                float dist = s_dist[i];
                float ang = s_angle[i];
                Vector2 dP = s_dirPrev[i];
                Vector2 dN = s_dirNext[i];

                Vector2 tStart = p + dP * dist;
                Vector2 tEnd   = p + dN * dist;

                Vector2 bisector = (dP + dN);
                float bLen = bisector.magnitude;
                if (bLen < 1e-5f) { s_buffer.Add(VectorNode.Corner(p)); continue; }
                bisector /= bLen;

                float cosHalf = Mathf.Cos(ang * 0.5f);
                if (cosHalf < 1e-5f) { s_buffer.Add(VectorNode.Corner(p)); continue; }
                Vector2 center = p + bisector * (dist / cosHalf);
                float effRadius = (tStart - center).magnitude;

                Vector2 vA = tStart - center;
                Vector2 vB = tEnd - center;
                float aStart = Mathf.Atan2(vA.y, vA.x);
                float aEnd = Mathf.Atan2(vB.y, vB.x);
                float delta = aEnd - aStart;
                if (delta > Mathf.PI) delta -= Mathf.PI * 2f;
                else if (delta < -Mathf.PI) delta += Mathf.PI * 2f;

                int seg = Mathf.Max(2, segmentsPerCorner);
                for (int s = 0; s <= seg; s++)
                {
                    float t = aStart + delta * (s / (float)seg);
                    Vector2 pt = center + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * effRadius;
                    s_buffer.Add(VectorNode.Corner(pt));
                }
            }

            path.nodes.Clear();
            for (int i = 0; i < s_buffer.Count; i++) path.nodes.Add(s_buffer[i]);
        }

        private static void EnsureCapacity(int n)
        {
            if (s_dist.Length < n)
            {
                int cap = Mathf.NextPowerOfTwo(n);
                s_dist = new float[cap];
                s_angle = new float[cap];
                s_dirPrev = new Vector2[cap];
                s_dirNext = new Vector2[cap];
                s_skip = new bool[cap];
            }
        }
    }
}
