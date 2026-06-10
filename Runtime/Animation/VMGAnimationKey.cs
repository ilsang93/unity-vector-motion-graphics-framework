using System;
using UnityEngine;

namespace VMG.Animation
{
    [Serializable]
    public struct VMGAnimationKey
    {
        public float time;

        public float floatValue;
        public int intValue;
        public bool boolValue;
        public Color colorValue;
        public Vector4 vectorValue;

        public Vector2 inTangent;
        public Vector2 outTangent;
    }
}
