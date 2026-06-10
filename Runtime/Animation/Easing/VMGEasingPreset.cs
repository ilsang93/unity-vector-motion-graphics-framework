using UnityEngine;

namespace VMG.Animation
{
    public enum VMGEasingPreset
    {
        Custom = 0,
        Linear,
        Ease,
        EaseIn,
        EaseOut,
        EaseInOut,
        QuadIn,
        QuadOut,
        CubicIn,
        CubicOut,
        SineIn,
        SineOut,
        Hold,
    }

    public static class VMGEasingPresets
    {
        public static void GetTangents(VMGEasingPreset preset, out Vector2 outTangent, out Vector2 inTangent)
        {
            switch (preset)
            {
                case VMGEasingPreset.Linear:
                    outTangent = new Vector2(1f / 3f, 1f / 3f);
                    inTangent = new Vector2(1f / 3f, 1f / 3f);
                    return;
                case VMGEasingPreset.Ease:
                    outTangent = new Vector2(0.25f, 0.1f);
                    inTangent = new Vector2(0.25f, 1f);
                    return;
                case VMGEasingPreset.EaseIn:
                    outTangent = new Vector2(0.42f, 0f);
                    inTangent = new Vector2(1f, 1f);
                    return;
                case VMGEasingPreset.EaseOut:
                    outTangent = new Vector2(0f, 0f);
                    inTangent = new Vector2(0.58f, 1f);
                    return;
                case VMGEasingPreset.EaseInOut:
                    outTangent = new Vector2(0.42f, 0f);
                    inTangent = new Vector2(0.58f, 1f);
                    return;
                case VMGEasingPreset.QuadIn:
                    outTangent = new Vector2(0.11f, 0f);
                    inTangent = new Vector2(0.5f, 0f);
                    return;
                case VMGEasingPreset.QuadOut:
                    outTangent = new Vector2(0.5f, 1f);
                    inTangent = new Vector2(0.89f, 1f);
                    return;
                case VMGEasingPreset.CubicIn:
                    outTangent = new Vector2(0.32f, 0f);
                    inTangent = new Vector2(0.67f, 0f);
                    return;
                case VMGEasingPreset.CubicOut:
                    outTangent = new Vector2(0.33f, 1f);
                    inTangent = new Vector2(0.68f, 1f);
                    return;
                case VMGEasingPreset.SineIn:
                    outTangent = new Vector2(0.12f, 0f);
                    inTangent = new Vector2(0.39f, 0f);
                    return;
                case VMGEasingPreset.SineOut:
                    outTangent = new Vector2(0.61f, 1f);
                    inTangent = new Vector2(0.88f, 1f);
                    return;
                case VMGEasingPreset.Hold:
                    outTangent = new Vector2(1f, 0f);
                    inTangent = new Vector2(1f, 0f);
                    return;
                default:
                    outTangent = new Vector2(1f / 3f, 1f / 3f);
                    inTangent = new Vector2(1f / 3f, 1f / 3f);
                    return;
            }
        }
    }
}
