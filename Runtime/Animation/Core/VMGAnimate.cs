using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VMG.Animation.Core
{
    // Fluent builder + handle for a code-driven animation. Returned by
    // VMG.Animate(target) and chained with .To / .Duration / .Ease / etc.
    //
    // anime.js parity:
    // - "single value" To = lazy capture of current -> value
    // - auto-play on construction (deferred to next LateUpdate so a Timeline
    //   can claim ownership before it starts ticking standalone)
    // - composition is "later wins": tweens evaluate in add order so the
    //   last-registered write on a channel is what survives a frame
    public class VMGAnimate
    {
        readonly Component m_Target;
        readonly VMGAnimation m_Anim;
        readonly List<VMGCodeTween> m_PendingTweens = new List<VMGCodeTween>();

        // Pending top-level params. Applied to each tween at Finalize time.
        // Mirrors anime.js's pattern of merging defaults at child-init.
        float m_Duration = 0.3f;
        bool m_HasDurationUserSet;
        float m_Delay;
        bool m_HasDelayUserSet;
        float m_EndDelay;
        VMGEase m_Ease = VMGEase.From(VMGEasingPreset.Ease);
        bool m_HasEase;

        bool m_Finalized;
        bool m_OwnedByTimeline;

        TaskCompletionSource<bool> m_CompletionSource;

        internal VMGAnimate(Component target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            m_Target = target;
            m_Anim = new VMGAnimation
            {
                iterationDuration = 0.0001f, // grows on Finalize
                iterationCount = 1f,
            };
            // Deferred so a Timeline can call ClaimByTimeline() before the
            // engine starts ticking us standalone.
            VMGEngine.RegisterDeferred(this);
        }

        // ------- Authoring API (.To / .FromTo / .Set) -------

        public VMGAnimate To(string path, float value) => AddTween(path, VMGChannelType.Float, hasFrom: false, toFloat: value);
        public VMGAnimate To(string path, int value) => AddTween(path, VMGChannelType.Int, hasFrom: false, toInt: value);
        public VMGAnimate To(string path, bool value) => AddTween(path, VMGChannelType.Bool, hasFrom: false, toBool: value);
        public VMGAnimate To(string path, Color value) => AddTween(path, VMGChannelType.Color, hasFrom: false, toColor: value);
        public VMGAnimate To(string path, Vector2 value) => AddTween(path, VMGChannelType.Vector2, hasFrom: false, toVector: value);
        public VMGAnimate To(string path, Vector3 value) => AddTween(path, VMGChannelType.Vector3, hasFrom: false, toVector: value);
        public VMGAnimate To(string path, Vector4 value) => AddTween(path, VMGChannelType.Vector4, hasFrom: false, toVector: value);

        public VMGAnimate FromTo(string path, float from, float to) => AddTween(path, VMGChannelType.Float, hasFrom: true, fromFloat: from, toFloat: to);
        public VMGAnimate FromTo(string path, int from, int to) => AddTween(path, VMGChannelType.Int, hasFrom: true, fromInt: from, toInt: to);
        public VMGAnimate FromTo(string path, bool from, bool to) => AddTween(path, VMGChannelType.Bool, hasFrom: true, fromBool: from, toBool: to);
        public VMGAnimate FromTo(string path, Color from, Color to) => AddTween(path, VMGChannelType.Color, hasFrom: true, fromColor: from, toColor: to);
        public VMGAnimate FromTo(string path, Vector2 from, Vector2 to) => AddTween(path, VMGChannelType.Vector2, hasFrom: true, fromVector: from, toVector: to);
        public VMGAnimate FromTo(string path, Vector3 from, Vector3 to) => AddTween(path, VMGChannelType.Vector3, hasFrom: true, fromVector: from, toVector: to);
        public VMGAnimate FromTo(string path, Vector4 from, Vector4 to) => AddTween(path, VMGChannelType.Vector4, hasFrom: true, fromVector: from, toVector: to);

        // FunctionValue overloads — the value is evaluated lazily on first
        // Render (and again on Refresh / RefreshOnLoop). anime.js parity for
        // `x: () => Math.random() * 500` and `animation.refresh()`. The
        // captured target lives in the lambda's closure if needed.
        public VMGAnimate To(string path, Func<float> fn) => AddFnTween(path, VMGChannelType.Float, toFloatFn: fn);
        public VMGAnimate To(string path, Func<int> fn) => AddFnTween(path, VMGChannelType.Int, toIntFn: fn);
        public VMGAnimate To(string path, Func<bool> fn) => AddFnTween(path, VMGChannelType.Bool, toBoolFn: fn);
        public VMGAnimate To(string path, Func<Color> fn) => AddFnTween(path, VMGChannelType.Color, toColorFn: fn);
        public VMGAnimate To(string path, Func<Vector2> fn) => AddFnTween(path, VMGChannelType.Vector2, toVector2Fn: fn);
        public VMGAnimate To(string path, Func<Vector3> fn) => AddFnTween(path, VMGChannelType.Vector3, toVector3Fn: fn);
        public VMGAnimate To(string path, Func<Vector4> fn) => AddFnTween(path, VMGChannelType.Vector4, toVector4Fn: fn);

        public VMGAnimate FromTo(string path, Func<float> from, Func<float> to) => AddFnTween(path, VMGChannelType.Float, fromFloatFn: from, toFloatFn: to, hasFrom: true);
        public VMGAnimate FromTo(string path, Func<int> from, Func<int> to) => AddFnTween(path, VMGChannelType.Int, fromIntFn: from, toIntFn: to, hasFrom: true);
        public VMGAnimate FromTo(string path, Func<bool> from, Func<bool> to) => AddFnTween(path, VMGChannelType.Bool, fromBoolFn: from, toBoolFn: to, hasFrom: true);
        public VMGAnimate FromTo(string path, Func<Color> from, Func<Color> to) => AddFnTween(path, VMGChannelType.Color, fromColorFn: from, toColorFn: to, hasFrom: true);
        public VMGAnimate FromTo(string path, Func<Vector2> from, Func<Vector2> to) => AddFnTween(path, VMGChannelType.Vector2, fromVector2Fn: from, toVector2Fn: to, hasFrom: true);
        public VMGAnimate FromTo(string path, Func<Vector3> from, Func<Vector3> to) => AddFnTween(path, VMGChannelType.Vector3, fromVector3Fn: from, toVector3Fn: to, hasFrom: true);
        public VMGAnimate FromTo(string path, Func<Vector4> from, Func<Vector4> to) => AddFnTween(path, VMGChannelType.Vector4, fromVector4Fn: from, toVector4Fn: to, hasFrom: true);

        // ------- Modifier API -------

        public VMGAnimate Duration(float seconds) { m_Duration = Mathf.Max(0f, seconds); m_HasDurationUserSet = true; return this; }
        public VMGAnimate Delay(float seconds) { m_Delay = Mathf.Max(0f, seconds); m_HasDelayUserSet = true; return this; }
        public VMGAnimate EndDelay(float seconds) { m_EndDelay = Mathf.Max(0f, seconds); return this; }

        public VMGAnimate Ease(VMGEasingPreset preset) { m_Ease = VMGEase.From(preset); m_HasEase = true; return this; }
        public VMGAnimate Ease(VMGEase ease) { m_Ease = ease; m_HasEase = true; return this; }
        public VMGAnimate Ease(float p1x, float p1y, float p2x, float p2y) { m_Ease = VMGEase.Bezier(p1x, p1y, p2x, p2y); m_HasEase = true; return this; }
        public VMGAnimate Ease(string name) { m_Ease = VMGEase.From(name); m_HasEase = true; return this; }

        public VMGAnimate Loop() { EnsureFinalized(); m_Anim.iterationCount = float.PositiveInfinity; return this; }
        public VMGAnimate Loop(int count) { EnsureFinalized(); m_Anim.iterationCount = Mathf.Max(1, count); return this; }
        public VMGAnimate LoopDelay(float seconds) { EnsureFinalized(); m_Anim.loopDelay = Mathf.Max(0f, seconds); return this; }
        public VMGAnimate Alternate(bool value = true) { EnsureFinalized(); m_Anim.alternate = value; return this; }
        public VMGAnimate Reversed(bool value = true) { EnsureFinalized(); m_Anim.reversed = value; return this; }

        public VMGAnimate Paused(bool value = true) { EnsureFinalized(); m_Anim.paused = value; return this; }

        // ------- Callbacks -------

        public VMGAnimate OnBegin(Action callback) { EnsureFinalized(); m_Anim.onBegin += _ => callback?.Invoke(); return this; }
        public VMGAnimate OnBeforeUpdate(Action callback) { EnsureFinalized(); m_Anim.onBeforeUpdate += _ => callback?.Invoke(); return this; }
        public VMGAnimate OnUpdate(Action callback) { EnsureFinalized(); m_Anim.onUpdate += _ => callback?.Invoke(); return this; }
        // OnRender = anime.js alias for onUpdate (after-write hook).
        public VMGAnimate OnRender(Action callback) { EnsureFinalized(); m_Anim.onUpdate += _ => callback?.Invoke(); return this; }
        public VMGAnimate OnLoop(Action callback) { EnsureFinalized(); m_Anim.onLoop += _ => callback?.Invoke(); return this; }
        public VMGAnimate OnComplete(Action callback) { EnsureFinalized(); m_Anim.onComplete += _ => callback?.Invoke(); return this; }
        public VMGAnimate OnPause(Action callback) { EnsureFinalized(); m_Anim.onPause += _ => callback?.Invoke(); return this; }

        // ------- Handle API -------

        // Completes when the animation finishes naturally OR when Stop() is
        // called. Infinite loops never complete naturally; user must Stop()
        // to release the awaiter.
        public Task Completion
        {
            get
            {
                EnsureFinalized();
                if (m_CompletionSource == null)
                {
                    m_CompletionSource = new TaskCompletionSource<bool>();
                    if (m_Anim.completed) m_CompletionSource.TrySetResult(true);
                    else m_Anim.onComplete += _ => m_CompletionSource.TrySetResult(true);
                }
                return m_CompletionSource.Task;
            }
        }

        public bool IsPlaying => !m_Anim.paused && !m_Anim.completed;
        public bool IsCompleted => m_Anim.completed;
        public float Progress => m_Anim.Progress;

        public void Play() { EnsureFinalized(); m_Anim.Play(); }
        public void Pause() { EnsureFinalized(); m_Anim.Pause(); }
        public void Stop()
        {
            EnsureFinalized();
            m_Anim.Stop();
            // Stop counts as natural completion for awaiters (anime.js parity).
            m_CompletionSource?.TrySetResult(true);
            VMGEngine.Unregister(m_Anim);
        }
        public void Seek(float absoluteTime) { EnsureFinalized(); m_Anim.Seek(absoluteTime); }

        // ------- anime.js parity sugar -------

        // Reset to t=0 and play. anime.js's restart().
        public VMGAnimate Restart()
        {
            EnsureFinalized();
            m_Anim.Stop();
            // Stop already seeks to 0 + clears completed/began; Play() then
            // un-pauses for a fresh cycle.
            m_Anim.Play();
            // Awaiters that already resolved keep their result. A fresh
            // awaiter gets a new source on next Completion read.
            if (m_CompletionSource != null && m_CompletionSource.Task.IsCompleted)
                m_CompletionSource = null;
            return this;
        }

        // Counterpart to Restart that doesn't auto-play.
        public VMGAnimate Reset()
        {
            EnsureFinalized();
            m_Anim.Stop();
            return this;
        }

        // anime.js's complete(): jump to end and fire onComplete.
        public VMGAnimate Complete()
        {
            EnsureFinalized();
            if (float.IsInfinity(m_Anim.iterationCount) || m_Anim.iterationCount <= 0f)
            {
                Debug.LogWarning("[VMG.Animation] Complete() called on an infinite animation; ignored.");
                return this;
            }
            m_Anim.RenderForCallbacks();
            m_CompletionSource?.TrySetResult(true);
            VMGEngine.Unregister(m_Anim);
            return this;
        }

        // Writable speed multiplier (anime.js's playbackRate).
        public float PlaybackRate
        {
            get { EnsureFinalized(); return m_Anim.speed; }
            set { EnsureFinalized(); m_Anim.speed = Mathf.Max(0f, value); }
        }

        // Toggle direction at the current playhead and keep playing.
        // anime.js parity for reverse(). Distinct from Reversed(bool) which
        // is the construction-time setter.
        public VMGAnimate Reverse()
        {
            EnsureFinalized();
            m_Anim.Reverse();
            if (m_CompletionSource != null && m_CompletionSource.Task.IsCompleted)
                m_CompletionSource = null;
            return this;
        }

        // Re-evaluate all FunctionValue slots on the next Render. anime.js
        // parity for animation.refresh(). No-op when no tween has lazy
        // values.
        public VMGAnimate Refresh()
        {
            EnsureFinalized();
            m_Anim.Refresh();
            return this;
        }

        // When true, every iteration boundary auto-calls Refresh — gives
        // "new random per loop" out of the box. Off by default (anime.js
        // resolves once and keeps the value across loops).
        public VMGAnimate RefreshOnLoop(bool value = true)
        {
            EnsureFinalized();
            m_Anim.refreshOnLoop = value;
            return this;
        }

        // ------- Internal hooks -------

        // Called by VMGTimeline.Add — apply timeline defaults BEFORE
        // finalize so they can actually reach the baked tweens. Each only
        // takes if the user hasn't already set the same modifier explicitly
        // (anime.js's "child overrides default" rule).
        internal void SetDefaultDuration(float seconds)
        {
            if (m_Finalized || m_HasDurationUserSet) return;
            m_Duration = Mathf.Max(0f, seconds);
        }

        internal void SetDefaultDelay(float seconds)
        {
            if (m_Finalized || m_HasDelayUserSet) return;
            m_Delay = Mathf.Max(0f, seconds);
        }

        internal void SetDefaultEase(VMGEase ease)
        {
            if (m_Finalized || m_HasEase) return;
            m_Ease = ease;
            m_HasEase = true;
        }

        // Called by VMGTimeline.Add to take ownership: the engine must not
        // tick this animation, the timeline will drive Tick() itself.
        internal VMGAnimation ClaimByTimeline()
        {
            EnsureFinalized();
            if (!m_OwnedByTimeline)
            {
                m_OwnedByTimeline = true;
                VMGEngine.CancelDeferred(this);
                VMGEngine.Unregister(m_Anim);
            }
            return m_Anim;
        }

        internal VMGAnimation Animation { get { EnsureFinalized(); return m_Anim; } }

        // Called by VMGEngine when promoting the deferred queue. Returns the
        // VMGAnimation core to register, or null if a Timeline already claimed
        // ownership in the meantime.
        internal VMGAnimation PromoteToEngine()
        {
            if (m_OwnedByTimeline) return null;
            EnsureFinalized();
            return m_Anim;
        }

        // ------- Build pipeline -------

        VMGAnimate AddFnTween(string path, VMGChannelType type,
            Func<float> fromFloatFn = null, Func<float> toFloatFn = null,
            Func<int> fromIntFn = null, Func<int> toIntFn = null,
            Func<bool> fromBoolFn = null, Func<bool> toBoolFn = null,
            Func<Color> fromColorFn = null, Func<Color> toColorFn = null,
            Func<Vector2> fromVector2Fn = null, Func<Vector2> toVector2Fn = null,
            Func<Vector3> fromVector3Fn = null, Func<Vector3> toVector3Fn = null,
            Func<Vector4> fromVector4Fn = null, Func<Vector4> toVector4Fn = null,
            bool hasFrom = false)
        {
            if (m_Finalized)
            {
                Debug.LogError($"[VMG.Animation] cannot add tween '{path}' after the animation is finalized");
                return this;
            }
            var tween = BuildTween(path, type, hasFrom,
                0f, 0f, 0, 0, false, false,
                default, default, default, default);
            if (tween == null) return this;
            tween.hasAnyFn = true;
            tween.fromFloatFn = fromFloatFn; tween.toFloatFn = toFloatFn;
            tween.fromIntFn = fromIntFn; tween.toIntFn = toIntFn;
            tween.fromBoolFn = fromBoolFn; tween.toBoolFn = toBoolFn;
            tween.fromColorFn = fromColorFn; tween.toColorFn = toColorFn;
            tween.fromVector2Fn = fromVector2Fn; tween.toVector2Fn = toVector2Fn;
            tween.fromVector3Fn = fromVector3Fn; tween.toVector3Fn = toVector3Fn;
            tween.fromVector4Fn = fromVector4Fn; tween.toVector4Fn = toVector4Fn;
            m_PendingTweens.Add(tween);
            return this;
        }

        VMGAnimate AddTween(string path, VMGChannelType type,
            bool hasFrom,
            float fromFloat = 0, float toFloat = 0,
            int fromInt = 0, int toInt = 0,
            bool fromBool = false, bool toBool = false,
            Color fromColor = default, Color toColor = default,
            Vector4 fromVector = default, Vector4 toVector = default)
        {
            if (m_Finalized)
            {
                Debug.LogError($"[VMG.Animation] cannot add tween '{path}' after the animation is finalized");
                return this;
            }
            var tween = BuildTween(path, type, hasFrom,
                fromFloat, toFloat, fromInt, toInt, fromBool, toBool,
                fromColor, toColor, fromVector, toVector);
            if (tween != null) m_PendingTweens.Add(tween);
            return this;
        }

        VMGCodeTween BuildTween(string path, VMGChannelType type,
            bool hasFrom,
            float fromFloat, float toFloat,
            int fromInt, int toInt,
            bool fromBool, bool toBool,
            Color fromColor, Color toColor,
            Vector4 fromVector, Vector4 toVector)
        {
            var rootType = m_Target.GetType();
            if (!VMGFieldPathCompiler.TryCompile(rootType, path, out var compiled, out var error))
            {
                Debug.LogError($"[VMG.Animation] path '{path}' on {rootType.Name} compile failed: {error}");
                return null;
            }
            var writer = new VMGChannelWriter(m_Target, compiled, type);
            if (!writer.IsTypeCompatible(out var typeError))
            {
                Debug.LogError($"[VMG.Animation] {typeError} (path '{path}' on {rootType.Name})");
                return null;
            }
            var reader = new VMGChannelReader(m_Target, compiled, type);
            return new VMGCodeTween
            {
                writer = writer,
                reader = reader,
                channelType = type,
                hasFrom = hasFrom,
                fromFloat = fromFloat, toFloat = toFloat,
                fromInt = fromInt, toInt = toInt,
                fromBool = fromBool, toBool = toBool,
                fromColor = fromColor, toColor = toColor,
                fromVector = fromVector, toVector = toVector,
            };
        }

        // Lock the animation shape. Called automatically by any handle/modifier
        // method that needs the underlying VMGAnimation in its final form.
        // Idempotent.
        void EnsureFinalized()
        {
            if (m_Finalized) return;
            m_Finalized = true;

            float dur = Mathf.Max(0.0001f, m_Duration);
            m_Anim.iterationDuration = dur;
            m_Anim.startDelay = m_Delay;
            // endDelay maps to extra time tacked onto the iteration. We model
            // it by lengthening iterationDuration; tweens themselves stay in
            // [0, dur] so the value holds for the endDelay span. Simple and
            // matches what users expect ("hold the end for N seconds").
            if (m_EndDelay > 0f) m_Anim.iterationDuration = dur + m_EndDelay;

            var ease = m_HasEase ? m_Ease : VMGEase.From(VMGEasingPreset.Ease);
            foreach (var t in m_PendingTweens)
            {
                t.startTime = 0f;
                t.endTime = dur;
                t.ease = ease;
                m_Anim.tweens.Add(t);
            }
            m_PendingTweens.Clear();

            m_Anim.paused = false; // auto-play
        }
    }
}
