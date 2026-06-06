using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// AE "Trim Paths". Operates on path arc-length. Start/End in [0,1],
    /// Offset wraps. For Phase 1 this is best used on open paths feeding
    /// the stroke pipeline; closed paths are converted to open trimmed paths.
    ///
    /// Struct (not class) so its fields surface to the Animation window
    /// as keyframable channels.
    [Serializable]
    public struct TrimPathModifier : IPathModifier
    {
        [Tooltip("Whether trim is applied. Keyframable.")]
        public bool enabled;
        [Range(0f, 1f)]
        [Tooltip("Trim start as fraction of arc length [0,1]. Keyframable.")]
        public float start;
        [Range(0f, 1f)]
        [Tooltip("Trim end as fraction of arc length [0,1]. Keyframable.")]
        public float end;
        [Range(-1f, 1f)]
        [Tooltip("Offset applied to start/end before slicing; wraps on closed paths. Keyframable.")]
        public float offset;

        /// Factory: a fresh trim modifier with end=1 so authoring code
        /// sees a sensible "no-trim" default. Inspector-created modifiers
        /// rely on `enabled = false` instead — once a user enables the
        /// modifier they set start/end explicitly, so no field fixup is
        /// needed.
        public static TrimPathModifier Default()
        {
            return new TrimPathModifier { end = 1f };
        }

        private static readonly List<Vector2> s_resampled = new List<Vector2>(64);
        private static readonly List<float> s_cumLen = new List<float>(64);

        public bool Enabled => enabled;

        public void Apply(VectorPath path)
        {
            int n = path.Count;
            if (n < 2) return;

            float s = Mathf.Clamp01(start);
            float e = Mathf.Clamp01(end);
            // For closed paths offset wraps (the path is a loop). For
            // open paths offset just slides the window, so we keep its
            // raw value below and clamp the resulting window to [0,1].
            float oWrap = offset - Mathf.Floor(offset);

            // Linearise: build cumulative-length table over the (optionally closed) polyline.
            bool closed = path.closed;
            int segCount = closed ? n : n - 1;
            s_cumLen.Clear();
            s_cumLen.Add(0f);
            float total = 0f;
            for (int i = 0; i < segCount; i++)
            {
                Vector2 a = path.nodes[i].position;
                Vector2 b = path.nodes[(i + 1) % n].position;
                total += Vector2.Distance(a, b);
                s_cumLen.Add(total);
            }
            if (total < 1e-5f) return;

            s_resampled.Clear();

            if (closed)
            {
                // Identity check (no-op trim).
                if (Mathf.Approximately(s, 0f) && Mathf.Approximately(e, 1f) && Mathf.Approximately(oWrap, 0f)) return;

                float t0 = s + oWrap;
                float t1 = e + oWrap;
                if (t1 - t0 >= 1f) return; // full coverage, identity after wrap
                t0 -= Mathf.Floor(t0);
                t1 -= Mathf.Floor(t1);

                if (t0 <= t1)
                {
                    Sample(path, segCount, n, t0 * total, t1 * total, s_resampled);
                }
                else
                {
                    // Wrap: emit [t0,1] then [0,t1]. Drop the seam vertex.
                    Sample(path, segCount, n, t0 * total, total, s_resampled);
                    int beforeSecond = s_resampled.Count;
                    Sample(path, segCount, n, 0f, t1 * total, s_resampled);
                    if (beforeSecond > 0 && s_resampled.Count > beforeSecond
                        && (s_resampled[beforeSecond - 1] - s_resampled[beforeSecond]).sqrMagnitude < 1e-10f)
                    {
                        s_resampled.RemoveAt(beforeSecond);
                    }
                }
            }
            else
            {
                // Open path: there is no "loop" to wrap around, so the
                // offset is treated as a continuous shift of the [s,e]
                // window. Anything that slides off either end of the
                // path is simply not drawn — no flicker, no full clear.
                // Identity check uses raw offset (not the wrapped form)
                // because offset=0 is the only true no-op here.
                if (Mathf.Approximately(s, 0f) && Mathf.Approximately(e, 1f) && Mathf.Approximately(offset, 0f)) return;

                float t0 = Mathf.Clamp01(s + offset);
                float t1 = Mathf.Clamp01(e + offset);
                if (t1 - t0 <= 1e-6f) { path.nodes.Clear(); path.closed = false; return; }
                Sample(path, segCount, n, t0 * total, t1 * total, s_resampled);
            }

            path.nodes.Clear();
            for (int i = 0; i < s_resampled.Count; i++) path.nodes.Add(VectorNode.Corner(s_resampled[i]));
            path.closed = false; // trimmed result is treated as open
        }

        private static void Sample(VectorPath path, int segCount, int n, float lenA, float lenB, List<Vector2> outPts)
        {
            if (lenB <= lenA) return;
            outPts.Add(PointAt(path, segCount, n, lenA));
            for (int i = 0; i < segCount; i++)
            {
                float segStart = s_cumLen[i];
                float segEnd = s_cumLen[i + 1];
                if (segEnd <= lenA) continue;
                if (segStart >= lenB) break;
                if (segStart > lenA && segStart < lenB)
                {
                    outPts.Add(path.nodes[i].position);
                }
            }
            outPts.Add(PointAt(path, segCount, n, lenB));
        }

        private static Vector2 PointAt(VectorPath path, int segCount, int n, float len)
        {
            for (int i = 0; i < segCount; i++)
            {
                float a = s_cumLen[i];
                float b = s_cumLen[i + 1];
                if (len <= b)
                {
                    float t = (b - a) > 1e-7f ? (len - a) / (b - a) : 0f;
                    return Vector2.Lerp(path.nodes[i].position, path.nodes[(i + 1) % n].position, t);
                }
            }
            return path.nodes[segCount % n].position;
        }
    }
}
