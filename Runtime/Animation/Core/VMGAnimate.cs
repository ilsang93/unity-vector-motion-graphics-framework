using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using VMG.Svg;

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

        // MotionPath state. Single per-VMGAnimate: a target has one curve it
        // follows at a time. anime.js's createMotionPath returns x/y/angle
        // accessors bound to one path; same semantic. If the user calls
        // .AlongPath() twice the second wins (matches "later config replaces
        // earlier" elsewhere in the builder).
        VMGMotionPath m_PendingMotion;
        bool m_AutoRotate;
        float m_AutoRotateOffsetDeg;

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

        // ------- Keyframes API -------

        // CSS / anime.js parity for multi-stop animations on a single channel.
        // Times are normalized to [0, 1] across the iteration duration set by
        // .Duration(). Adjacent keyframes become FromTo segments; gaps before
        // the first key (time > 0) or after the last key are no-op holds. The
        // segment's ease is taken from the *target* keyframe's Ease (anime.js
        // convention — "ease applies to the segment ending at this frame"),
        // falling back to the animation-level Ease() when null.
        //
        // Call multiple times with different paths to animate several channels
        // in lock-step inside one animation.
        public VMGAnimate Keyframes(string path, params (float time, float value)[] keys)
            => AddKeyframes<float>(path, VMGChannelType.Float, Promote(keys));
        public VMGAnimate Keyframes(string path, params (float time, int value)[] keys)
            => AddKeyframes<int>(path, VMGChannelType.Int, Promote(keys));
        public VMGAnimate Keyframes(string path, params (float time, bool value)[] keys)
            => AddKeyframes<bool>(path, VMGChannelType.Bool, Promote(keys));
        public VMGAnimate Keyframes(string path, params (float time, Color value)[] keys)
            => AddKeyframes<Color>(path, VMGChannelType.Color, Promote(keys));
        public VMGAnimate Keyframes(string path, params (float time, Vector2 value)[] keys)
            => AddKeyframes<Vector2>(path, VMGChannelType.Vector2, Promote(keys));
        public VMGAnimate Keyframes(string path, params (float time, Vector3 value)[] keys)
            => AddKeyframes<Vector3>(path, VMGChannelType.Vector3, Promote(keys));
        public VMGAnimate Keyframes(string path, params (float time, Vector4 value)[] keys)
            => AddKeyframes<Vector4>(path, VMGChannelType.Vector4, Promote(keys));

        // VMGKeyframe<T> overloads — use when you need per-segment ease
        // overrides. The plain tuple overloads above are sufficient when
        // every segment shares the animation-level ease.
        public VMGAnimate Keyframes(string path, params VMGKeyframe<float>[] keys)
            => AddKeyframes<float>(path, VMGChannelType.Float, keys);
        public VMGAnimate Keyframes(string path, params VMGKeyframe<int>[] keys)
            => AddKeyframes<int>(path, VMGChannelType.Int, keys);
        public VMGAnimate Keyframes(string path, params VMGKeyframe<bool>[] keys)
            => AddKeyframes<bool>(path, VMGChannelType.Bool, keys);
        public VMGAnimate Keyframes(string path, params VMGKeyframe<Color>[] keys)
            => AddKeyframes<Color>(path, VMGChannelType.Color, keys);
        public VMGAnimate Keyframes(string path, params VMGKeyframe<Vector2>[] keys)
            => AddKeyframes<Vector2>(path, VMGChannelType.Vector2, keys);
        public VMGAnimate Keyframes(string path, params VMGKeyframe<Vector3>[] keys)
            => AddKeyframes<Vector3>(path, VMGChannelType.Vector3, keys);
        public VMGAnimate Keyframes(string path, params VMGKeyframe<Vector4>[] keys)
            => AddKeyframes<Vector4>(path, VMGChannelType.Vector4, keys);

        static VMGKeyframe<T>[] Promote<T>((float time, T value)[] keys)
        {
            if (keys == null) return null;
            var dst = new VMGKeyframe<T>[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                dst[i] = new VMGKeyframe<T>(keys[i].time, keys[i].value);
            return dst;
        }

        // ------- MotionPath API -------

        // Follow an arc-length parametrized curve. The target's
        // transform.position is driven along the path; with .AutoRotate(),
        // transform.eulerAngles.z is also written to face along the tangent.
        // anime.js parity: createMotionPath. Calling twice replaces the
        // pending motion path (the last call wins).
        public VMGAnimate AlongPath(VMGShapeAsset asset, int subShapeIndex = 0)
        {
            if (m_Finalized) { Debug.LogError("[VMG.Animation] cannot add .AlongPath after the animation is finalized"); return this; }
            var mp = VMGMotionPath.FromAsset(asset, subShapeIndex);
            if (mp == null)
            {
                Debug.LogError($"[VMG.Animation] .AlongPath: asset '{(asset == null ? "null" : asset.name)}' has no sub-shape at index {subShapeIndex}");
                return this;
            }
            m_PendingMotion = mp;
            return this;
        }

        public VMGAnimate AlongPath(IList<Vector2> points, bool closed = false)
        {
            if (m_Finalized) { Debug.LogError("[VMG.Animation] cannot add .AlongPath after the animation is finalized"); return this; }
            var mp = VMGMotionPath.FromPoints(points, closed);
            if (mp == null)
            {
                Debug.LogError("[VMG.Animation] .AlongPath: points list is null or empty");
                return this;
            }
            m_PendingMotion = mp;
            return this;
        }

        // Rotate the target so its local +X axis faces along the curve's
        // tangent. Offset is added after Atan2 — supply -90 for sprites
        // whose "forward" is +Y, etc. No-op without a prior .AlongPath().
        public VMGAnimate AutoRotate(float offsetDeg = 0f)
        {
            if (m_Finalized) { Debug.LogError("[VMG.Animation] cannot add .AutoRotate after the animation is finalized"); return this; }
            m_AutoRotate = true;
            m_AutoRotateOffsetDeg = offsetDeg;
            return this;
        }

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

        // Snap every written channel back to its pre-animation baseline and
        // stop. anime.js parity for revert(). Without this, single-tween
        // toggle patterns force the user to wrap in a Timeline just to get a
        // revertable handle.
        public void Revert()
        {
            EnsureFinalized();
            m_Anim.Revert();
            m_CompletionSource?.TrySetResult(true);
            VMGEngine.Unregister(m_Anim);
        }

        // Stop the tween at its current channel value (no baseline restore)
        // and clear tween flags so a follow-up Animate can recapture from
        // here. Use for toggle/re-press patterns where the old handle is
        // being discarded — Pause would leave it resumable with the original
        // from→to, Revert would flash the baseline.
        public void Cancel()
        {
            EnsureFinalized();
            m_Anim.Cancel();
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
            var writer = new VMGChannelWriter(m_Target, compiled, type, path);
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

        // Expand a multi-stop keyframes spec into per-adjacent-pair FromTo
        // segment tweens. Times are normalized to [0, 1] at authoring time;
        // we stamp them with explicit startTime/endTime in seconds inside
        // EnsureFinalized so the [0, dur] reset doesn't clobber them.
        VMGAnimate AddKeyframes<T>(string path, VMGChannelType type, VMGKeyframe<T>[] keys)
        {
            if (m_Finalized)
            {
                Debug.LogError($"[VMG.Animation] cannot add tween '{path}' after the animation is finalized");
                return this;
            }
            if (keys == null || keys.Length < 2)
            {
                Debug.LogError($"[VMG.Animation] .Keyframes('{path}') needs at least 2 keyframes");
                return this;
            }
            // Sort by time. We mutate a copy so callers' arrays stay intact.
            var sorted = new VMGKeyframe<T>[keys.Length];
            System.Array.Copy(keys, sorted, keys.Length);
            System.Array.Sort(sorted, (a, b) => a.Time.CompareTo(b.Time));

            for (int i = 1; i < sorted.Length; i++)
            {
                var prev = sorted[i - 1];
                var cur = sorted[i];
                if (cur.Time <= prev.Time) continue; // skip degenerate / duplicate stops
                var tween = BuildSegmentTween(path, type, prev.Value, cur.Value);
                if (tween == null) return this;
                tween.hasExplicitSegment = true;
                tween.startTime = Mathf.Clamp01(prev.Time);
                tween.endTime = Mathf.Clamp01(cur.Time);
                // Ease per anime.js parity: target frame's override wins. If
                // neither side declares one, EnsureFinalized fills in the
                // animation-level ease (so a later .Ease() call still reaches
                // un-decorated segments).
                if (cur.Ease.HasValue)
                {
                    tween.ease = cur.Ease.Value;
                    tween.hasExplicitEase = true;
                }
                else if (prev.Ease.HasValue)
                {
                    tween.ease = prev.Ease.Value;
                    tween.hasExplicitEase = true;
                }
                tween.hasFrom = true; // explicit from supplied
                m_PendingTweens.Add(tween);
            }
            return this;
        }

        VMGCodeTween BuildSegmentTween<T>(string path, VMGChannelType type, T from, T to)
        {
            switch (type)
            {
                case VMGChannelType.Float:
                {
                    float f = (float)(object)from, t = (float)(object)to;
                    return BuildTween(path, type, true, f, t, 0, 0, false, false, default, default, default, default);
                }
                case VMGChannelType.Int:
                {
                    int f = (int)(object)from, t = (int)(object)to;
                    return BuildTween(path, type, true, 0, 0, f, t, false, false, default, default, default, default);
                }
                case VMGChannelType.Bool:
                {
                    bool f = (bool)(object)from, t = (bool)(object)to;
                    return BuildTween(path, type, true, 0, 0, 0, 0, f, t, default, default, default, default);
                }
                case VMGChannelType.Color:
                {
                    Color f = (Color)(object)from, t = (Color)(object)to;
                    return BuildTween(path, type, true, 0, 0, 0, 0, false, false, f, t, default, default);
                }
                case VMGChannelType.Vector2:
                {
                    Vector2 f = (Vector2)(object)from, t = (Vector2)(object)to;
                    return BuildTween(path, type, true, 0, 0, 0, 0, false, false, default, default, f, t);
                }
                case VMGChannelType.Vector3:
                {
                    Vector3 f = (Vector3)(object)from, t = (Vector3)(object)to;
                    return BuildTween(path, type, true, 0, 0, 0, 0, false, false, default, default, f, t);
                }
                case VMGChannelType.Vector4:
                {
                    Vector4 f = (Vector4)(object)from, t = (Vector4)(object)to;
                    return BuildTween(path, type, true, 0, 0, 0, 0, false, false, default, default, f, t);
                }
            }
            return null;
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
                if (t.hasExplicitSegment)
                {
                    // Keyframes segments: startTime/endTime hold normalized
                    // positions in [0, 1]; scale them into seconds against
                    // the iteration window. Per-segment ease overrides win,
                    // otherwise fall back to the animation-level ease.
                    t.startTime = Mathf.Clamp01(t.startTime) * dur;
                    t.endTime = Mathf.Clamp01(t.endTime) * dur;
                    if (!t.hasExplicitEase) t.ease = ease;
                }
                else
                {
                    t.startTime = 0f;
                    t.endTime = dur;
                    t.ease = ease;
                }
                t.owner = m_Anim;
                m_Anim.tweens.Add(t);
            }
            m_PendingTweens.Clear();

            if (m_PendingMotion != null)
            {
                var motion = BuildMotionPathTween(m_PendingMotion, ease, dur);
                if (motion != null)
                {
                    motion.owner = m_Anim;
                    m_Anim.tweens.Add(motion);
                }
                m_PendingMotion = null;
            }

            m_Anim.paused = false; // auto-play
        }

        // Build a single VMGMotionPathTween bound to transform.position
        // (and optionally transform.eulerAngles.z for AutoRotate). The path
        // is sampled in 2D and projected to XY, preserving the current Z.
        VMGMotionPathTween BuildMotionPathTween(VMGMotionPath motion, VMGEase ease, float duration)
        {
            const string positionPath = "transform.position";
            var rootType = m_Target.GetType();
            if (!VMGFieldPathCompiler.TryCompile(rootType, positionPath, out var posCompiled, out var error))
            {
                Debug.LogError($"[VMG.Animation] .AlongPath: cannot bind '{positionPath}' on {rootType.Name}: {error}");
                return null;
            }
            var posType = posCompiled.leafType == typeof(Vector2) ? VMGChannelType.Vector2 : VMGChannelType.Vector3;
            var posWriter = new VMGChannelWriter(m_Target, posCompiled, posType, positionPath);
            if (!posWriter.IsTypeCompatible(out var posErr))
            {
                Debug.LogError($"[VMG.Animation] .AlongPath: {posErr}");
                return null;
            }
            var posReader = new VMGChannelReader(m_Target, posCompiled, posType);

            var tween = new VMGMotionPathTween
            {
                positionWriter = posWriter,
                positionReader = posReader,
                path = motion,
                ease = ease,
                isVector3Channel = posType == VMGChannelType.Vector3,
                startTime = 0f,
                endTime = duration,
            };

            if (m_AutoRotate)
            {
                const string rotPath = "transform.eulerAngles.z";
                if (!VMGFieldPathCompiler.TryCompile(rootType, rotPath, out var rotCompiled, out var rotError))
                {
                    Debug.LogWarning($"[VMG.Animation] .AutoRotate: cannot bind '{rotPath}': {rotError}. Position will still animate.");
                }
                else
                {
                    var rotWriter = new VMGChannelWriter(m_Target, rotCompiled, VMGChannelType.Float, rotPath);
                    if (rotWriter.IsTypeCompatible(out _))
                    {
                        tween.rotationWriter = rotWriter;
                        tween.rotationReader = new VMGChannelReader(m_Target, rotCompiled, VMGChannelType.Float);
                        tween.rotationOffsetDeg = m_AutoRotateOffsetDeg;
                    }
                }
            }

            return tween;
        }
    }
}
