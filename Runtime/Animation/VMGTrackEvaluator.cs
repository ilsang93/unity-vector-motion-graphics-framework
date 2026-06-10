using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation
{
    internal static class VMGTrackEvaluator
    {
        public static float EvaluateFloat(VMGAnimationTrack track, float time)
        {
            var keys = track.keys;
            if (keys == null || keys.Count == 0) return 0f;
            if (keys.Count == 1) return keys[0].floatValue;
            if (FindSegment(keys, time, out var a, out var b, out var u))
            {
                float ease = VMGEasing.Evaluate(a.outTangent, b.inTangent, u);
                return Mathf.LerpUnclamped(a.floatValue, b.floatValue, ease);
            }
            return time <= keys[0].time ? keys[0].floatValue : keys[keys.Count - 1].floatValue;
        }

        public static int EvaluateInt(VMGAnimationTrack track, float time)
        {
            var keys = track.keys;
            if (keys == null || keys.Count == 0) return 0;
            if (keys.Count == 1) return keys[0].intValue;
            if (FindSegment(keys, time, out var a, out var b, out var u))
            {
                float ease = VMGEasing.Evaluate(a.outTangent, b.inTangent, u);
                return Mathf.RoundToInt(Mathf.LerpUnclamped(a.intValue, b.intValue, ease));
            }
            return time <= keys[0].time ? keys[0].intValue : keys[keys.Count - 1].intValue;
        }

        public static bool EvaluateBool(VMGAnimationTrack track, float time)
        {
            var keys = track.keys;
            if (keys == null || keys.Count == 0) return false;
            if (keys.Count == 1) return keys[0].boolValue;
            // Step-hold: take the last key whose time <= requested time.
            if (time <= keys[0].time) return keys[0].boolValue;
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                if (keys[i].time <= time) return keys[i].boolValue;
            }
            return keys[0].boolValue;
        }

        public static Color EvaluateColor(VMGAnimationTrack track, float time)
        {
            var keys = track.keys;
            if (keys == null || keys.Count == 0) return Color.white;
            if (keys.Count == 1) return keys[0].colorValue;
            if (FindSegment(keys, time, out var a, out var b, out var u))
            {
                float ease = VMGEasing.Evaluate(a.outTangent, b.inTangent, u);
                return Color.LerpUnclamped(a.colorValue, b.colorValue, ease);
            }
            return time <= keys[0].time ? keys[0].colorValue : keys[keys.Count - 1].colorValue;
        }

        public static Vector4 EvaluateVector(VMGAnimationTrack track, float time)
        {
            var keys = track.keys;
            if (keys == null || keys.Count == 0) return Vector4.zero;
            if (keys.Count == 1) return keys[0].vectorValue;
            if (FindSegment(keys, time, out var a, out var b, out var u))
            {
                float ease = VMGEasing.Evaluate(a.outTangent, b.inTangent, u);
                return Vector4.LerpUnclamped(a.vectorValue, b.vectorValue, ease);
            }
            return time <= keys[0].time ? keys[0].vectorValue : keys[keys.Count - 1].vectorValue;
        }

        static bool FindSegment(List<VMGAnimationKey> keys, float time, out VMGAnimationKey a, out VMGAnimationKey b, out float u)
        {
            a = default;
            b = default;
            u = 0f;
            int n = keys.Count;
            if (time <= keys[0].time || time >= keys[n - 1].time) return false;

            int lo = 0;
            int hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (keys[mid].time <= time) lo = mid;
                else hi = mid;
            }

            a = keys[lo];
            b = keys[hi];
            float span = b.time - a.time;
            u = span > 0f ? (time - a.time) / span : 0f;
            return true;
        }
    }
}
