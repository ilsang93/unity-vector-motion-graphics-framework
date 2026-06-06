using UnityEngine;

namespace VMG.Core
{
    /// CPU procedural stroke. Produces a quad strip along the polyline with
    /// configurable alignment, cap and join styles.
    ///
    /// For CCW-wound shapes (every primitive this package emits is CCW), the
    /// per-edge left-normal points TOWARDS the shape's interior, so:
    ///   Inner  -> stroke grows on the +normal side (interior)
    ///   Outer  -> stroke grows on the -normal side (exterior)
    ///   Center -> ±w/2
    public static class StrokeMeshBuilder
    {
        private static Vector2[] s_nrm = new Vector2[0];
        private const int RoundCapSegments = 12;
        private const int RoundJoinSegments = 8;

        public static void Build(VectorPath path, in StrokeStyle style, MeshBuffer mb)
        {
            if (!style.enabled) return;
            if (path == null || path.Count < 2 || style.width <= 0f) return;

            int n = path.Count;
            bool closed = path.closed && n >= 3;

            float w = style.width;
            float offsetPos, offsetNeg;
            switch (style.alignment)
            {
                case StrokeAlignment.Inner: offsetPos = w; offsetNeg = 0f; break;
                case StrokeAlignment.Outer: offsetPos = 0f; offsetNeg = -w; break;
                default: offsetPos = w * 0.5f; offsetNeg = -w * 0.5f; break;
            }

            Color32 col = style.color;
            float miterLimit = Mathf.Max(1f, style.miterLimit);
            int segCount = closed ? n : n - 1;

            // Per-segment left-normals.
            if (s_nrm.Length < segCount) s_nrm = new Vector2[Mathf.NextPowerOfTwo(segCount)];
            for (int i = 0; i < segCount; i++)
            {
                Vector2 a = path.nodes[i].position;
                Vector2 b = path.nodes[(i + 1) % n].position;
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-6f) { s_nrm[i] = Vector2.up; continue; }
                d /= len;
                s_nrm[i] = new Vector2(-d.y, d.x);
            }

            // Build per-edge "ribbon" of two vertices at each end. Caps and
            // joins are then emitted as separate geometry — adjacent edges
            // never share vertices, so the join style is free to pick any
            // bridge geometry.
            for (int seg = 0; seg < segCount; seg++)
            {
                Vector2 a = path.nodes[seg].position;
                Vector2 b = path.nodes[(seg + 1) % n].position;
                Vector2 n0 = s_nrm[seg];
                int v0 = mb.VertexCount;
                mb.AddVertex(a + n0 * offsetPos, col);
                mb.AddVertex(a + n0 * offsetNeg, col);
                mb.AddVertex(b + n0 * offsetPos, col);
                mb.AddVertex(b + n0 * offsetNeg, col);
                // Quad (v0, v2, v3, v1) -> two triangles.
                mb.AddTriangle(v0, v0 + 2, v0 + 3);
                mb.AddTriangle(v0, v0 + 3, v0 + 1);
            }

            // Joins between consecutive edges.
            int joinCount = closed ? segCount : segCount - 1;
            for (int j = 0; j < joinCount; j++)
            {
                int segA = j;
                int segB = (j + 1) % segCount;
                Vector2 cornerP = path.nodes[(j + 1) % n].position;
                EmitJoin(mb, cornerP, s_nrm[segA], s_nrm[segB], offsetPos, offsetNeg, col, style.join, miterLimit);
            }

            // Caps for open paths.
            if (!closed)
            {
                // Start cap: at path.nodes[0], outgoing edge dir = s_nrm[0]'s perpendicular.
                Vector2 startP = path.nodes[0].position;
                Vector2 startN = s_nrm[0];
                Vector2 startDir = new Vector2(startN.y, -startN.x); // back-pointing
                EmitCap(mb, startP, startN, startDir, offsetPos, offsetNeg, col, style.cap);

                // End cap.
                int lastSeg = segCount - 1;
                Vector2 endP = path.nodes[n - 1].position;
                Vector2 endN = s_nrm[lastSeg];
                Vector2 endDir = new Vector2(-endN.y, endN.x); // forward-pointing
                EmitCap(mb, endP, endN, endDir, offsetPos, offsetNeg, col, style.cap);
            }
        }

