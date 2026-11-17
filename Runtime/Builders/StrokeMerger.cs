using System.Collections.Generic;
using UnityEngine;
using VMG.ThirdParty.Clipper2Lib;

namespace VMG.Core
{
    /// Overlap-free stroke meshing. The legacy StrokeMeshBuilder emits one
    /// independent ribbon per segment plus separate join / cap pieces, and
    /// every sub-shape is stroked on its own — opaque that is invisible,
    /// but under alpha blending each overlap blends twice (dark knots at
    /// joins, bright dots on round strokes, dark crossings where two
    /// sub-shapes intersect).
    ///
    /// This builder turns every stroke into its outline polygon (Clipper2),
    /// unions all outlines that share a color, triangulates the result once
    /// and puts the AA ring on the final outline only. Within one renderer
    /// no pixel of a same-colored stroke region is covered twice.
    ///
    ///   Center alignment  -> ClipperOffset. Strokes with the same color and
    ///                        width are offset in one call, whose internal
    ///                        union already merges them.
    ///   Inner / Outer     -> the legacy ribbon / join / cap pieces
    ///                        (StrokeMeshBuilder.BuildPieces), NonZero union.
    ///
    /// Vertex contract is unchanged: UV0 holds the position placeholder
    /// (ApplyGradient / NormalizeUVsToRect run afterwards as usual), UV1.x
    /// is the AA distance (1 = interior, 0 = visible edge).
    ///
    /// Known limits: different-colored strokes still blend over each other,
    /// and the fill + stroke of one shape still overlap (both need a
    /// difference pass, which brings conflation seams — deferred). Miter
    /// joins past the miter limit are squared off (Clipper) rather than
    /// beveled (legacy) on Center strokes.
    public sealed class StrokeMerger : IStrokePieceSink
    {
        // Strokes that can share one ClipperOffset.Execute (same delta and
        // miter limit). Join / end type may differ per path.
        private sealed class OffsetBatch
        {
            public double delta;
            public double miterLimit;
            public readonly List<Path64> paths = new List<Path64>(8);
            public readonly List<JoinType> joins = new List<JoinType>(8);
            public readonly List<EndType> ends = new List<EndType>(8);
        }

        private sealed class Group
        {
            public Color32 color;
            public readonly Paths64 pieces = new Paths64();
            public readonly List<OffsetBatch> batches = new List<OffsetBatch>(2);
            public float ring = float.PositiveInfinity;
        }

        /// Use constrained-Delaunay triangulation for the interior. Better
        /// shaped triangles (matters for per-vertex radial gradients) at
        /// roughly 2-3x the triangulation cost. Renderers enable it only for
        /// radial stroke gradients.
        public bool QualityTriangulation;

        private readonly List<Group> m_Groups = new List<Group>(4);
        private readonly List<Group> m_GroupPool = new List<Group>(4);
        private readonly List<OffsetBatch> m_BatchPool = new List<OffsetBatch>(4);
        private readonly List<Path64> m_PathPool = new List<Path64>(64);
        private readonly Clipper64 m_Clipper = new Clipper64();
        private readonly ClipperOffset m_Offset = new ClipperOffset();
        private readonly Paths64 m_OffsetOut = new Paths64();
        private readonly Paths64 m_Subject = new Paths64();
        private readonly Paths64 m_Solution = new Paths64();
        private readonly Paths64 m_Open = new Paths64();
        private readonly Dictionary<Point64, int> m_VertexIndex = new Dictionary<Point64, int>(256, PointComparer.Instance);
        private readonly List<List<Vector2>> m_Contours = new List<List<Vector2>>(8);
        private readonly List<List<Vector2>> m_ContourPool = new List<List<Vector2>>(8);
        private readonly List<IReadOnlyList<Vector2>> m_ContourView = new List<IReadOnlyList<Vector2>>(8);

        private Group m_Current;
        private double m_Scale = 1.0;
        private double m_InvScale = 1.0;
        private float m_Band;

