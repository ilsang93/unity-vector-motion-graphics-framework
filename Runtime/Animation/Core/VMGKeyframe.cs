using UnityEngine;

namespace VMG.Animation.Core
{
    // One keyframe in a VMGAnimate.Keyframes(...) call. Time is normalized to
    // [0, 1] across the animation's iteration duration. The optional ease,
    // when set, applies to the segment ENDING at this keyframe (anime.js
    // parity — matches how VMGFx DSL keyframes blocks resolve per-segment
    // easing). When null, the animation's top-level Ease() is used.
    public struct VMGKeyframe<T>
    {
        public float Time;
        public T Value;
        public VMGEase? Ease;

        public VMGKeyframe(float time, T value)
        {
            Time = time;
            Value = value;
            Ease = null;
        }

        public VMGKeyframe(float time, T value, VMGEase ease)
        {
            Time = time;
            Value = value;
            Ease = ease;
        }

        public VMGKeyframe(float time, T value, VMGEasingPreset ease)
        {
            Time = time;
            Value = value;
            Ease = VMGEase.From(ease);
        }
    }
}
