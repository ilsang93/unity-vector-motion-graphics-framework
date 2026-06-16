using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    public static class FillMeshBuilder
    {
        private static readonly List<Vector2> s_poly = new List<Vector2>(64);
        private static readonly List<int> s_capTris = new List<int>(128);

        public static void Build(VectorPath path, in FillStyle style, MeshBuffer mb)
        {
            if (!style.enabled) return;
            if (path == null || path.Count < 3) return;
            if (!path.closed) return; // open paths cannot be filled

            s_poly.Clear();
            for (int i = 0; i < path.Count; i++) s_poly.Add(path.nodes[i].position);

            Color32 col = style.color;
            int firstVert = mb.VertexCount;

            // FillTessellator picks the right strategy for the path:
            // ear-clipping on simple polylines (1 vertex per node), or
            // trapezoidal scanline on self-intersecting ones (4 vertices
            // per fill trapezoid). Either way the emitted vertices are
            // ours to push into the mesh buffer.
            FillTessellator.Triangulate(s_poly, mb.triangles, firstVert);
            var emitted = FillTessellator.GetEmittedVertices();
            // Interior vertices: distance = 1 so the SDF shader keeps them
            // fully opaque. The boundary ring emitted below carries the
            // 1→0 ramp.
            for (int i = 0; i < emitted.Count; i++) mb.AddVertex(emitted[i], col, 1f);

            // Skip the AA ring on a degenerate polygon — when one bounding
            // dimension collapses to zero (e.g. a rectangle authored at
            // size (W, 0)) the interior mesh has no area but the outset
            // ring still emits a band of OutsetWidthFor(...) on each side,
            // producing a visible line. A user who authored a zero-area
            // shape expects nothing to render. Detection: longer-side
            // threshold matches OutsetWidthFor's degenerate-clamp floor.
            if (PolygonIsDegenerate(s_poly)) return;

            // Outset AA ring along the original polyline. The interior side
            // (distance=1) sits exactly on the polyline so it z-orders flush
            // with the fill body; the outer side (distance=0) sits one
            // outset-width outside. The SDF shader collapses this band to
            // exactly the fwidth() of distance — i.e. 1 pixel — so the
            // band width itself doesn't matter visually as long as it
            // straddles the boundary.
            EmitAaRing(s_poly, OutsetWidthFor(s_poly), col, mb);
        }

        // A polygon counts as degenerate when either bounding dimension
        // collapses below a sub-pixel threshold. We only suppress the AA
        // ring (not the interior triangulation) — the interior is already
        // zero-area, so there's nothing left to render once the ring is
        // dropped. Threshold is well under one screen pixel at any sane
        // zoom so it never trips on real shapes.
        private static bool PolygonIsDegenerate(List<Vector2> poly)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < poly.Count; i++)
            {
                var v = poly[i];
                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
            }
            const float kEps = 1e-3f;
            return (maxX - minX) <= kEps || (maxY - minY) <= kEps;
        }

        // Boundary band width. Self-scaling against the polygon's larger
        // dimension so very small shapes still get a band wide enough for
        // the shader to interpolate, and large shapes don't blow out into
        // visible "fuzz". 0.5% of the longer side is well below 1 pixel
        // for any sane on-screen size at any sane zoom.
        private static float OutsetWidthFor(List<Vector2> poly)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < poly.Count; i++)
            {
                var v = poly[i];
                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
            }
            float d = Mathf.Max(maxX - minX, maxY - minY);
            // Lower bound keeps the ring usable when the path is degenerate
            // (e.g. a near-line trim result).
            return Mathf.Max(d * 0.005f, 1e-4f);
        }

        // CCW polygons: left-normal points inward (so the fill is to the
        // +normal side). The AA ring sits on the -normal (outward) side.
        private static Vector2[] s_ringNrm = new Vector2[64];
        private static Vector2[] EnsureNrmBuffer(int n)
        {
            if (s_ringNrm.Length < n) s_ringNrm = new Vector2[Mathf.NextPowerOfTwo(n)];
            return s_ringNrm;
        }

        private static void EmitAaRing(List<Vector2> poly, float w, Color32 col, MeshBuffer mb)
        {
            int n = poly.Count;
            if (n < 3 || w <= 0f) return;

            var nrm = EnsureNrmBuffer(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-6f) { nrm[i] = Vector2.up; continue; }
                d /= len;
                nrm[i] = new Vector2(-d.y, d.x); // left-normal: inward for CCW
            }

            // Inner ring sits exactly on the polyline (distance=1, fully
            // interior so the fill body and the ring agree on color). Outer
            // ring sits offset along -normal by w (distance=0, fully
            // transparent at the boundary). Two triangles per edge bridge
            // them; corners get bevel triangles so the band doesn't gap.
            //
            // Per-edge emit (no sharing between edges) keeps the corner
            // logic local — adjacent edges' outer rails generally don't
            // meet exactly, so a per-vertex outer rail would have to solve
            // the same miter/bevel decision the stroke builder does. The
            // bevel triangles between edges cover the gap directly.
            for (int seg = 0; seg < n; seg++)
            {
                Vector2 a = poly[seg];
                Vector2 b = poly[(seg + 1) % n];
                Vector2 nSeg = nrm[seg];
                Vector2 outA = a - nSeg * w;
                Vector2 outB = b - nSeg * w;

                int v0 = mb.VertexCount;
                mb.AddVertex(a, col, 1f);
                mb.AddVertex(b, col, 1f);
                mb.AddVertex(outB, col, 0f);
                mb.AddVertex(outA, col, 0f);
                // CCW polygon: -normal is outward. The quad (a, outA, outB,
                // b) winds CCW from +Z (the viewing direction of the front
                // face), so triangles (v0, v0+3, v0+2) and (v0, v0+2, v0+1).
                mb.AddTriangle(v0, v0 + 3, v0 + 2);
                mb.AddTriangle(v0, v0 + 2, v0 + 1);
            }

            // Corner gap-fillers: at each vertex P, the outer rails of the
            // incoming and outgoing edges diverge by the angle between their
            // normals. Cover the wedge with a single bevel triangle: (P,
            // P-nA*w, P-nB*w). Winding chosen so the triangle faces +Z.
            for (int v = 0; v < n; v++)
            {
                Vector2 p = poly[v];
                int prev = (v - 1 + n) % n;
                Vector2 nPrev = nrm[prev];
                Vector2 nNext = nrm[v];
                // Skip near-collinear vertices — the outer rails already
                // meet, no wedge to fill.
                float cross = nPrev.x * nNext.y - nPrev.y * nNext.x;
                if (Mathf.Abs(cross) < 1e-4f) continue;
                // cross > 0 (bend goes inward, i.e. +normal-ward): the
                // outer rails diverge, gap is on the outer side.
                // cross < 0 (bend outward): the outer rails overlap; the
                // bevel triangle ends up degenerate but harmless.
                Vector2 outPrev = p - nPrev * w;
                Vector2 outNext = p - nNext * w;
                int c = mb.VertexCount;
                mb.AddVertex(p, col, 1f);
                mb.AddVertex(outPrev, col, 0f);
                mb.AddVertex(outNext, col, 0f);
                // CCW polygon convex corner: cross > 0, outer rails diverge
                // — wedge winds (p, outNext, outPrev) as CCW from +Z. The
                // opposite sign means the rails overlap and the bevel
                // triangle ends up degenerate; harmless and we don't have
                // to special-case it.
                if (cross > 0f) mb.AddTriangle(c, c + 2, c + 1);
                else            mb.AddTriangle(c, c + 1, c + 2);
            }
        }

        /// 3D extrusion of the fill polygon. Emits front face (normal +Z),
        /// back face (normal -Z, reversed winding), and side walls (normal
        /// = each edge's outward-pointing 2D right-normal).
        ///
        /// All emitted vertices include explicit normals so the consumer
        /// must ensure the rest of the merged buffer is also normal-aware
        /// (see MeshBuffer.PromoteToZWithFrontNormal for the stroke side).
        public static void BuildExtruded(VectorPath path, in FillStyle style, in DepthStyle depth, MeshBuffer mb)
        {
            if (!style.enabled) return;
            if (path == null || path.Count < 3) return;
            if (!path.closed) return;

            depth.GetFaceZ(out float frontZ, out float backZ);
            Color32 col = style.color;

            s_poly.Clear();
            for (int i = 0; i < path.Count; i++) s_poly.Add(path.nodes[i].position);
            int n = s_poly.Count;

            // Triangulate the front face using FillTessellator so
            // self-intersecting paths (e.g. a star drawn as a single
            // continuous polyline) honor even-odd winding. The emitted
            // vertex set drives front + back face vertex emission — for
            // simple polygons it's the source verts 1:1; for self-
            // intersecting ones it's trapezoid corners.
            s_capTris.Clear();
            FillTessellator.Triangulate(s_poly, s_capTris, 0);
            var aug = FillTessellator.GetEmittedVertices();
            int augN = aug.Count;

            // Front face at frontZ. Reverse the per-triangle winding so
            // CCW source -> CW from the +Z viewing direction (= front).
            int frontBase = mb.VertexCount;
            for (int i = 0; i < augN; i++)
            {
                var p = aug[i];
                mb.AddVertex(new Vector3(p.x, p.y, frontZ), col, p, new Vector3(0f, 0f, 1f));
            }
            for (int i = 0; i + 2 < s_capTris.Count; i += 3)
            {
                mb.triangles.Add(frontBase + s_capTris[i]);
                mb.triangles.Add(frontBase + s_capTris[i + 2]);
                mb.triangles.Add(frontBase + s_capTris[i + 1]);
            }

            // Back face at backZ. Original CCW winding is correct here:
            // from -Z (the back face's outward direction) the polygon
            // appears mirror-flipped, so source-CCW reads as CW = front.
            int backBase = mb.VertexCount;
            for (int i = 0; i < augN; i++)
            {
                var p = aug[i];
                mb.AddVertex(new Vector3(p.x, p.y, backZ), col, p, new Vector3(0f, 0f, -1f));
            }
            for (int i = 0; i < s_capTris.Count; i++) mb.triangles.Add(backBase + s_capTris[i]);

            // Side walls: one quad per *original* closed-path edge. Walls
            // follow the authored polyline, not the augmented one — a
            // self-crossing at an intersection point is a topological
            // construct that shouldn't sprout side geometry. Users who
            // want side walls along every visible boundary edge of a
            // self-intersecting fill will need to author the path without
            // crossings.
            for (int i = 0; i < n; i++)
            {
                Vector2 a = s_poly[i];
                Vector2 b = s_poly[(i + 1) % n];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                d /= len;
                // CCW path: left-normal (-d.y, d.x) points inward,
                // so the outward side-wall normal is (d.y, -d.x).
                Vector3 nrm = new Vector3(d.y, -d.x, 0f);

                int v = mb.VertexCount;
                mb.AddVertex(new Vector3(a.x, a.y, frontZ), col, a, nrm);
                mb.AddVertex(new Vector3(b.x, b.y, frontZ), col, b, nrm);
                mb.AddVertex(new Vector3(b.x, b.y, backZ), col, b, nrm);
                mb.AddVertex(new Vector3(a.x, a.y, backZ), col, a, nrm);
                // CCW path: viewed from +nrm, the quad sequence
                // (a_f, b_f, b_b, a_b) traces CCW = back-facing in Unity.
                // Reverse to CW so the outward face is the front face.
                mb.AddTriangle(v, v + 3, v + 2);
                mb.AddTriangle(v, v + 2, v + 1);
            }
        }
    }
}
