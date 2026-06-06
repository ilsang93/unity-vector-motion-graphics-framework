using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    public sealed class MeshBuffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>(256);
        public readonly List<Color32> colors = new List<Color32>(256);
        public readonly List<Vector2> uvs = new List<Vector2>(256);
        public readonly List<int> triangles = new List<int>(512);

        public void Clear()
        {
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();
        }

        public int VertexCount => vertices.Count;

        public void AddVertex(Vector2 p, Color32 c)
        {
            vertices.Add(new Vector3(p.x, p.y, 0f));
            colors.Add(c);
            uvs.Add(p);
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
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
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
