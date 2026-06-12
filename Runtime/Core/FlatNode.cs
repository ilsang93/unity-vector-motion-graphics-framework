using System;
using UnityEngine;

namespace VMG.Core
{
    /// AnimationClip-friendly flattened equivalent of VectorNode. Used by
    /// PrimitiveShapeSource as 64 individual fields (Node00..Node63)
    /// because Unity's Animation window exposes named struct fields but
    /// not List<T> or T[] element fields. Each field of this struct
    /// (position.x, inTangent.y, ...) becomes a keyframable channel.
    [Serializable]
    public struct FlatNode
    {
        public Vector2 position;
        public Vector2 inTangent;
        public Vector2 outTangent;
        public NodeType type;

        public VectorNode ToVectorNode()
        {
            return new VectorNode
            {
                position = position,
                inTangent = inTangent,
                outTangent = outTangent,
                type = type,
            };
        }

        public static FlatNode From(VectorNode n)
        {
            return new FlatNode
            {
                position = n.position,
                inTangent = n.inTangent,
                outTangent = n.outTangent,
                type = n.type,
            };
        }
    }
}
