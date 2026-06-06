using System;
using UnityEngine;

namespace VMG.Core
{
    public enum NodeType
    {
        Corner = 0,
        Smooth = 1,
        Bezier = 2,
    }

    [Serializable]
    public struct VectorNode
    {
        public Vector2 position;
        public Vector2 inTangent;
        public Vector2 outTangent;
        public NodeType type;

        public static VectorNode Corner(Vector2 p)
        {
            return new VectorNode
            {
                position = p,
                inTangent = Vector2.zero,
                outTangent = Vector2.zero,
                type = NodeType.Corner,
            };
        }
    }
}