        private static void EmitJoin(MeshBuffer mb, Vector2 p, Vector2 nA, Vector2 nB,
                                     float offPos, float offNeg, Color32 col,
                                     LineJoin join, float miterLimit)
        {
            // Determine which side is the "outer" side of the bend (the side
            // where edges diverge — that's where the join geometry sits).
            float cross = nA.x * nB.y - nA.y * nB.x; // >0: bend toward +normal, <0: bend toward -normal
            if (Mathf.Abs(cross) < 1e-4f) return; // collinear

            // Common reference points: the two outgoing/incoming ribbon corners
            // on both sides of the corner.
            Vector2 posA = p + nA * offPos;
            Vector2 negA = p + nA * offNeg;
            Vector2 posB = p + nB * offPos;
            Vector2 negB = p + nB * offNeg;

            switch (join)
            {
                case LineJoin.Bevel:
                    EmitBevel(mb, p, posA, negA, posB, negB, cross, col);
                    break;
                case LineJoin.Round:
                    EmitRoundJoin(mb, p, nA, nB, offPos, offNeg, cross, col);
                    break;
                default:
                    EmitMiter(mb, p, nA, nB, posA, negA, posB, negB, offPos, offNeg, cross, col, miterLimit);
                    break;
            }
        }

        private static void EmitBevel(MeshBuffer mb, Vector2 p,
                                      Vector2 posA, Vector2 negA, Vector2 posB, Vector2 negB,
                                      float cross, Color32 col)
        {
            int c = mb.VertexCount;
            if (cross > 0f)
            {
                // Bend toward +normal side; +normal side is "inner" of bend,
                // so the gap is on -normal side: bridge (negA, p, negB).
                mb.AddVertex(negA, col); mb.AddVertex(p, col); mb.AddVertex(negB, col);
                mb.AddTriangle(c, c + 1, c + 2);
            }
            else
            {
                mb.AddVertex(posA, col); mb.AddVertex(posB, col); mb.AddVertex(p, col);
                mb.AddTriangle(c, c + 1, c + 2);
            }
        }

        private static void EmitMiter(MeshBuffer mb, Vector2 p, Vector2 nA, Vector2 nB,
                                      Vector2 posA, Vector2 negA, Vector2 posB, Vector2 negB,
                                      float offPos, float offNeg, float cross, Color32 col, float miterLimit)
        {
            // The outer side of the bend is where the two ribbons diverge.
            // cross > 0: bend goes +normal-ward, so outer side is -normal.
            // cross < 0: outer side is +normal.
            float outerOffset = cross > 0f ? offNeg : offPos;
            Vector2 outerA = cross > 0f ? negA : posA;
            Vector2 outerB = cross > 0f ? negB : posB;

            // Each ribbon's outer rail is a line through outerA/outerB along
            // the original edge direction (perpendicular to its normal).
            // Forward dir of an edge whose left-normal is n is (n.y, -n.x).
            Vector2 dirA = new Vector2(nA.y, -nA.x);
            Vector2 dirB = new Vector2(nB.y, -nB.x);

            // Intersect rayA (going forward from outerA) with rayB (going
            // backward from outerB, i.e. -dirB). The miter spike sits at the
            // intersection of those two infinite lines.
            // Solve outerA + t*dirA = outerB - s*dirB.
            float denom = dirA.x * dirB.y - dirA.y * dirB.x;
            if (Mathf.Abs(denom) < 1e-6f)
            {
                EmitBevel(mb, p, posA, negA, posB, negB, cross, col);
                return;
            }
            Vector2 delta = outerB - outerA;
            float t = (delta.x * dirB.y - delta.y * dirB.x) / denom;
            Vector2 spike = outerA + dirA * t;

            // Miter length / stroke half-width tells us how spiky this corner
            // got. Fall back to bevel when it exceeds the limit.
            float spikeDist = Vector2.Distance(spike, p);
            float halfWidth = Mathf.Abs(outerOffset);
            if (halfWidth > 1e-5f && spikeDist / halfWidth > miterLimit)
            {
                EmitBevel(mb, p, posA, negA, posB, negB, cross, col);
                return;
            }

            // Triangle pair filling the outer wedge: (outerA, spike, p) and
            // (spike, outerB, p). Winding chosen so the wedge faces the same
            // way for either bend direction.
            int c = mb.VertexCount;
            mb.AddVertex(outerA, col); mb.AddVertex(spike, col); mb.AddVertex(outerB, col); mb.AddVertex(p, col);
            if (cross > 0f)
            {
                mb.AddTriangle(c, c + 1, c + 3);
                mb.AddTriangle(c + 1, c + 2, c + 3);
            }
            else
            {
                mb.AddTriangle(c, c + 3, c + 1);
                mb.AddTriangle(c + 1, c + 3, c + 2);
            }
        }

