using System.Collections.Generic;

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

        // Re-evaluate every tween's FunctionValue slots on the next sample.
        // No-op for tweens without lazy values.
        public void Refresh()
        {
            for (int i = 0, n = tweens.Count; i < n; i++)
            {
                if (tweens[i] is VMGCodeTween ct) ct.Refresh();
            }
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
