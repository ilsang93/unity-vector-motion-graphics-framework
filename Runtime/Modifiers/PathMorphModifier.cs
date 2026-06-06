using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Linearly blends the working path toward a target shape's polyline.
    /// Both paths are resampled to the same vertex count (arc-length uniform)
    /// before lerping, so source and target may have different vertex counts.
    ///
    /// Struct (not class) so the Animation window's Add Property tree
    /// surfaces inner fields (enabled / progress / target.size / etc.)
    /// as keyframable channels.
    [Serializable]
    public struct PathMorphModifier : IPathModifier
    {
        [Tooltip("Whether morphing is applied. Keyframable.")]
        public bool enabled;
        [Range(0f, 1f)]
        [Tooltip("Blend amount from source toward target shape. Keyframable.")]
        public float progress;
        [Tooltip("Target shape to morph toward. Per-node fields are keyframable via the m_NodeNN slots (same rules as the renderer's base shape).")]
        public PrimitiveShapeSource target;
        [Range(8, 512)]
        [Tooltip("Vertex count both paths are resampled to before lerping. Keyframable but changing it mid-animation forces a topology change.")]
        public int resampleCount;

        /// Defaults applied on demand because structs can't have field
        /// initializers. Mirrors PrimitiveShapeSource.Normalize.
        public void Normalize()
        {
            if (resampleCount < 8) resampleCount = 64;
            target.Normalize();
        }

        public static PathMorphModifier Default()
        {
            var m = new PathMorphModifier { target = PrimitiveShapeSource.Default() };
            m.Normalize();
            return m;
        }

        private static readonly VectorPath s_targetPath = new VectorPath();
        private static readonly List<Vector2> s_srcSamples = new List<Vector2>(128);
        private static readonly List<Vector2> s_dstSamples = new List<Vector2>(128);

        public bool Enabled => enabled && progress > 0f;

        public void Apply(VectorPath path)
        {
            Normalize();
            if (path == null || path.Count < 2) return;

            s_targetPath.Clear();
            target.Build(s_targetPath);
            if (s_targetPath.Count < 2) return;

            int n = Mathf.Max(8, resampleCount);
            ResampleUniform(path, n, s_srcSamples);
            ResampleUniform(s_targetPath, n, s_dstSamples);

            bool closed = path.closed && s_targetPath.closed;
            path.nodes.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector2 a = s_srcSamples[i];
                Vector2 b = s_dstSamples[i];
                path.Add(Vector2.Lerp(a, b, progress));
            }
            path.closed = closed;
        }

        private static void ResampleUniform(VectorPath path, int count, List<Vector2> outPts)
        {
            outPts.Clear();
            int n = path.Count;
            bool closed = path.closed;
            int segCount = closed ? n : n - 1;
            if (segCount < 1) { for (int i = 0; i < count; i++) outPts.Add(path.GetPoint(0)); return; }

            // Cumulative arc length.
            float total = 0f;
            // Reuse the same temporary list each call — caller guarantees no
            // reentrancy.
            var lens = s_tmpLens;
            lens.Clear();
            lens.Add(0f);
            for (int i = 0; i < segCount; i++)
            {
                total += Vector2.Distance(path.GetPoint(i), path.GetPoint((i + 1) % n));
                lens.Add(total);
            }
            if (total < 1e-5f) { for (int i = 0; i < count; i++) outPts.Add(path.GetPoint(0)); return; }

            // For closed paths emit `count` samples evenly over [0,total).
            // For open paths emit `count` samples over [0,total].
            float step = closed ? total / count : total / (count - 1);
            int cursor = 0;
            for (int s = 0; s < count; s++)
            {
                float target = step * s;
                if (!closed && s == count - 1) target = total;
                while (cursor < segCount - 1 && lens[cursor + 1] < target) cursor++;
                float a = lens[cursor];
                float b = lens[cursor + 1];
                float t = (b - a) > 1e-7f ? (target - a) / (b - a) : 0f;
                outPts.Add(Vector2.Lerp(path.GetPoint(cursor), path.GetPoint((cursor + 1) % n), t));
            }
        }

        private static readonly List<float> s_tmpLens = new List<float>(128);
    }
}
