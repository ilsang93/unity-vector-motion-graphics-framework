using System;
using UnityEngine;

namespace VMG.Animation.Core
{
    // Code-driven single-segment tween. Holds its own from/to values, easing
    // curve, and writer — no VMGAnimationTrack involved. Used by the fluent
    // VMG.Animate(target).To(path, value)... builder.
    //
    // Baseline (from) capture is lazy: if hasFrom == false at the first
    // Evaluate call inside the time window, we read the current value via
    // the reader and treat that as the start. This matches anime.js's
    // single-value behavior ("current -> value").
    internal class VMGCodeTween : VMGTweenBase
    {
        public VMGChannelWriter writer;
        public VMGChannelReader reader;
        public VMGChannelType channelType;
        public VMGEase ease;

        public bool hasFrom;

        // Typed value storage. Only the field matching channelType is read.
        public float fromFloat, toFloat;
        public int fromInt, toInt;
        public bool fromBool, toBool;
        public Color fromColor, toColor;
        public Vector4 fromVector, toVector;

        // FunctionValue slots. Each is optional; when set, the value is
        // resolved on first Evaluate (or after Refresh) instead of read
        // from the typed field above. anime.js parity: lazy from/to.
        public Func<float> fromFloatFn, toFloatFn;
        public Func<int> fromIntFn, toIntFn;
        public Func<bool> fromBoolFn, toBoolFn;
        public Func<Color> fromColorFn, toColorFn;
        public Func<Vector2> fromVector2Fn, toVector2Fn;
        public Func<Vector3> fromVector3Fn, toVector3Fn;
        public Func<Vector4> fromVector4Fn, toVector4Fn;

        // True iff any FunctionValue slot is set — fast path skip when no
        // tween in the animation has lazy values.
        public bool hasAnyFn;

        // When true, startTime/endTime were set by the builder (Keyframes
        // segment expansion) and EnsureFinalized must not overwrite them
        // with the default [0, dur] window.
        public bool hasExplicitSegment;

        // When true, the segment carries its own ease override (set on the
        // target keyframe). When false, EnsureFinalized fills in the
        // animation-level ease so user-set .Ease() calls reach the segment.
        public bool hasExplicitEase;

        // Skip-redundant-write cache, mirrors VMGClipTween.
        public bool hasLastValue;
        public float lastFloat;
        public int lastInt;
        public bool lastBool;
        public Color lastColor;
        public Vector4 lastVector;

        // Resolved-once flag for `to`-side FunctionValue. `from`-side reuses
        // hasFrom (same lifecycle as a normal lazy capture).
        bool m_HasResolvedTo;

        // Revert baseline is captured ONCE per channel per (re)play cycle,
        // independently of hasFrom — FunctionValue.Refresh() clears hasFrom
        // but must not move the revert origin (anime.js parity: revert()
        // returns to "before THIS animation began", not "before last
        // Refresh"). Cleared by VMGAnimation.Revert() so a Revert→Restart
        // recaptures.
        bool m_HasRegisteredRevert;

