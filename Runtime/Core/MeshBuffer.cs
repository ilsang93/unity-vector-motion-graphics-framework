using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    public sealed class MeshBuffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>(256);
        public readonly List<Color32> colors = new List<Color32>(256);
        public readonly List<Vector2> uvs = new List<Vector2>(256);
        // UV1.x carries the AA distance channel: 1 = fully interior, 0 = at
        // the visible boundary. The SDF shader fades alpha across the
        // fwidth(distance) band so edges land on the pixel grid regardless
        // of zoom. UV1.y is reserved.
        public readonly List<Vector2> uv1s = new List<Vector2>(256);
        public readonly List<Vector3> normals = new List<Vector3>(256);
        public readonly List<int> triangles = new List<int>(512);

        public void Clear()
        {
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            uv1s.Clear();
            normals.Clear();
            triangles.Clear();
        }

        public int VertexCount => vertices.Count;

        /// Append another buffer's geometry into this one, offsetting its
        /// triangle indices. Normals are copied only when both buffers either
        /// use them or don't, so a mixed (some-normal) state isn't created —
        /// callers that need normals must ensure both buffers are normal-aware.
        public void Append(MeshBuffer other)
        {
            if (other == null || other.vertices.Count == 0) return;
            int baseV = vertices.Count;
            vertices.AddRange(other.vertices);
            colors.AddRange(other.colors);
            uvs.AddRange(other.uvs);
            uv1s.AddRange(other.uv1s);
            // Keep normals consistent: only carry them over if this buffer
            // already tracks one-per-vertex and the other does too.
            bool thisHasNormals = normals.Count == baseV;
            bool otherHasNormals = other.normals.Count == other.vertices.Count;
            if (thisHasNormals && otherHasNormals) normals.AddRange(other.normals);
            for (int i = 0; i < other.triangles.Count; i++)
                triangles.Add(baseV + other.triangles[i]);
        }

        public void AddVertex(Vector2 p, Color32 c)
        {
            vertices.Add(new Vector3(p.x, p.y, 0f));
            colors.Add(c);
            uvs.Add(p);
            uv1s.Add(new Vector2(1f, 0f));
        }

        /// Add an interior or AA-ring vertex with explicit distance value.
        /// distance: 1 = fully interior (opaque), 0 = at visible boundary.
        public void AddVertex(Vector2 p, Color32 c, float distance)
        {
            vertices.Add(new Vector3(p.x, p.y, 0f));
            colors.Add(c);
            uvs.Add(p);
            uv1s.Add(new Vector2(distance, 0f));
        }

        /// Add a vertex with an explicit Z and normal. Use when emitting
        /// depth-extruded geometry; the consumer is then responsible for
        /// keeping normals.Count == vertices.Count across the whole buffer.
        public void AddVertex(Vector3 p, Color32 c, Vector2 uv, Vector3 normal)
        {
            vertices.Add(p);
            colors.Add(c);
            uvs.Add(uv);
            uv1s.Add(new Vector2(1f, 0f));
            normals.Add(normal);
        }

        public void AddTriangle(int a, int b, int c)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        public void AddQuad(int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        public void ApplyTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            if (uv1s.Count == vertices.Count && vertices.Count > 0)
                mesh.SetUVs(1, uv1s);
            mesh.SetTriangles(triangles, 0);
            // Only push normals if every vertex got one — mixing extruded
            // and flat 2D geometry in one buffer leaves normals partial,
            // in which case the mesh stays normal-less (same as before).
            if (normals.Count == vertices.Count && vertices.Count > 0)
                mesh.SetNormals(normals);
            mesh.RecalculateBounds();
        }

        /// Recolor vertices [fromVertex, end) by evaluating a two-stop
        /// gradient at each vertex's path-space position, then multiplying by
        /// tint. Must run BEFORE NormalizeUVsToRect — it reads each vertex's
        /// original 2D position from uvs[i] (AddVertex stores position there
        /// as a placeholder). `bounds` is the rect the gradient maps across
        /// (typically the renderer's fill+stroke union bounds so fill and
        /// stroke gradients stay aligned).
        public void ApplyGradient(in VMGGradient gradient, Rect bounds, Color tint, int fromVertex)
        {
            if (fromVertex < 0) fromVertex = 0;
            for (int i = fromVertex; i < colors.Count; i++)
            {
                Color c = gradient.Evaluate(uvs[i], bounds) * tint;
                colors[i] = c;
            }
        }

        /// Rewrite UV0 so that each vertex's UV is its position remapped into
        /// [0,1] across the given rect. Used after all geometry has been
        /// emitted (AddVertex copies position into UV as a placeholder) so
        /// the result is suitable for texture sampling.
        public void NormalizeUVsToRect(Rect rect)
        {
            if (vertices.Count == 0) return;
            float w = rect.width;
            float h = rect.height;
            float invW = w > 1e-6f ? 1f / w : 0f;
            float invH = h > 1e-6f ? 1f / h : 0f;
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                uvs[i] = new Vector2((v.x - rect.xMin) * invW, (v.y - rect.yMin) * invH);
            }
        }

        /// Promote every flat 2D vertex to a 3D vertex with the given Z
        /// and a front-facing normal. Used when merging a buffer built by
        /// a 2D-only emitter (e.g. StrokeMeshBuilder) into a depth-aware
        /// combined buffer — keeps normals.Count == vertices.Count.
        public void PromoteToZWithFrontNormal(float z)
        {
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                vertices[i] = new Vector3(v.x, v.y, z);
            }
            normals.Clear();
            for (int i = 0; i < vertices.Count; i++) normals.Add(new Vector3(0f, 0f, 1f));
        }

        /// Copy every vertex/color/uv/triangle from `src` into this buffer.
        /// Used when stroke geometry must be duplicated for front+back face
        /// in depth-extruded mode. Triangle indices are offset to point at
        /// the new vertex range. Normals/Z are left as-is — the caller
        /// adjusts them via PromoteToZWithFrontNormal afterwards.
        public void CopyFrom(MeshBuffer src)
        {
            Clear();
            for (int i = 0; i < src.vertices.Count; i++)
            {
                vertices.Add(src.vertices[i]);
                colors.Add(src.colors[i]);
                uvs.Add(src.uvs[i]);
            }
            for (int i = 0; i < src.uv1s.Count; i++) uv1s.Add(src.uv1s[i]);
            for (int i = 0; i < src.normals.Count; i++) normals.Add(src.normals[i]);
            for (int i = 0; i < src.triangles.Count; i++) triangles.Add(src.triangles[i]);
        }

        /// Reverse the winding of every triangle and flip the Z component
        /// of every vertex normal. Use after PromoteToZWithFrontNormal()
        /// when the buffer is being placed on the back face — the geometry
        /// must read as CW (front-facing) when viewed from -Z.
        public void FlipForBackFace()
        {
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int t1 = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = t1;
            }
            for (int i = 0; i < normals.Count; i++)
            {
                var n = normals[i];
                normals[i] = new Vector3(n.x, n.y, -n.z);
            }
        }

        /// Same as NormalizeUVsToRect but the rect is computed from the
        /// existing vertex positions' axis-aligned bounds.
        public void NormalizeUVsToVertexBounds()
        {
            if (vertices.Count == 0) return;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                if (v.x < minX) minX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.x > maxX) maxX = v.x;
                if (v.y > maxY) maxY = v.y;
            }
            NormalizeUVsToRect(new Rect(minX, minY, maxX - minX, maxY - minY));
        }
    }
}