        /// Start a new batch. `referenceSize` is the drawing's overall extent
        /// (fit rect / path bounds); it sets the integer grid resolution and
        /// the AA band width, which follows the same 0.5%-of-extent rule as
        /// FillMeshBuilder so merged strokes and fills antialias alike.
        public void Begin(float referenceSize)
        {
            for (int i = 0; i < m_Groups.Count; i++) RecycleGroup(m_Groups[i]);
            m_Groups.Clear();
            m_Current = null;
            float extent = Mathf.Max(referenceSize, 1e-6f);
            // ~1M grid units across the drawing: far below a pixel at any
            // zoom, far below Clipper's int64 range even for distant geometry.
            m_Scale = 1048576.0 / extent;
            m_InvScale = 1.0 / m_Scale;
            m_Band = Mathf.Max(extent * 0.005f, 1e-4f);
        }

        /// Reference size for Begin() when stroking a single path: its
        /// bounding box's longer side plus the stroke width.
        public static float ExtentOf(VectorPath path, float strokeWidth)
        {
            if (path == null || path.Count == 0) return strokeWidth;
            Vector2 min = path.nodes[0].position, max = min;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 p = path.nodes[i].position;
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Mathf.Max(max.x - min.x, max.y - min.y) + strokeWidth;
        }

        /// Collect one stroked path. Strokes with the same final color merge
        /// into one outline at Flush.
        public void Add(VectorPath path, in StrokeStyle style)
        {
            if (!style.enabled || path == null || path.Count < 2 || style.width <= 0f) return;
            m_Current = GroupFor(style.color);
            // Pull the edges in by the AA band so the outset ring lands the
            // visible edge where the legacy stroke's was. Clamped so thin
            // strokes keep at least half their width as solid core.
            float inset = Mathf.Min(m_Band, style.width * 0.25f);
            if (inset < m_Current.ring) m_Current.ring = inset;
            if (style.alignment == StrokeAlignment.Center) AddCenter(path, style, inset);
            else StrokeMeshBuilder.BuildPieces(path, style, inset, this);
            m_Current = null;
        }

        private void AddCenter(VectorPath path, in StrokeStyle style, float inset)
        {
            var src = RentPath();
            for (int i = 0; i < path.Count; i++)
            {
                var pt = ToPoint(path.nodes[i].position);
                if (src.Count > 0 && src[src.Count - 1] == pt) continue;
                src.Add(pt);
            }
            bool closed = path.closed && src.Count >= 3;
            if (closed && src[0] == src[src.Count - 1]) src.RemoveAt(src.Count - 1);
            if (src.Count < 2) { m_PathPool.Add(src); return; }

            double delta = (style.width * 0.5 - inset) * m_Scale;
            // Clipper measures the miter against the half width, VMG against
            // the full width: the same corner trips both at 2x the value.
            double miter = Mathf.Max(1f, style.miterLimit) * 2.0;
            var batch = BatchFor(m_Current, delta, miter);
            batch.paths.Add(src);
            batch.joins.Add(style.join == LineJoin.Round ? JoinType.Round
                          : style.join == LineJoin.Bevel ? JoinType.Bevel : JoinType.Miter);
            batch.ends.Add(closed ? EndType.Joined
                         : style.cap == LineCap.Round ? EndType.Round
                         : style.cap == LineCap.Square ? EndType.Square : EndType.Butt);
        }

        void IStrokePieceSink.AddPiece(List<Vector2> polygon)
        {
            if (polygon.Count < 3) return;
            var path = RentPath();
            for (int i = 0; i < polygon.Count; i++) path.Add(ToPoint(polygon[i]));
            // NonZero union needs every piece wound the same way, otherwise
            // a CW piece over a CCW one cancels to winding 0 (a hole).
            double area = Clipper.Area(path);
            if (area == 0.0) { m_PathPool.Add(path); return; }
            if (area < 0.0) path.Reverse();
            m_Current.pieces.Add(path);
        }

