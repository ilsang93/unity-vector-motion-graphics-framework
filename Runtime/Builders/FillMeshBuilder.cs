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
            for (int i = 0; i < s_poly.Count; i++) mb.AddVertex(s_poly[i], col);

            EarClippingTriangulator.Triangulate(s_poly, mb.triangles, firstVert);
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

            // Triangulate once in CCW source order. Ear clipping always
            // emits CCW triangles. Unity treats CW as front-facing.
            s_capTris.Clear();
            EarClippingTriangulator.Triangulate(s_poly, s_capTris, 0);

            // Front face at frontZ. Reverse the per-triangle winding so
            // CCW source -> CW from the +Z viewing direction (= front).
            int frontBase = mb.VertexCount;
            for (int i = 0; i < n; i++)
            {
                var p = s_poly[i];
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
            for (int i = 0; i < n; i++)
            {
                var p = s_poly[i];
                mb.AddVertex(new Vector3(p.x, p.y, backZ), col, p, new Vector3(0f, 0f, -1f));
            }
            for (int i = 0; i < s_capTris.Count; i++) mb.triangles.Add(backBase + s_capTris[i]);

            // Side walls: one quad per closed-path edge. Each quad gets
            // its own 4 vertices because the outward normal differs per
            // edge — sharing with neighbors would average normals and
            // ruin flat-shaded side lighting.
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