        public override void Evaluate(float iterationTime)
        {
            // Resolve to-side FunctionValue once (anime.js calls the function
            // when the animation begins, then caches). Refresh() clears
            // m_HasResolvedTo so the next Evaluate re-invokes.
            if (hasAnyFn && !m_HasResolvedTo) ResolveTo();

            // Revert baseline: take a reader snapshot once, before any write.
            // Goes through the same reader (not the FunctionValue from-side)
            // so revert truly restores the pre-animation value.
            if (!m_HasRegisteredRevert) RegisterRevertBaseline();

            // Lazy capture of "from" the first time we render inside the
            // window. anime.js does the same: a tween's start value is the
            // target's current value at the moment the tween begins.
            if (!hasFrom) CaptureFrom();

            float span = endTime - startTime;
            float local = span > 0f ? (iterationTime - startTime) / span : 1f;
            if (local < 0f) local = 0f;
            else if (local > 1f) local = 1f;

            float t = ease.Evaluate(local);

            switch (channelType)
            {
                case VMGChannelType.Float:
                {
                    float v = Mathf.LerpUnclamped(fromFloat, toFloat, t);
                    if (hasLastValue && lastFloat == v) return;
                    writer.Write(v);
                    lastFloat = v; hasLastValue = true;
                    return;
                }
                case VMGChannelType.Int:
                {
                    int v = Mathf.RoundToInt(Mathf.LerpUnclamped(fromInt, toInt, t));
                    if (hasLastValue && lastInt == v) return;
                    writer.Write(v);
                    lastInt = v; hasLastValue = true;
                    return;
                }
                case VMGChannelType.Bool:
                {
                    bool v = t >= 1f ? toBool : fromBool;
                    if (hasLastValue && lastBool == v) return;
                    writer.Write(v);
                    lastBool = v; hasLastValue = true;
                    return;
                }
                case VMGChannelType.Color:
                {
                    Color v = Color.LerpUnclamped(fromColor, toColor, t);
                    if (hasLastValue && lastColor == v) return;
                    writer.Write(v);
                    lastColor = v; hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector2:
                {
                    Vector4 v = Vector4.LerpUnclamped(fromVector, toVector, t);
                    if (hasLastValue && lastVector == v) return;
                    writer.Write((Vector2)v);
                    lastVector = v; hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector3:
                {
                    Vector4 v = Vector4.LerpUnclamped(fromVector, toVector, t);
                    if (hasLastValue && lastVector == v) return;
                    writer.Write((Vector3)v);
                    lastVector = v; hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector4:
                {
                    Vector4 v = Vector4.LerpUnclamped(fromVector, toVector, t);
                    if (hasLastValue && lastVector == v) return;
                    writer.Write(v);
                    lastVector = v; hasLastValue = true;
                    return;
                }
            }
        }

        void RegisterRevertBaseline()
        {
            m_HasRegisteredRevert = true;
            if (owner == null || writer == null || reader == null) return;
            object boxed = reader.Read();
            if (boxed == null) return;
            VMGAnimation.RevertWriteKind kind;
            switch (channelType)
            {
                case VMGChannelType.Float: kind = VMGAnimation.RevertWriteKind.Float; break;
                case VMGChannelType.Int: kind = VMGAnimation.RevertWriteKind.Int; break;
                case VMGChannelType.Bool: kind = VMGAnimation.RevertWriteKind.Bool; break;
                case VMGChannelType.Color: kind = VMGAnimation.RevertWriteKind.Color; break;
                case VMGChannelType.Vector2: kind = VMGAnimation.RevertWriteKind.Vector2; break;
                case VMGChannelType.Vector3: kind = VMGAnimation.RevertWriteKind.Vector3; break;
                case VMGChannelType.Vector4: kind = VMGAnimation.RevertWriteKind.Vector4; break;
                default: return;
            }
            owner.EnsureRevertBaseline(writer, kind, boxed);
        }

        // Called by VMGAnimation.Revert(): drop the captured flag so the
        // next play cycle re-snapshots the (now restored) channel.
        internal void ResetRevertCaptureFlag()
        {
            m_HasRegisteredRevert = false;
        }

        void CaptureFrom()
        {
            hasFrom = true;
            // FunctionValue on the from-side overrides the reader-based
            // baseline. Evaluated once per tween-window-entry (or per
            // Refresh).
            if (hasAnyFn)
            {
                switch (channelType)
                {
                    case VMGChannelType.Float: if (fromFloatFn != null) { fromFloat = fromFloatFn(); return; } break;
                    case VMGChannelType.Int: if (fromIntFn != null) { fromInt = fromIntFn(); return; } break;
                    case VMGChannelType.Bool: if (fromBoolFn != null) { fromBool = fromBoolFn(); return; } break;
                    case VMGChannelType.Color: if (fromColorFn != null) { fromColor = fromColorFn(); return; } break;
                    case VMGChannelType.Vector2: if (fromVector2Fn != null) { fromVector = fromVector2Fn(); return; } break;
                    case VMGChannelType.Vector3: if (fromVector3Fn != null) { fromVector = fromVector3Fn(); return; } break;
                    case VMGChannelType.Vector4: if (fromVector4Fn != null) { fromVector = fromVector4Fn(); return; } break;
                }
            }
            if (reader == null) return;
            object boxed = reader.Read();
            if (boxed == null) return;
            switch (channelType)
            {
                case VMGChannelType.Float: fromFloat = (float)boxed; break;
                case VMGChannelType.Int: fromInt = (int)boxed; break;
                case VMGChannelType.Bool: fromBool = (bool)boxed; break;
                case VMGChannelType.Color: fromColor = (Color)boxed; break;
                case VMGChannelType.Vector2: fromVector = (Vector2)boxed; break;
                case VMGChannelType.Vector3: fromVector = (Vector3)boxed; break;
                case VMGChannelType.Vector4: fromVector = (Vector4)boxed; break;
            }
        }

        void ResolveTo()
        {
            m_HasResolvedTo = true;
            switch (channelType)
            {
                case VMGChannelType.Float: if (toFloatFn != null) toFloat = toFloatFn(); break;
                case VMGChannelType.Int: if (toIntFn != null) toInt = toIntFn(); break;
                case VMGChannelType.Bool: if (toBoolFn != null) toBool = toBoolFn(); break;
                case VMGChannelType.Color: if (toColorFn != null) toColor = toColorFn(); break;
                case VMGChannelType.Vector2: if (toVector2Fn != null) toVector = toVector2Fn(); break;
                case VMGChannelType.Vector3: if (toVector3Fn != null) toVector = toVector3Fn(); break;
                case VMGChannelType.Vector4: if (toVector4Fn != null) toVector = toVector4Fn(); break;
            }
        }

        // Re-evaluate all FunctionValue slots on next Evaluate. Also drops
        // hasLastValue so the next write isn't suppressed by the
        // skip-redundant-write cache (the resolved value may equal the
        // cached one by coincidence, but a fresh resolve should write).
        public void Refresh()
        {
            if (!hasAnyFn) return;
            m_HasResolvedTo = false;
            hasFrom = false;
            hasLastValue = false;
        }
    }
}