        /// Merge each color group and emit interior triangles + AA ring.
        public void Flush(MeshBuffer mb)
        {
            for (int g = 0; g < m_Groups.Count; g++)
            {
                var group = m_Groups[g];
                if (!Resolve(group)) continue;
                DropSpeckHoles();
                if (m_Solution.Count == 0) continue;

                EmitInterior(m_Solution, group.color, mb);

                // Outers come back positive (CCW, Y up) and holes negative,
                // i.e. the filled region is always on the contour's left —
                // EmitRing's convention, so holes get an inward ring.
                float ring = float.IsPositiveInfinity(group.ring) ? m_Band : group.ring;
                ToContours(m_Solution);
                for (int c = 0; c < m_Contours.Count; c++)
                    EmitRing(m_Contours[c], ring, group.color, mb);
            }
        }

        // Leaves the group's merged outline in m_Solution.
        private bool Resolve(Group group)
        {
            m_Solution.Clear();
            m_Subject.Clear();
            for (int b = 0; b < group.batches.Count; b++)
            {
                var batch = group.batches[b];
                m_Offset.Clear();
                m_Offset.MiterLimit = batch.miterLimit;
                // Round joins / caps: chord error under a quarter of the AA band.
                m_Offset.ArcTolerance = System.Math.Max(m_Band * 0.25 * m_Scale, 1.0);
                for (int i = 0; i < batch.paths.Count; i++)
                    m_Offset.AddPath(batch.paths[i], batch.joins[i], batch.ends[i]);
                m_Offset.Execute(batch.delta, m_OffsetOut);
                // One batch and nothing else: ClipperOffset already unioned
                // its paths (MergeGroups), so its output is the outline.
                if (group.batches.Count == 1 && group.pieces.Count == 0)
                {
                    m_Solution.AddRange(m_OffsetOut);
                    return m_Solution.Count > 0;
                }
                // Offset output is oriented (outers positive, holes
                // negative), which the NonZero union below relies on.
                m_Subject.AddRange(m_OffsetOut);
                m_OffsetOut.Clear(); // keep the paths alive in m_Subject
            }
            m_Subject.AddRange(group.pieces);
            if (m_Subject.Count == 0) return false;
            m_Clipper.Clear();
            m_Clipper.AddSubject(m_Subject);
            bool ok = m_Clipper.Execute(ClipType.Union, FillRule.NonZero, m_Solution, m_Open);
            m_Subject.Clear();
            if (!ok || m_Solution.Count == 0) return false;
            // Legacy pieces meet along shared edges whose endpoints were
            // computed separately; after rounding to the grid those edges
            // are only nearly collinear, so the union can leave hairline
            // gaps between pieces. Invisible as geometry, but the AA ring
            // on each side of a gap lands on the neighbor and blends twice.
            // A tiny morphological closing (grow, then shrink) seals them.
            if (group.pieces.Count > 0)
            {
                const double Seal = 4.0; // grid units (~4e-6 of the extent)
                m_Offset.Clear();
                m_Offset.MiterLimit = 4.0;
                m_Offset.ArcTolerance = 0.0;
                m_Offset.AddPaths(m_Solution, JoinType.Miter, EndType.Polygon);
                m_Offset.Execute(Seal, m_OffsetOut);
                m_Offset.Clear();
                m_Offset.AddPaths(m_OffsetOut, JoinType.Miter, EndType.Polygon);
                m_Offset.Execute(-Seal, m_Solution);
            }
            return m_Solution.Count > 0;
        }

        // A stroke wide enough to nearly close its own inside (a small
        // circle) leaves a sub-pixel hole; its AA ring would draw a visible
        // dot. Holes smaller than the AA band carry no visible area anyway.
        private void DropSpeckHoles()
        {
            double band = m_Band * m_Scale;
            double minArea = band * band * 4.0;
            for (int i = m_Solution.Count - 1; i >= 0; i--)
            {
                double area = Clipper.Area(m_Solution[i]);
                if (area < 0.0 && -area < minArea) m_Solution.RemoveAt(i);
            }
        }

