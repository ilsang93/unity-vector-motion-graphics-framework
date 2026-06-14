using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace VMG.Animation.Core
{
    // Code-driven timeline. Mirrors anime.js's Timeline: a Timer that owns a
    // list of children, each positioned at a per-child offset within the
    // iteration. Position can be expressed as an anime.js-style string
    // ('<', '+=N', label, ...) or a typed VMGAt.
    //
    // Composition (round 4-B D21): "last-registered wins". Children evaluate
    // in add order; the last writer on a (target, path) survives a frame.
    // No active de-duplication of overlapping tweens.
    public class VMGTimeline : VMGTimer
    {
        // Pre-finalize storage. Once any handle/runtime call needs the timer
        // to be live, EnsureFinalized() copies these into the actual Timer
        // state (paused = false starts auto-play).
        readonly List<Child> m_Children = new List<Child>();
        readonly Dictionary<string, float> m_Labels = new Dictionary<string, float>();

        // Defaults applied to children added after the call. Mirrors
        // anime.js's `defaults` on Timeline construction.
        float m_DefaultDuration = -1f;
        float m_DefaultDelay = -1f;
        VMGEase m_DefaultEase;
        bool m_HasDefaultEase;

        // Explicit duration override. -1 = auto (grow with children, current
        // default). >0 = locked; child adds no longer extend iterationDuration
        // and children that stick out past the explicit duration are
        // truncated by the existing OnAfterRender clamp. anime.js parity
        // with `duration: N` in createTimeline().
        float m_ExplicitDuration = -1f;

        // Optional ease applied to iterationTime before child dispatch.
        // anime.js's `playbackEase`. Composes with each child's own ease
        // (children see a re-mapped time but still run their own curve).
        // Default = no remap (zero-overhead fast path).
        VMGEase m_PlaybackEase;
        bool m_HasPlaybackEase;

        bool m_Finalized;
        bool m_OwnedByParent;
        TaskCompletionSource<bool> m_CompletionSource;

        // Per-child timeline metadata.
        struct Child
        {
            public VMGAnimation anim;     // Concrete child (animation OR a nested timeline rendered through its base Timer).
            public VMGTimer timer;        // Non-null when the child is an empty timer (set/call slots).
            public float offset;
            public float duration;
        }

        internal VMGTimeline()
        {
            iterationDuration = 0.0001f; // grows as children are added
            iterationCount = 1f;
            paused = true; // auto-play flipped at finalize
            VMGEngine.RegisterDeferredTimeline(this);
        }

        // ------- Defaults / loop / direction (modifiers) -------

        // Lock the timeline's total length to an explicit value. Pads with
        // a held end (children clamp to their last value via the existing
        // OnAfterRender path) when longer than the children's natural span,
        // truncates when shorter. anime.js parity for `duration: N`.
        // Pass -1 (or any negative) to release the lock.
        public VMGTimeline Duration(float seconds)
        {
            if (seconds < 0f)
            {
                m_ExplicitDuration = -1f;
                RecomputeIterationDuration();
                return this;
            }
            m_ExplicitDuration = Mathf.Max(0.0001f, seconds);
            // Warn if any child would be truncated. Computed here (not on
            // each Add) so the user sees a single message at the point of
            // the decision rather than one per child.
            float naturalEnd = 0f;
            for (int i = 0; i < m_Children.Count; i++)
            {
                var c = m_Children[i];
                float e = c.offset + c.duration;
                if (e > naturalEnd) naturalEnd = e;
            }
            if (naturalEnd > m_ExplicitDuration + 1e-4f)
                Debug.LogWarning($"[VMG.Animation] Timeline.Duration({seconds:F3}) is shorter than the children's natural end ({naturalEnd:F3}); the tail will be truncated.");
            iterationDuration = m_ExplicitDuration;
            return this;
        }

        // Apply an ease curve to iterationTime *before* it's distributed to
        // children. Composes with each child's own ease — the child sees a
        // re-mapped time but still runs its own curve. anime.js parity for
        // `playbackEase`. Mirrors VMGAnimate.Ease overloads for surface
        // consistency. Pass VMGEase.Linear (or omit and never call) to opt
        // out — the no-remap path stays zero-overhead.
        public VMGTimeline PlaybackEase(VMGEasingPreset preset) { m_PlaybackEase = VMGEase.From(preset); m_HasPlaybackEase = true; return this; }
        public VMGTimeline PlaybackEase(VMGEase ease) { m_PlaybackEase = ease; m_HasPlaybackEase = true; return this; }
        public VMGTimeline PlaybackEase(float p1x, float p1y, float p2x, float p2y) { m_PlaybackEase = VMGEase.Bezier(p1x, p1y, p2x, p2y); m_HasPlaybackEase = true; return this; }
        public VMGTimeline PlaybackEase(string name) { m_PlaybackEase = VMGEase.From(name); m_HasPlaybackEase = true; return this; }

        public VMGTimeline Defaults(float duration = -1f, VMGEasingPreset ease = VMGEasingPreset.Custom, float delay = -1f)
        {
            if (duration >= 0f) m_DefaultDuration = duration;
            if (delay >= 0f) m_DefaultDelay = delay;
            if (ease != VMGEasingPreset.Custom)
            {
                m_DefaultEase = VMGEase.From(ease);
                m_HasDefaultEase = true;
            }
            return this;
        }

        // Overload taking a constructed VMGEase. Used by the DSL header where
        // ease can be a preset name, cubicBezier(...), steps(N), or spring —
        // all of which produce a VMGEase rather than a preset enum.
        public VMGTimeline Defaults(VMGEase ease, float duration = -1f, float delay = -1f)
        {
            if (duration >= 0f) m_DefaultDuration = duration;
            if (delay >= 0f) m_DefaultDelay = delay;
            m_DefaultEase = ease;
            m_HasDefaultEase = true;
            return this;
        }

        public VMGTimeline Loop() { EnsureFinalized(); iterationCount = float.PositiveInfinity; return this; }
        public VMGTimeline Loop(int count) { EnsureFinalized(); iterationCount = Mathf.Max(1, count); return this; }
        public VMGTimeline LoopDelay(float seconds) { EnsureFinalized(); loopDelay = Mathf.Max(0f, seconds); return this; }
        public VMGTimeline Alternate(bool value = true) { EnsureFinalized(); alternate = value; return this; }
        public VMGTimeline Reversed(bool value = true) { EnsureFinalized(); reversed = value; return this; }
        public VMGTimeline Paused(bool value = true) { EnsureFinalized(); paused = value; return this; }

        // ------- Callbacks (Timer events forwarded) -------

        public VMGTimeline OnBegin(Action callback) { EnsureFinalized(); onBegin += _ => callback?.Invoke(); return this; }
        public VMGTimeline OnBeforeUpdate(Action callback) { EnsureFinalized(); onBeforeUpdate += _ => callback?.Invoke(); return this; }
        public VMGTimeline OnUpdate(Action callback) { EnsureFinalized(); onUpdate += _ => callback?.Invoke(); return this; }
        // OnRender is anime.js's name for the "after each frame's writes"
        // hook; VMG fires it from the same point as onUpdate so it's an
        // alias for parity. Kept separate so call sites read naturally.
        public VMGTimeline OnRender(Action callback) { EnsureFinalized(); onUpdate += _ => callback?.Invoke(); return this; }
        public VMGTimeline OnLoop(Action callback) { EnsureFinalized(); onLoop += _ => callback?.Invoke(); return this; }
        public VMGTimeline OnComplete(Action callback) { EnsureFinalized(); onComplete += _ => callback?.Invoke(); return this; }
        public VMGTimeline OnPause(Action callback) { EnsureFinalized(); onPause += _ => callback?.Invoke(); return this; }

        // ------- Handle API -------

        public Task Completion
        {
            get
            {
                EnsureFinalized();
                if (m_CompletionSource == null)
                {
                    m_CompletionSource = new TaskCompletionSource<bool>();
                    if (completed) m_CompletionSource.TrySetResult(true);
                    else onComplete += _ => m_CompletionSource.TrySetResult(true);
                }
                return m_CompletionSource.Task;
            }
        }

        public bool IsPlaying => !paused && !completed;
        public bool IsCompleted => completed;

        public new void Play() { EnsureFinalized(); base.Play(); }
        public new void Pause() { EnsureFinalized(); base.Pause(); }
        public new void Stop()
        {
            EnsureFinalized();
            base.Stop();
            m_CompletionSource?.TrySetResult(true);
            VMGEngine.UnregisterTimeline(this);
        }
        public new void Seek(float absoluteTime) { EnsureFinalized(); base.Seek(absoluteTime); }

        // ------- anime.js parity sugar -------

        // Reset to t=0 and play. anime.js parity: restart() is restart from
        // the beginning, distinct from resume() which keeps the playhead.
        // We rearm a fresh completion source so awaiters can wait again.
        public VMGTimeline Restart()
        {
            EnsureFinalized();
            // Stop already seeks to 0 and pauses; then Play() un-pauses and
            // (because completed was cleared by Stop) starts a new cycle.
            base.Stop();
            m_PrevIterationTime = -1f;
            m_HasPrevIterationTime = false;
            // Re-arm the completion source. Previous awaiters keep their
            // result (anime.js's then() is one-shot per restart cycle).
            if (m_CompletionSource != null && m_CompletionSource.Task.IsCompleted)
                m_CompletionSource = null;
            base.Play();
            return this;
        }

        // anime.js's resume() = play from current position without reset.
        // Our base Play() already does this when not completed; we expose it
        // under the parity name.
        public VMGTimeline Resume() { EnsureFinalized(); base.Play(); return this; }

        // Reset to t=0 and remain paused. Counterpart to Restart that
        // doesn't auto-play. anime.js's reset().
        public VMGTimeline Reset()
        {
            EnsureFinalized();
            base.Stop();
            m_PrevIterationTime = -1f;
            m_HasPrevIterationTime = false;
            return this;
        }

        // Jump to end + fire onComplete. anime.js's complete(). Useful for
        // "skip animation" buttons. Infinite timelines have no end — we
        // treat that as a no-op + warning.
        public VMGTimeline Complete()
        {
            EnsureFinalized();
            if (float.IsInfinity(iterationCount) || iterationCount <= 0f)
            {
                Debug.LogWarning("[VMG.Animation] Complete() called on an infinite timeline; ignored.");
                return this;
            }
            // RenderForCallbacks drives the timer to its TotalDuration with
            // fireCallbacks=true, which fires onComplete and writes every
            // tween's final value via OnAfterRender's normal path.
            RenderForCallbacks();
            m_CompletionSource?.TrySetResult(true);
            VMGEngine.UnregisterTimeline(this);
            return this;
        }

        // Writable speed multiplier (anime.js's playbackRate). VMGTimer
        // already has `speed`; this just wraps it under the parity name so
        // call sites read naturally.
        public float PlaybackRate
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        // Re-evaluate every child animation's FunctionValue slots on the
        // next sample. anime.js parity for timeline.refresh(). Nested
        // timelines forward to their own children recursively via the
        // VMGAnimation slot in Child (which for nested timelines is null —
        // see AddChildTimer; we walk timer-children separately).
        public VMGTimeline Refresh()
        {
            for (int i = 0, n = m_Children.Count; i < n; i++)
            {
                var c = m_Children[i];
                c.anim?.Refresh();
                if (c.timer is VMGTimeline nested) nested.Refresh();
            }
            return this;
        }

        // Restore every channel every child has written to back to its
        // pre-animation value. anime.js's revert(): snap-back (not animated)
        // and stop. Distinct from Reverse() which flips direction and keeps
        // playing.
        //
        // Children are reverted in REVERSE order so when multiple children
        // wrote to the same (target, path), the last writer is undone first,
        // restoring the value the previous writer would have seen — then
        // that previous writer is undone, ultimately reaching the
        // before-anything state.
        public VMGTimeline Revert() => EndAndClearChildren(restoreBaseline: true);

        // Stop every child at its current channel value and clear tween flags
        // so a follow-up animate can recapture from here. Symmetric with
        // VMGAnimation.Cancel — see that method for the toggle/re-press
        // motivation. Children are walked in reverse (same composition rule
        // as Revert) but baseline writes are skipped.
        public VMGTimeline Cancel() => EndAndClearChildren(restoreBaseline: false);

        VMGTimeline EndAndClearChildren(bool restoreBaseline)
        {
            EnsureFinalized();
            for (int i = m_Children.Count - 1; i >= 0; i--)
            {
                var c = m_Children[i];
                if (c.anim != null)
                {
                    if (restoreBaseline) c.anim.Revert();
                    else c.anim.Cancel();
                }
                if (c.timer is VMGTimeline nested)
                {
                    if (restoreBaseline) nested.Revert();
                    else nested.Cancel();
                }
            }
            // Same trick as VMGAnimation.Revert: skip base.Stop's Seek-to-0
            // because re-rendering at t=0 would dispatch into children and
            // overwrite the freshly restored channel values via their own
            // tween outputs at iterationTime=0.
            paused = true;
            m_CurrentTime = -startDelay;
            m_IterationTime = 0f;
            m_CurrentIteration = 0;
            began = false;
            completed = false;
            m_PrevIterationTime = -1f;
            m_HasPrevIterationTime = false;
            if (m_CompletionSource != null && m_CompletionSource.Task.IsCompleted)
                m_CompletionSource = null;
            return this;
        }

        // Toggle direction at the current playhead and keep playing.
        // anime.js parity for reverse(). Distinct from Reversed(bool) which
        // is the construction-time setter. The base impl mirrors
        // CurrentTime so iterationTime stays continuous across the flip;
        // we also reset the prev-iterationTime tracker so the next
        // OnAfterRender sweep doesn't misfire 0-duration child callbacks.
        public new VMGTimeline Reverse()
        {
            EnsureFinalized();
            base.Reverse();
            m_PrevIterationTime = -1f;
            m_HasPrevIterationTime = false;
            // If Reverse re-armed a completed timeline, the awaiter contract
            // (one-shot per cycle) means we drop the resolved source so a
            // fresh awaiter gets a new one on next Completion read.
            if (m_CompletionSource != null && m_CompletionSource.Task.IsCompleted)
                m_CompletionSource = null;
            return this;
        }

        // ------- Add (children) -------

        public VMGTimeline Add(VMGAnimate builder) => Add(builder, VMGAt.End());
        public VMGTimeline Add(VMGAnimate builder, VMGAt at)
        {
            if (builder == null) return this;
            // Apply defaults BEFORE ClaimByTimeline triggers finalize. ease
            // default has to land here because EnsureFinalized bakes ease
            // into each tween; once that's done, mutating m_DefaultEase on
            // the timeline can't reach the baked tweens any more.
            ApplyDefaultsToBuilder(builder);
            var anim = builder.ClaimByTimeline();
            AddAnimationChild(anim, at);
            return this;
        }

        public VMGTimeline Add(VMGTimeline child) => Add(child, VMGAt.End());
        public VMGTimeline Add(VMGTimeline child, VMGAt at)
        {
            if (child == null || child == this) return this;
            child.ClaimByParent();
            // Treat a nested timeline as an animation child: its base Timer
            // is what we Seek into each frame.
            AddChildTimer(child, at, child.iterationDuration);
            return this;
        }

        // anime.js's sync(): semantically distinct from add() because anime.js
        // freezes composition at child-creation time and replays as-is. In
        // VMG, Add already takes ownership of the builder's finalized
        // animation (ClaimByTimeline), so add and sync collapse to the same
        // behavior. Sync is exposed as an alias for parity / discoverability.
        public VMGTimeline Sync(VMGAnimate builder) => Add(builder, VMGAt.End());
        public VMGTimeline Sync(VMGAnimate builder, VMGAt at) => Add(builder, at);
        public VMGTimeline Sync(VMGTimeline child) => Add(child, VMGAt.End());
        public VMGTimeline Sync(VMGTimeline child, VMGAt at) => Add(child, at);

        // Embed an existing authored clip. Events on the clip become Call
        // children on this timeline at their respective times.
        public VMGTimeline Add(VMGAnimationClip clip, Transform root) => Add(clip, root, VMGAt.End());
        public VMGTimeline Add(VMGAnimationClip clip, Transform root, VMGAt at)
        {
            if (clip == null || root == null) return this;
            var anim = VMGClipCompiler.Compile(clip, root);
            anim.paused = true; // we drive it via Seek
            AddAnimationChild(anim, at);

            // Resolve the just-resolved offset so event Calls land at the
            // correct timeline time.
            float baseOffset = m_Children[m_Children.Count - 1].offset;
            if (clip.events != null)
            {
                foreach (var ev in clip.events)
                {
                    if (ev == null) continue;
                    var captured = ev;
                    Call(() => captured.invoke?.Invoke(), VMGAt.Time(baseOffset + ev.time));
                }
            }
            return this;
        }

        // ------- Stagger (per-index time distribution) -------

        // anime.js's stagger(value, { from }) distributes a value across N
        // targets. In VMG it becomes "build N children, place them at
        // staggered offsets". The base offset (anchor) is `at`; each child
        // lands at `at + values[i] * step`. The builder lambda runs once per
        // target with (target, index, total) so callers can also vary per
        // tween values (e.g. .To("x", i * 50)).
        //
        // Defaults (Duration / Delay / Ease set on the timeline before this
        // call) propagate to every emitted child via the existing
        // ApplyDefaultsToBuilder path. Each child is otherwise independent —
        // the lambda owns construction.
        public VMGTimeline Stagger<T>(System.Collections.Generic.IList<T> targets,
            Func<T, VMGAnimate> build,
            float step,
            VMGStaggerFrom from = VMGStaggerFrom.First,
            VMGAt at = default,
            int? seed = null) where T : Component
        {
            return Stagger(targets, (t, i, n) => build(t), step, from, at, seed);
        }

        public VMGTimeline Stagger<T>(System.Collections.Generic.IList<T> targets,
            Func<T, int, int, VMGAnimate> build,
            float step,
            VMGStaggerFrom from = VMGStaggerFrom.First,
            VMGAt at = default,
            int? seed = null) where T : Component
        {
            if (targets == null || targets.Count == 0 || build == null) return this;

            // Anchor in absolute timeline time. Resolved ONCE before the loop
            // so every emitted child anchors on the same base — if we re-
            // resolved per-child with VMGAt.End() then each prior child would
            // push the next anchor forward, defeating the stagger.
            float anchor = ResolvePosition(at);

            int n = targets.Count;
            var calc = VMGStaggerCalculator.Build(n, from, seed);

            for (int i = 0; i < n; i++)
            {
                var builder = build(targets[i], i, n);
                if (builder == null) continue;
                float childOffset = anchor + calc.values[i] * step;
                ApplyDefaultsToBuilder(builder);
                var anim = builder.ClaimByTimeline();
                AddAnimationChild(anim, VMGAt.Time(childOffset));
            }
            return this;
        }

        // Mini-timeline overload: the build lambda hands back a fully-
        // populated VMGTimeline per child instead of a single VMGAnimate.
        // Lets a stagger body contain multiple animate / motionPath /
        // keyframes statements that all share the same per-child offset,
        // staying in lockstep within each child. Defaults are propagated
        // by the caller (it copies our defaults into the mini before
        // adding builders), so the children inside the mini pick them
        // up via the mini's own ApplyDefaultsToBuilder.
        public VMGTimeline Stagger<T>(System.Collections.Generic.IList<T> targets,
            Func<T, int, int, VMGTimeline> build,
            float step,
            VMGStaggerFrom from = VMGStaggerFrom.First,
            VMGAt at = default,
            int? seed = null) where T : Component
        {
            if (targets == null || targets.Count == 0 || build == null) return this;

            float anchor = ResolvePosition(at);

            int n = targets.Count;
            var calc = VMGStaggerCalculator.Build(n, from, seed);

            for (int i = 0; i < n; i++)
            {
                var mini = build(targets[i], i, n);
                if (mini == null || mini == this) continue;
                float childOffset = anchor + calc.values[i] * step;
                mini.ClaimByParent();
                AddChildTimer(mini, VMGAt.Time(childOffset), mini.iterationDuration);
            }
            return this;
        }

        // Copy this timeline's authoring defaults (Duration / Delay /
        // Ease) onto `dest`. Used by stagger's mini-timeline path so each
        // per-child mini-timeline inherits the parent timeline's defaults
        // before its builders are added.
        internal void CopyDefaultsTo(VMGTimeline dest)
        {
            if (dest == null || dest == this) return;
            if (m_DefaultDuration > 0f) dest.m_DefaultDuration = m_DefaultDuration;
            if (m_DefaultDelay > 0f) dest.m_DefaultDelay = m_DefaultDelay;
            if (m_HasDefaultEase) { dest.m_DefaultEase = m_DefaultEase; dest.m_HasDefaultEase = true; }
        }

        // ------- Set (instant value) -------

        public VMGTimeline Set(Component target, string path, float value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Float, at, vFloat: value);
        public VMGTimeline Set(Component target, string path, int value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Int, at, vInt: value);
        public VMGTimeline Set(Component target, string path, bool value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Bool, at, vBool: value);
        public VMGTimeline Set(Component target, string path, Color value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Color, at, vColor: value);
        public VMGTimeline Set(Component target, string path, Vector2 value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Vector2, at, vVector: value);
        public VMGTimeline Set(Component target, string path, Vector3 value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Vector3, at, vVector: value);
        public VMGTimeline Set(Component target, string path, Vector4 value, VMGAt at = default)
            => AddSet(target, path, VMGChannelType.Vector4, at, vVector: value);

        VMGTimeline AddSet(Component target, string path, VMGChannelType type, VMGAt at,
            float vFloat = 0, int vInt = 0, bool vBool = false,
            Color vColor = default, Vector4 vVector = default)
        {
            // Build a 0-duration FromTo where from == to, so the value is
            // simply written when iterationTime passes the slot.
            var builder = VMGFx.Animate(target);
            switch (type)
            {
                case VMGChannelType.Float: builder.FromTo(path, vFloat, vFloat); break;
                case VMGChannelType.Int: builder.FromTo(path, vInt, vInt); break;
                case VMGChannelType.Bool: builder.FromTo(path, vBool, vBool); break;
                case VMGChannelType.Color: builder.FromTo(path, vColor, vColor); break;
                case VMGChannelType.Vector2: builder.FromTo(path, (Vector2)vVector, (Vector2)vVector); break;
                case VMGChannelType.Vector3: builder.FromTo(path, (Vector3)vVector, (Vector3)vVector); break;
                case VMGChannelType.Vector4: builder.FromTo(path, vVector, vVector); break;
            }
            builder.Duration(0f);
            Add(builder, at);
            return this;
        }

        // ------- Call (callback at a time) -------

        public VMGTimeline Call(Action callback) => Call(callback, VMGAt.End());
        public VMGTimeline Call(Action callback, VMGAt at)
        {
            if (callback == null) return this;
            // A 0-duration Timer with an onComplete; it'll fire the frame the
            // timeline crosses the slot.
            var t = new VMGTimer { iterationDuration = 0.0001f, iterationCount = 1f, paused = true };
            t.onComplete += _ => callback();
            AddChildTimer(t, at, 0f);
            return this;
        }

        // ------- Label -------

        public VMGTimeline Label(string name) => Label(name, VMGAt.End());
        public VMGTimeline Label(string name, VMGAt at)
        {
            if (string.IsNullOrEmpty(name)) return this;
            float time = ResolvePosition(at);
            m_Labels[name] = time;
            return this;
        }

        // ------- Stretch (scale entire timeline + labels) -------

        public VMGTimeline Stretch(float newDuration)
        {
            EnsureFinalized();
            if (newDuration <= 0f || iterationDuration <= 0f) return this;
            float scale = newDuration / iterationDuration;

            for (int i = 0; i < m_Children.Count; i++)
            {
                var c = m_Children[i];
                c.offset *= scale;
                c.duration *= scale;
                m_Children[i] = c;

                if (c.anim != null) c.anim.iterationDuration = Mathf.Max(0.0001f, c.anim.iterationDuration * scale);
                else if (c.timer != null) c.timer.iterationDuration = Mathf.Max(0.0001f, c.timer.iterationDuration * scale);
            }

            var keys = new List<string>(m_Labels.Keys);
            foreach (var k in keys) m_Labels[k] *= scale;

            iterationDuration = newDuration;
            // Stretch and explicit Duration both fix the timeline length but
            // by different rules (Stretch scales children, Duration just
            // boxes them). After a Stretch the children already match the
            // new length, so adopt that length as the explicit value so
            // future child adds don't grow past it.
            m_ExplicitDuration = newDuration;
            return this;
        }

        // ------- Remove (best-effort by target) -------

        public VMGTimeline Remove(Component target)
        {
            // Only animation children carry per-tween target info today.
            // Remove any animation child whose tweens are all bound to the
            // same target. This is a conservative match — narrower removes
            // (per-path) can be added later.
            for (int i = m_Children.Count - 1; i >= 0; i--)
            {
                var c = m_Children[i];
                if (c.anim != null && AllTweensTargetSame(c.anim, target))
                {
                    m_Children.RemoveAt(i);
                }
            }
            RecomputeIterationDuration();
            return this;
        }

        static bool AllTweensTargetSame(VMGAnimation anim, Component target)
        {
            if (anim.tweens.Count == 0) return false;
            var targetGo = target != null ? target.gameObject : null;
            foreach (var t in anim.tweens)
            {
                if (t is VMGCodeTween c)
                {
                    if (c.writer == null) return false;
                    if (c.writer.TargetEquals(target)) continue;
                    // motionPath binds the writer to the Transform (or
                    // RectTransform), not the renderer the caller passed in.
                    // Treat any Component on the same GameObject as a match
                    // so Remove(renderer) still finds it.
                    var wtObj = c.writer.TargetObject as Component;
                    if (wtObj != null && targetGo != null && wtObj.gameObject == targetGo) continue;
                    return false;
                }
                else return false; // clip-driven: not introspectable here
            }
            return true;
        }

        // ------- Internal hooks -------

        // A parent timeline taking ownership: stop the engine from ticking us
        // and let the parent's OnAfterRender drive Seek().
        internal void ClaimByParent()
        {
            EnsureFinalized();
            if (!m_OwnedByParent)
            {
                m_OwnedByParent = true;
                VMGEngine.CancelDeferredTimeline(this);
                VMGEngine.UnregisterTimeline(this);
            }
            paused = true; // parent drives via Seek, not Tick
        }

        internal bool PromoteToEngine()
        {
            if (m_OwnedByParent) return false;
            EnsureFinalized();
            return true;
        }

        // ------- Build pipeline -------

        void AddAnimationChild(VMGAnimation anim, VMGAt at)
        {
            float offset = ResolvePosition(at);
            float dur = anim.iterationDuration;
            // anime.js considers the full repeat count for child duration on
            // the timeline. For simplicity we use one iteration; infinite
            // loops just hold at the end (timeline doesn't grow to infinity).
            if (!float.IsInfinity(anim.iterationCount) && anim.iterationCount > 1f)
                dur *= anim.iterationCount;

            m_Children.Add(new Child { anim = anim, offset = offset, duration = dur });
            anim.paused = true; // parent (this timeline) drives Seek

            float end = offset + dur;
            // When Duration(...) has locked the timeline, children no longer
            // grow iterationDuration. Truncation is handled by OnAfterRender's
            // existing clamp.
            if (m_ExplicitDuration < 0f && end > iterationDuration)
                iterationDuration = Mathf.Max(0.0001f, end);
        }

        void AddChildTimer(VMGTimer timer, VMGAt at, float duration)
        {
            float offset = ResolvePosition(at);
            m_Children.Add(new Child { timer = timer, offset = offset, duration = duration });
            timer.paused = true;

            float end = offset + duration;
            if (m_ExplicitDuration < 0f && end > iterationDuration)
                iterationDuration = Mathf.Max(0.0001f, end);
        }

        // Pushed onto the builder BEFORE it finalizes, so the default ease
        // actually reaches each tween (anime.js parity). The builder only
        // adopts each default if the user hasn't explicitly set the
        // corresponding modifier — same as anime.js's per-call override.
        void ApplyDefaultsToBuilder(VMGAnimate builder)
        {
            if (m_DefaultDuration > 0f) builder.SetDefaultDuration(m_DefaultDuration);
            if (m_DefaultDelay > 0f) builder.SetDefaultDelay(m_DefaultDelay);
            if (m_HasDefaultEase) builder.SetDefaultEase(m_DefaultEase);
        }

        float ResolvePosition(VMGAt at)
        {
            switch (at.kind)
            {
                case VMGAt.Kind.Absolute:
                    return at.offset;
                case VMGAt.Kind.AfterPrev:
                {
                    if (m_Children.Count == 0) return at.offset;
                    var last = m_Children[m_Children.Count - 1];
                    return last.offset + last.duration + at.offset;
                }
                case VMGAt.Kind.WithPrev:
                {
                    if (m_Children.Count == 0) return at.offset;
                    var last = m_Children[m_Children.Count - 1];
                    return last.offset + at.offset;
                }
                case VMGAt.Kind.MultiplyPrev:
                {
                    // anime.js '*=F' anchors on the previous child's start
                    // + (factor * prevDuration). No previous child → fall
                    // back to timeline end (anime.js does the same).
                    if (m_Children.Count == 0) return iterationDuration;
                    var last = m_Children[m_Children.Count - 1];
                    return last.offset + last.duration * at.offset;
                }
                case VMGAt.Kind.FromLabel:
                {
                    if (at.label != null && m_Labels.TryGetValue(at.label, out var t)) return t + at.offset;
                    return iterationDuration + at.offset; // unknown label falls back to end
                }
                case VMGAt.Kind.End:
                default:
                    return iterationDuration + at.offset;
            }
        }

        void RecomputeIterationDuration()
        {
            // Explicit duration overrides the natural recomputation — the
            // box length is fixed by Duration(...) regardless of children.
            if (m_ExplicitDuration > 0f)
            {
                iterationDuration = m_ExplicitDuration;
                return;
            }
            float end = 0f;
            for (int i = 0; i < m_Children.Count; i++)
            {
                var c = m_Children[i];
                float e = c.offset + c.duration;
                if (e > end) end = e;
            }
            iterationDuration = Mathf.Max(0.0001f, end);
        }

        void EnsureFinalized()
        {
            if (m_Finalized) return;
            m_Finalized = true;
            paused = false; // auto-play (anime.js parity)
        }

        // ------- Rendering -------

        // Track the previous iterationTime so we can detect when a 0-duration
        // child timer (Call / Set) was crossed in the last sweep. VMGTimer's
        // Seek uses fireCallbacks:false (parity with editor scrub), so a Call
        // child's onComplete never fires from a plain Seek — we sweep here
        // instead. -1 marks "no previous sample yet".
        float m_PrevIterationTime = -1f;
        bool m_HasPrevIterationTime;

        protected override void OnAfterRender(float iterationTime)
        {
            // PlaybackEase remaps iterationTime BEFORE child dispatch so
            // every child (anim + raw timer) sees the same eased time.
            // We also track the mapped time as m_PrevIterationTime so the
            // 0-duration child sweep detects crossings in mapped space —
            // otherwise a tiny ease curve at t≈0 could pre-fire callbacks
            // that should sit further along.
            if (m_HasPlaybackEase && iterationDuration > 0f)
            {
                float normT = Mathf.Clamp01(iterationTime / iterationDuration);
                float eased = m_PlaybackEase.Evaluate(normT);
                iterationTime = eased * iterationDuration;
            }

            float prev = m_HasPrevIterationTime ? m_PrevIterationTime : float.NegativeInfinity;

            for (int i = 0, n = m_Children.Count; i < n; i++)
            {
                var c = m_Children[i];
                float local = iterationTime - c.offset;
                if (c.anim != null)
                {
                    if (local < 0f) continue;
                    // Clamp past the end so the child holds its end value.
                    if (local > c.anim.iterationDuration) local = c.anim.iterationDuration;
                    c.anim.Seek(local);
                }
                else if (c.timer != null)
                {
                    // 0-duration timers (Call/Set) fire when the playhead
                    // crosses their offset from one sample to the next. Plain
                    // Seek won't deliver their callback because Render's
                    // fireCallbacks branch is gated to Tick, not Seek.
                    if (c.duration <= 0f)
                    {
                        bool crossedForward = prev < c.offset && iterationTime >= c.offset;
                        if (crossedForward) c.timer.RenderForCallbacks();
                    }
                    else
                    {
                        if (local < 0f) continue;
                        if (local > c.timer.iterationDuration) local = c.timer.iterationDuration;
                        c.timer.Seek(local);
                    }
                }
            }

            m_PrevIterationTime = iterationTime;
            m_HasPrevIterationTime = true;
        }
    }
}
