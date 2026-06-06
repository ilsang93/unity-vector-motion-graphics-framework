using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Uniform arc-length resampling of a polyline. Two paths with
    /// different vertex counts can be resampled to a common count
    /// and then blended index-by-index — the basis of N-way shape
    /// stacking and morph-style transitions.
    public static class ArcLengthResample
    {
        // Caller-owned output list. Static scratch buffer for cumulative
        // lengths because builds are sequential on the main thread.
        private static readonly List<float> s_tmpLens = new List<float>(128);

        /// Emit `count` samples evenly spaced along the polyline's arc
        /// length. Closed paths emit over [0, total); open paths over
        /// [0, total]. Output replaces any contents in `outPts`.
        public static void Resample(VectorPath path, int count, List<Vector2> outPts)
        {
            outPts.Clear();
            int n = path.Count;
            bool closed = path.closed;
            int segCount = closed ? n : n - 1;
            if (segCount < 1)
            {
                Vector2 only = n > 0 ? path.GetPoint(0) : Vector2.zero;
                for (int i = 0; i < count; i++) outPts.Add(only);
                return;
            }

            // Cumulative arc length.
            var lens = s_tmpLens;
            lens.Clear();
            lens.Add(0f);
            float total = 0f;
            for (int i = 0; i < segCount; i++)
            {
                total += Vector2.Distance(path.GetPoint(i), path.GetPoint((i + 1) % n));
                lens.Add(total);
            }
            if (total < 1e-5f)
            {
                Vector2 only = path.GetPoint(0);
                for (int i = 0; i < count; i++) outPts.Add(only);
                return;
            }

            float step = closed ? total / count : total / (count - 1);
            int cursor = 0;
            for (int s = 0; s < count; s++)
            {
                float t = step * s;
                if (!closed && s == count - 1) t = total;
                while (cursor < segCount - 1 && lens[cursor + 1] < t) cursor++;
                float a = lens[cursor];
                float b = lens[cursor + 1];
                float u = (b - a) > 1e-7f ? (t - a) / (b - a) : 0f;
                outPts.Add(Vector2.Lerp(path.GetPoint(cursor), path.GetPoint((cursor + 1) % n), u));
            }
        }
    }
}