        private static void EmitRoundJoin(MeshBuffer mb, Vector2 p, Vector2 nA, Vector2 nB,
                                          float offPos, float offNeg, float cross, Color32 col)
        {
            // Fan on the outer side of the bend, pivoting around p.
            // Outer side is -normal when bend goes +normal-ward, +normal otherwise.
            float r = cross > 0f ? -offNeg : offPos;
            Vector2 startVec = cross > 0f ? -nA : nA;
            Vector2 endVec   = cross > 0f ? -nB : nB;
            float aStart = Mathf.Atan2(startVec.y, startVec.x);
            float aEnd = Mathf.Atan2(endVec.y, endVec.x);
            float delta = aEnd - aStart;
            if (delta > Mathf.PI) delta -= Mathf.PI * 2f;
            else if (delta < -Mathf.PI) delta += Mathf.PI * 2f;

            int seg = Mathf.Max(2, RoundJoinSegments);
            int pivot = mb.VertexCount;
            mb.AddVertex(p, col);
            for (int s = 0; s <= seg; s++)
            {
                float t = aStart + delta * (s / (float)seg);
                Vector2 v = p + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
                mb.AddVertex(v, col);
                if (s > 0)
                {
                    int last = pivot + s;
                    int prev = last - 1;
                    if (delta > 0f) mb.AddTriangle(pivot, prev, last);
                    else mb.AddTriangle(pivot, last, prev);
                }
            }
        }

        private static void EmitCap(MeshBuffer mb, Vector2 p, Vector2 n, Vector2 outwardDir,
                                    float offPos, float offNeg, Color32 col, LineCap cap)
        {
            if (cap == LineCap.Butt) return;

            Vector2 vPos = p + n * offPos;
            Vector2 vNeg = p + n * offNeg;

            if (cap == LineCap.Square)
            {
                float ext = (offPos - offNeg) * 0.5f;
                Vector2 extPos = vPos + outwardDir * ext;
                Vector2 extNeg = vNeg + outwardDir * ext;
                int c = mb.VertexCount;
                mb.AddVertex(vPos, col); mb.AddVertex(extPos, col);
                mb.AddVertex(extNeg, col); mb.AddVertex(vNeg, col);
                mb.AddTriangle(c, c + 1, c + 2);
                mb.AddTriangle(c, c + 2, c + 3);
                return;
            }

            // Round cap: half-disc fan pivoting on the centerline point.
            // Build samples around the cap center starting at vPos and ending
            // at vNeg, choosing the sweep direction whose midpoint lies on the
            // outwardDir side.
            Vector2 center = p + n * ((offPos + offNeg) * 0.5f);
            float r = (offPos - offNeg) * 0.5f;
            float aStart = Mathf.Atan2((vPos - center).y, (vPos - center).x);
            // Sweep ±π — pick the sign whose midpoint points along outwardDir.
            float midAng = aStart + Mathf.PI * 0.5f;
            Vector2 midDir = new Vector2(Mathf.Cos(midAng), Mathf.Sin(midAng));
            float delta = Vector2.Dot(midDir, outwardDir) >= 0f ? Mathf.PI : -Mathf.PI;

            int seg = Mathf.Max(3, RoundCapSegments);
            int pivot = mb.VertexCount;
            mb.AddVertex(center, col);
            for (int s = 0; s <= seg; s++)
            {
                float t = aStart + delta * (s / (float)seg);
                mb.AddVertex(center + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r, col);
                if (s > 0)
                {
                    int last = pivot + s + 1;
                    int prev = last - 1;
                    if (delta > 0f) mb.AddTriangle(pivot, prev, last);
                    else mb.AddTriangle(pivot, last, prev);
                }
            }
        }
    }
}