        private void EmitInterior(Paths64 solution, Color32 col, MeshBuffer mb)
        {
            var result = Clipper.Triangulate(solution, out Paths64 tris, QualityTriangulation);
            if (result == TriangulateResult.success && CoversExactly(solution, tris))
            {
                // Triangles come back as independent 3-point paths; weld
                // shared corners so the interior costs one vertex per
                // outline point.
                m_VertexIndex.Clear();
                for (int t = 0; t < tris.Count; t++)
                {
                    var tri = tris[t];
                    if (tri.Count != 3) continue;
                    int a = Weld(tri[0], col, mb);
                    int b = Weld(tri[1], col, mb);
                    int c = Weld(tri[2], col, mb);
                    // Keep front faces CCW from +Z for depth-aware consumers.
                    long cross = (tri[1].X - tri[0].X) * (tri[2].Y - tri[0].Y)
                               - (tri[1].Y - tri[0].Y) * (tri[2].X - tri[0].X);
                    if (cross >= 0) mb.AddTriangle(a, b, c);
                    else mb.AddTriangle(a, c, b);
                }
                return;
            }

            // Fallback: even-odd scanline over every contour. The merged
            // outline never overlaps itself, so even-odd is exact; just slower
            // and vertex-heavier than the sweep triangulator.
            ToContours(solution);
            m_ContourView.Clear();
            for (int i = 0; i < m_Contours.Count; i++) m_ContourView.Add(m_Contours[i]);
            int first = mb.VertexCount;
            FillTessellator.TriangulateMulti(m_ContourView, mb.triangles, first);
            var emitted = FillTessellator.GetEmittedVertices();
            for (int i = 0; i < emitted.Count; i++) mb.AddVertex(emitted[i], col, 1f);
        }

        // Overlapping triangles would blend twice — the exact artifact this
        // class exists to remove — so a triangulation whose area does not
        // match the outline's is rejected.
        private static bool CoversExactly(Paths64 outline, Paths64 tris)
        {
            double target = System.Math.Abs(Clipper.Area(outline));
            if (target <= 0.0) return false;
            double sum = 0.0;
            for (int i = 0; i < tris.Count; i++) sum += System.Math.Abs(Clipper.Area(tris[i]));
            return System.Math.Abs(sum - target) <= target * 1e-4;
        }

