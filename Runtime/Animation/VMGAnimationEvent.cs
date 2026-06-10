using System;
using UnityEngine.Events;

namespace VMG.Animation
{
    [Serializable]
    public class VMGAnimationEvent
    {
        public float time;
        public string label;
        public UnityEvent invoke = new UnityEvent();
    }
}
