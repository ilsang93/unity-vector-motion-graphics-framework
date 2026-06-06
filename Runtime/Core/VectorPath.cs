using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    public sealed class VectorPath
    {
        public readonly List<VectorNode> nodes = new List<VectorNode>(16);
        public bool closed;

        public void Clear()
        {
            nodes.Clear();
            closed = false;
        }

        public void Add(Vector2 p)
        {
            nodes.Add(VectorNode.Corner(p));
        }

        public void Add(VectorNode n)
        {
            nodes.Add(n);
        }

        public int Count => nodes.Count;

        public Vector2 GetPoint(int i) => nodes[i].position;

        public float TotalLength()
        {
            int count = nodes.Count;
            if (count < 2) return 0f;
            float len = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                len += Vector2.Distance(nodes[i].position, nodes[i + 1].position);
            }
            if (closed)
            {
                len += Vector2.Distance(nodes[count - 1].position, nodes[0].position);
            }
            return len;
        }

        public void CopyFrom(VectorPath other)
        {
            nodes.Clear();
            for (int i = 0; i < other.nodes.Count; i++) nodes.Add(other.nodes[i]);
            closed = other.closed;
        }
    }
}