        // AA band along one outline contour (filled region on its left).
        // Unlike FillMeshBuilder's per-edge quads + bevel wedges, adjacent
        // edges share one outer vertex pushed out along the corner bisector,
        // so the band never overlaps itself at concave corners — which on
        // a dense curve (the inside of a round stroke) is every vertex.
        private static void EmitRing(List<Vector2> poly, float w, Color32 col, MeshBuffer mb)
        {
            int n = poly.Count;
            if (n < 3 || w <= 0f) return;
            int first = mb.VertexCount;
            Vector2 prevDir = Dir(poly[n - 1], poly[0]);
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i];
                Vector2 nextDir = Dir(p, poly[(i + 1) % n]);
                Vector2 nPrev = new Vector2(-prevDir.y, prevDir.x);
                Vector2 nNext = new Vector2(-nextDir.y, nextDir.x);
                Vector2 bis = nPrev + nNext;
                float len = bis.magnitude;
                Vector2 off;
                if (len < 1e-4f) off = nNext * w;
                else
                {
                    bis /= len;
                    // Miter length w / cos(half angle), clamped so needle
                    // corners don't fling the band out; the band is only
                    // ~1px of fade, a thinner tip is invisible.
                    float cosHalf = Mathf.Max(Vector2.Dot(bis, nNext), 0.35f);
                    off = bis * (w / cosHalf);
                }
                mb.AddVertex(p, col, 1f);
                mb.AddVertex(p - off, col, 0f); // left normal points inward
                prevDir = nextDir;
            }
            for (int i = 0; i < n; i++)
            {
                int a = first + i * 2, outA = a + 1;
                int b = first + ((i + 1) % n) * 2, outB = b + 1;
                mb.AddTriangle(a, outA, outB);
                mb.AddTriangle(a, outB, b);
            }
        }

        private static Vector2 Dir(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float m = d.magnitude;
            return m > 1e-9f ? d / m : Vector2.right;
        }

        private int Weld(Point64 p, Color32 col, MeshBuffer mb)
        {
            if (m_VertexIndex.TryGetValue(p, out int idx)) return idx;
            idx = mb.VertexCount;
            mb.AddVertex(ToVector(p), col, 1f);
            m_VertexIndex.Add(p, idx);
            return idx;
        }

        private void ToContours(Paths64 solution)
        {
            for (int i = 0; i < m_Contours.Count; i++) m_ContourPool.Add(m_Contours[i]);
            m_Contours.Clear();
            for (int i = 0; i < solution.Count; i++)
            {
                var src = solution[i];
                if (src.Count < 3) continue;
                List<Vector2> dst;
                if (m_ContourPool.Count > 0)
                {
                    dst = m_ContourPool[m_ContourPool.Count - 1];
                    m_ContourPool.RemoveAt(m_ContourPool.Count - 1);
                    dst.Clear();
                }
                else dst = new List<Vector2>(src.Count);
                for (int k = 0; k < src.Count; k++) dst.Add(ToVector(src[k]));
                m_Contours.Add(dst);
            }
        }

        private Point64 ToPoint(Vector2 v) =>
            new Point64((long)System.Math.Round(v.x * m_Scale), (long)System.Math.Round(v.y * m_Scale));

        private Vector2 ToVector(Point64 p) => new Vector2((float)(p.X * m_InvScale), (float)(p.Y * m_InvScale));

        // Point64 only overrides Equals(object), which boxes on every
        // dictionary probe; this comparer keeps welding allocation-free.
        private sealed class PointComparer : IEqualityComparer<Point64>
        {
            public static readonly PointComparer Instance = new PointComparer();
            public bool Equals(Point64 a, Point64 b) => a.X == b.X && a.Y == b.Y;
            public int GetHashCode(Point64 p) => unchecked((int)(p.X * 73856093L ^ p.Y * 19349663L));
        }

        private Group GroupFor(Color32 color)
        {
            for (int i = 0; i < m_Groups.Count; i++)
            {
                var c = m_Groups[i].color;
                if (c.r == color.r && c.g == color.g && c.b == color.b && c.a == color.a) return m_Groups[i];
            }
            Group group;
            if (m_GroupPool.Count > 0)
            {
                group = m_GroupPool[m_GroupPool.Count - 1];
                m_GroupPool.RemoveAt(m_GroupPool.Count - 1);
            }
            else group = new Group();
            group.color = color;
            group.ring = float.PositiveInfinity;
            m_Groups.Add(group);
            return group;
        }

        private OffsetBatch BatchFor(Group group, double delta, double miter)
        {
            for (int i = 0; i < group.batches.Count; i++)
            {
                var b = group.batches[i];
                if (b.delta == delta && b.miterLimit == miter) return b;
            }
            OffsetBatch batch;
            if (m_BatchPool.Count > 0)
            {
                batch = m_BatchPool[m_BatchPool.Count - 1];
                m_BatchPool.RemoveAt(m_BatchPool.Count - 1);
            }
            else batch = new OffsetBatch();
            batch.delta = delta;
            batch.miterLimit = miter;
            group.batches.Add(batch);
            return batch;
        }

        private void RecycleGroup(Group group)
        {
            for (int i = 0; i < group.pieces.Count; i++) m_PathPool.Add(group.pieces[i]);
            group.pieces.Clear();
            for (int b = 0; b < group.batches.Count; b++)
            {
                var batch = group.batches[b];
                for (int i = 0; i < batch.paths.Count; i++) m_PathPool.Add(batch.paths[i]);
                batch.paths.Clear(); batch.joins.Clear(); batch.ends.Clear();
                m_BatchPool.Add(batch);
            }
            group.batches.Clear();
            m_GroupPool.Add(group);
        }

        private Path64 RentPath()
        {
            if (m_PathPool.Count > 0)
            {
                var p = m_PathPool[m_PathPool.Count - 1];
                m_PathPool.RemoveAt(m_PathPool.Count - 1);
                p.Clear();
                return p;
            }
            return new Path64(16);
        }
    }
}
