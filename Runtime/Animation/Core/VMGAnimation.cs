using System.Collections.Generic;
#if UNITY_6000_5_OR_NEWER
using VMGObjectId = UnityEngine.EntityId;
#else
using VMGObjectId = System.Int32;
#endif

namespace VMG.Animation.Core
{
    // Timer + a list of tweens that share the same iteration timeline.
    // Mirrors anime.js's JSAnimation: one animation, many properties, one
    // playhead.
    public class VMGAnimation : VMGTimer
    {
        internal readonly List<VMGTweenBase> tweens = new List<VMGTweenBase>();

        // Fired after every render so external systems (e.g. editor Record
        // mode, child-inspector refresh) can re-snapshot. Parity hook with
        // VMGAnimator.AfterSample.
        public event System.Action<VMGAnimation> onSampled;

        // When true, the start of every new iteration auto-refreshes
        // FunctionValue slots — "new random per loop". anime.js doesn't
        // have a direct flag for this; the workaround there is to call
        // refresh() inside onLoop. We expose the flag so authors don't
        // have to wire the callback themselves.
        public bool refreshOnLoop;

        // Tracks the iteration we last sampled so RefreshOnLoop can detect
        // crossings without piggybacking on the (post-Evaluate) onLoop
        // event. Initialized to -1 so the first frame doesn't trip it.
        int m_LastSampledIteration = -1;

        // Revert baselines, keyed per (target, fieldPath). Captured lazily by
        // each tween the FIRST time it writes to a channel (parallel to
        // CaptureFrom). One slot per channel regardless of how many tweens
        // target it — Revert restores "the value before THIS animation began
        // writing anything", which is what anime.js's revert() means.
        // Independent of FunctionValue.Refresh (which clears hasFrom): the
        // baseline is set once and stays.
        internal struct RevertKey : System.IEquatable<RevertKey>
        {
            public VMGObjectId targetInstanceID;
            public string fieldPath;
            public bool Equals(RevertKey other) => targetInstanceID == other.targetInstanceID && fieldPath == other.fieldPath;
            public override bool Equals(object obj) => obj is RevertKey k && Equals(k);
            public override int GetHashCode() => unchecked(targetInstanceID.GetHashCode() * 397) ^ (fieldPath != null ? fieldPath.GetHashCode() : 0);
        }
        internal struct RevertEntry
        {
            public object boxedValue;
            public VMGAnimation.RevertWriteKind kind;
            public VMGChannelWriter writer; // one valid writer for the channel; used to write the baseline back
        }
        internal enum RevertWriteKind
        {
            Float, Int, Bool, Color, Vector2, Vector3, Vector4,
        }
        internal readonly Dictionary<RevertKey, RevertEntry> m_RevertBaselines = new Dictionary<RevertKey, RevertEntry>();

        // Re-evaluate every tween's FunctionValue slots on the next sample.
        // No-op for tweens without lazy values.
        public void Refresh()
        {
            for (int i = 0, n = tweens.Count; i < n; i++)
            {
                if (tweens[i] is VMGCodeTween ct) ct.Refresh();
            }
        }

        // Restore every channel this animation has written to back to the
        // value it held before this animation's first write. anime.js's
        // revert(): snap-back, not animated; this is distinct from Reverse()
        // (which is direction-flip + continue playing).
        //
        // Stops the timer too — the contract is "undo + stop", parity with
        // anime.js where revert() also pauses the animation. Callers wanting
        // a snap-back-and-replay can call Restart() afterward.
        public void Revert() => EndAndClear(restoreBaseline: true);

        // "Abort current tween, keep current channel value, allow a fresh
        // start." Like Revert but skips the baseline writes — the channel
        // stays at whatever the last Evaluate wrote. Tween flags reset so the
        // next play cycle re-captures from-side from the current value.
        //
        // Use case: toggle / re-press patterns where the old handle is being
        // discarded and a new tween will take over. Pause is wrong here
        // because the old handle would remain resumable with its original
        // from→to; Revert is wrong because it snaps the channel back to the
        // pre-animation baseline, which flashes during fast re-press.
        public void Cancel() => EndAndClear(restoreBaseline: false);

