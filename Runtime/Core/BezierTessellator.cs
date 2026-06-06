using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// Converts a node list with cubic Bezier tangents into a flat polyline.
    /// Segments between two consecutive nodes whose tangents are zero collapse
    /// to a straight line; otherwise the cubic A, A+outTangent, B+inTangent, B
    /// is sampled.
    ///
    /// All builders/modifiers downstream of the source operate on the flat
    /// polyline produced here — Bezier control points exist only at authoring
    /// time.
    public static class BezierTessellator
    {
        private const float MinTangentSqr = 1e-8f;

        /// Tessellates nodes into outPath as a polyline. `samplesPerSegment`
        /// controls subdivision of curved segments only; straight segments
        /// stay as a single edge.
        public static void Tessellate(IList<VectorNode> nodes, bool closed, int samplesPerSegment, VectorPath outPath)
        {
            outPath.nodes.Clear();
            int n = nodes.Count;
            // A "closed" path with fewer than 3 nodes degenerates: closing 2
            // nodes traces the same curve back and forth (looks like a leaf),
            // closing 1 node is a point. Demote those cases to open.
            if (n < 3) closed = false;
            outPath.closed = closed;
            if (n == 0) return;
            if (n == 1) { outPath.Add(nodes[0].position); return; }

            int segCount = closed ? n : n - 1;
            int samples = Mathf.Max(2, samplesPerSegment);

            outPath.Add(nodes[0].position);
            for (int i = 0; i < segCount; i++)
            {
                VectorNode a = nodes[i];
                VectorNode b = nodes[(i + 1) % n];
                bool curved = a.outTangent.sqrMagnitude > MinTangentSqr
                              || b.inTangent.sqrMagnitude > MinTangentSqr;
                if (!curved)
                {
                    if (i < segCount - 1 || !closed) outPath.Add(b.position);
                    continue;
                }

                Vector2 p0 = a.position;
                Vector2 p1 = a.position + a.outTangent;
                Vector2 p2 = b.position + b.inTangent;
                Vector2 p3 = b.position;
                // Emit intermediate samples; final sample (t=1) is the next
                // node's position, which is added by the next iteration (or
                // is the seam on a closed path, in which case we drop it).
                for (int s = 1; s < samples; s++)
                {
                    float t = s / (float)samples;
                    outPath.Add(EvalCubic(p0, p1, p2, p3, t));
                }
                if (i < segCount - 1 || !closed) outPath.Add(p3);
            }
        }

        public static Vector2 EvalCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;
            return uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3;
        }
    }
}
