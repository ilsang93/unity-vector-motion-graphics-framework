using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    public static class FillMeshBuilder
    {
        private static readonly List<Vector2> s_poly = new List<Vector2>(64);

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
    }
}
