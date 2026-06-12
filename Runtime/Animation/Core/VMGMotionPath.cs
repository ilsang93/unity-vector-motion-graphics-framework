using System.Collections.Generic;
using UnityEngine;
using VMG.Core;
using VMG.Svg;

namespace VMG.Animation.Core
{
    // Arc-length parametrized sampler over a flat polyline. Built once from
    // a VMGShapeAsset sub-shape or an inline Vector2 list, then queried by a
    // motion-path tween every frame.
    //
    // anime.js parity: createMotionPath returns x/y/angle accessors that walk
    // a path uniformly; this is the same idea, packed into one struct so a
    // single tween can read both position and tangent in one step.
    internal sealed class VMGMotionPath
    {
        // Flat polyline (already tessellated). Length-1 entries are valid
        // (a single point — Sample returns that point regardless of t).
        readonly Vector2[] m_Points;
        readonly bool m_Closed;

        // Cumulative arc length at each point. m_Cumulative[0] = 0,
        // m_Cumulative[^1] = total. For closed paths there's an extra
        // entry at the end mirroring m_Points[0] so a single binary search
        // covers the wrap-around segment too.
        readonly float[] m_Cumulative;
        readonly float m_TotalLength;

        VMGMotionPath(Vector2[] points, bool closed)
        {
            m_Points = points;
            m_Closed = closed;

            int n = points.Length;
            int cumCount = closed ? n + 1 : n;
            m_Cumulative = new float[cumCount];
            m_Cumulative[0] = 0f;
            float acc = 0f;
            for (int i = 1; i < n; i++)
            {
                acc += Vector2.Distance(points[i - 1], points[i]);
                m_Cumulative[i] = acc;
            }
            if (closed && n > 0)
            {
                acc += Vector2.Distance(points[n - 1], points[0]);
                m_Cumulative[n] = acc;
            }
            m_TotalLength = acc;
        }

        public bool IsValid => m_Points != null && m_Points.Length > 0;
        public float TotalLength => m_TotalLength;

        // Sample at normalized arc-length parameter u in [0, 1]. Returns
        // both the point and the unit tangent (zero if the path is a
        // single point). Tangent is in path-local 2D; consumers project
        // it however they need.
        public void Sample(float u, out Vector2 point, out Vector2 tangent)
        {
            if (m_Points.Length == 1 || m_TotalLength <= 0f)
            {
                point = m_Points[0];
                tangent = Vector2.right;
                return;
            }

            if (u <= 0f) { point = m_Points[0]; tangent = SegmentDir(0); return; }
            if (u >= 1f)
            {
                if (m_Closed) { point = m_Points[0]; tangent = SegmentDir(m_Points.Length - 1); return; }
                int last = m_Points.Length - 1;
                point = m_Points[last];
                tangent = SegmentDir(last - 1);
                return;
            }

            float target = u * m_TotalLength;
            // Binary search for the segment whose cumulative range contains target.
            int lo = 0, hi = m_Cumulative.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (m_Cumulative[mid] <= target) lo = mid;
                else hi = mid;
            }

            float segStart = m_Cumulative[lo];
            float segEnd = m_Cumulative[hi];
            float segLen = segEnd - segStart;
            float frac = segLen > 1e-8f ? (target - segStart) / segLen : 0f;

            Vector2 a = m_Points[lo];
            Vector2 b = m_Points[hi % m_Points.Length]; // wraps when closed
            point = Vector2.LerpUnclamped(a, b, frac);
            tangent = (b - a);
            float tm = tangent.magnitude;
            tangent = tm > 1e-8f ? tangent / tm : Vector2.right;
        }

        Vector2 SegmentDir(int i)
        {
            if (i < 0) i = 0;
            int n = m_Points.Length;
            Vector2 a = m_Points[i];
            Vector2 b = m_Points[(i + 1) % n];
            Vector2 d = b - a;
            float m = d.magnitude;
            return m > 1e-8f ? d / m : Vector2.right;
        }

        // ---- Factories ----

        // Reusable scratch path so successive constructions don't allocate
        // a fresh VectorPath every time. Not thread-safe (Unity main thread).
        static readonly VectorPath s_Scratch = new VectorPath();

        public static VMGMotionPath FromAsset(VMGShapeAsset asset, int subShapeIndex)
        {
            if (asset == null || asset.subShapes == null || asset.subShapes.Count == 0) return null;
            int idx = Mathf.Clamp(subShapeIndex, 0, asset.subShapes.Count - 1);
            var sub = asset.subShapes[idx];
            if (sub == null || sub.nodes == null || sub.nodes.Count == 0) return null;

            s_Scratch.Clear();
            // 16 samples per cubic is the same default the primitives use;
            // path-follow doesn't need higher fidelity than the renderer.
            BezierTessellator.Tessellate(sub.nodes, sub.closed, 16, s_Scratch);

            return FromVectorPath(s_Scratch);
        }

        public static VMGMotionPath FromPoints(IList<Vector2> points, bool closed)
        {
            if (points == null || points.Count == 0) return null;
            var arr = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++) arr[i] = points[i];
            return new VMGMotionPath(arr, closed && arr.Length >= 3);
        }

        static VMGMotionPath FromVectorPath(VectorPath path)
        {
            int n = path.Count;
            if (n == 0) return null;
            var arr = new Vector2[n];
            for (int i = 0; i < n; i++) arr[i] = path.GetPoint(i);
            return new VMGMotionPath(arr, path.closed && n >= 3);
        }
    }
}