        void EndAndClear(bool restoreBaseline)
        {
            // Stop FIRST so Render at t=0 (triggered by Stop's Seek) happens
            // before the baseline writes — otherwise the t=0 sample would
            // overwrite the baseline we just restored. Stopping also clears
            // hasFrom/hasLastValue via the reset loop below so the next
            // play cycle behaves like a fresh start.
            paused = true;
            // Mimic Stop's bookkeeping without the Seek-to-0 render: Seek
            // would re-evaluate every tween at iterationTime=0, which both
            // writes the start values back and (post-baseline-write) clobbers
            // the restored channel state.
            m_CurrentTime = -startDelay;
            m_IterationTime = 0f;
            m_CurrentIteration = 0;
            began = false;
            completed = false;

            if (restoreBaseline)
            {
                foreach (var kv in m_RevertBaselines)
                {
                    var entry = kv.Value;
                    if (entry.writer == null) continue;
                    switch (entry.kind)
                    {
                        case RevertWriteKind.Float: entry.writer.Write((float)entry.boxedValue); break;
                        case RevertWriteKind.Int: entry.writer.Write((int)entry.boxedValue); break;
                        case RevertWriteKind.Bool: entry.writer.Write((bool)entry.boxedValue); break;
                        case RevertWriteKind.Color: entry.writer.Write((UnityEngine.Color)entry.boxedValue); break;
                        case RevertWriteKind.Vector2: entry.writer.Write((UnityEngine.Vector2)entry.boxedValue); break;
                        case RevertWriteKind.Vector3: entry.writer.Write((UnityEngine.Vector3)entry.boxedValue); break;
                        case RevertWriteKind.Vector4: entry.writer.Write((UnityEngine.Vector4)entry.boxedValue); break;
                    }
                }
            }
            for (int i = 0, n = tweens.Count; i < n; i++)
            {
                if (tweens[i] is VMGCodeTween ct)
                {
                    ct.hasFrom = false;
                    ct.hasLastValue = false;
                    ct.ResetRevertCaptureFlag();
                }
                else if (tweens[i] is VMGMotionPathTween mt)
                {
                    mt.ResetBaselineForRevert();
                }
            }
            m_RevertBaselines.Clear();
        }

        // Called by VMGCodeTween / VMGMotionPathTween on first Evaluate to
        // snapshot the channel before any write happens. No-op if the slot
        // already exists (per-channel single capture rule).
        internal void EnsureRevertBaseline(VMGChannelWriter writer, RevertWriteKind kind, object boxedValue)
        {
            if (writer == null || writer.FieldPath == null) return;
            var key = new RevertKey { targetInstanceID = writer.TargetInstanceID, fieldPath = writer.FieldPath };
            if (m_RevertBaselines.ContainsKey(key)) return;
            m_RevertBaselines[key] = new RevertEntry { boxedValue = boxedValue, kind = kind, writer = writer };
        }

        protected override void OnAfterRender(float iterationTime)
        {
            // Iteration-boundary refresh runs BEFORE tween evaluation so the
            // first frame of the new iteration already sees the new values.
            if (refreshOnLoop && m_CurrentIteration != m_LastSampledIteration)
            {
                if (m_LastSampledIteration >= 0) Refresh();
                m_LastSampledIteration = m_CurrentIteration;
            }

            // Walk every tween. Each tween writes only if (a) the playhead
            // is inside its [startTime, endTime] window — currently the
            // full iteration for clip-driven animations — and (b) the
            // last-written value differs.
            for (int i = 0, n = tweens.Count; i < n; i++)
            {
                var t = tweens[i];
                if (iterationTime < t.startTime) continue;
                if (iterationTime > t.endTime)
                {
                    // Hold the end value by sampling at endTime exactly. The
                    // VMGTrackEvaluator already clamps past-end to the last
                    // key, so this is safe.
                    t.Evaluate(t.endTime);
                    continue;
                }
                t.Evaluate(iterationTime);
            }
            onSampled?.Invoke(this);
        }
    }
}
