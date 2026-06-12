using System;

namespace VMG.Animation.Core
{
    // Base time-driver. Owns the playhead, loop counting, and direction.
    // anime.js's Timer is the common base for Animation and Timeline; we
    // mirror that so future Timeline composition can plug in without
    // rewriting the time logic.
    public class VMGTimer
    {
        // Duration of one iteration. >0 required for a useful timer; setters
        // (duration == 0) are not modelled in this PoC.
        public float iterationDuration;

        // Total loop count. 1 = play once. <=0 or float.PositiveInfinity = infinite.
        public float iterationCount = 1f;

        // Inter-iteration gap.
        public float loopDelay;

        // Initial wait before the first iteration starts.
        public float startDelay;

        // Playback rate multiplier on top of engine tick delta.
        public float speed = 1f;

        // Direction flags. alternate flips the iteration direction on each
        // odd iteration; reversed flips the base direction.
        public bool alternate;
        public bool reversed;

        public bool paused = true;
        public bool began;
        public bool completed;

        public event Action<VMGTimer> onBegin;
        public event Action<VMGTimer> onBeforeUpdate;
        public event Action<VMGTimer> onUpdate;
        public event Action<VMGTimer> onLoop;
        public event Action<VMGTimer> onComplete;
        public event Action<VMGTimer> onPause;

        // Absolute time within this timer, including startDelay.
        // Range: [-startDelay, totalDuration].
        protected float m_CurrentTime;

        // Time within the current iteration, after easing/direction resolution.
        // Range: [0, iterationDuration].
        protected float m_IterationTime;

        protected int m_CurrentIteration;

        public float CurrentTime => m_CurrentTime;
        public float IterationTime => m_IterationTime;
        public int CurrentIteration => m_CurrentIteration;

        public float TotalDuration
        {
            get
            {
                if (float.IsInfinity(iterationCount) || iterationCount <= 0f) return float.PositiveInfinity;
                return (iterationDuration + loopDelay) * iterationCount - loopDelay;
            }
        }

        public float Progress
        {
            get
            {
                float d = TotalDuration;
                if (float.IsInfinity(d) || d <= 0f) return 0f;
                return UnityEngine.Mathf.Clamp01(m_CurrentTime / d);
            }
        }

        public void Play()
        {
            if (completed)
            {
                m_CurrentTime = -startDelay;
                m_IterationTime = 0f;
                m_CurrentIteration = 0;
                began = false;
                completed = false;
            }
            paused = false;
        }

        public void Pause()
        {
            // Idempotent + only fires onPause on the true transition. anime.js
            // parity: a no-op Pause doesn't re-notify. Completion-driven
            // paused=true (handled inside Render) deliberately does NOT fire
            // onPause — onComplete already covers that case.
            if (paused) return;
            paused = true;
            onPause?.Invoke(this);
        }

        public void Stop()
        {
            paused = true;
            Seek(0f);
            began = false;
            completed = false;
        }

        public void Seek(float absoluteTime)
        {
            // Drive the timer to an explicit time without auto-advance.
            // Used by external mode and editor scrub.
            Render(absoluteTime, fireCallbacks: false);
        }

        // Toggle direction at the current playhead and keep playing.
        // anime.js parity for reverse(). Differs from Reversed(bool), which
        // is a construction-time direction set.
        //
        // To keep iterationTime continuous across the flip we mirror
        // m_CurrentTime around the timeline midpoint — the same playhead
        // location now maps to the same iterTime under the new direction.
        // This is anime.js's behavior: Progress jumps to (1 - p), which
        // matches the mental model "what was ahead is now behind."
        //
        // If the timer has completed, the flip also clears the completion
        // flag and resumes playing — anime.js does the same.
        public void Reverse()
        {
            float total = TotalDuration;
            bool isInfinite = float.IsInfinity(total) || total <= 0f;

            reversed = !reversed;

            if (!isInfinite)
            {
                // Mirror only the in-bounds portion of CurrentTime. The
                // startDelay region (negative) stays as-is — flipping into
                // a delay region would be surprising.
                if (m_CurrentTime >= 0f && m_CurrentTime <= total)
                    m_CurrentTime = total - m_CurrentTime;
            }

            if (completed)
            {
                // Was at the end; the flip puts us at the start of the
                // reversed playback. Clear flags + resume.
                began = false;
                completed = false;
            }
            paused = false;
        }

        // Used by VMGTimeline to fire 0-duration child timers' onComplete
        // when the parent playhead crosses them. Plain Seek can't do this
        // because its fireCallbacks=false suppresses begin/update/complete.
        internal void RenderForCallbacks()
        {
            Render(iterationDuration, fireCallbacks: true);
        }

        public void Tick(float deltaTime)
        {
            if (paused || completed) return;
            float scaled = deltaTime * speed;
            Render(m_CurrentTime + scaled, fireCallbacks: true);
        }

        // Render is the single point that maps absolute time to (iteration,
        // iterationTime, direction) and fires lifecycle callbacks. Subclasses
        // (Animation) override OnAfterRender to write tween values.
        protected void Render(float newAbsoluteTime, bool fireCallbacks)
        {
            float total = TotalDuration;
            bool isInfinite = float.IsInfinity(total);
            float delayed = newAbsoluteTime - 0f; // startDelay handled by clamping below

            // Clamp to [-startDelay, total].
            float effectiveMin = -startDelay;
            float effectiveMax = isInfinite ? float.PositiveInfinity : total;
            if (newAbsoluteTime < effectiveMin) newAbsoluteTime = effectiveMin;
            if (newAbsoluteTime > effectiveMax) newAbsoluteTime = effectiveMax;

            m_CurrentTime = newAbsoluteTime;

            float activeTime = newAbsoluteTime; // time past startDelay+0 origin
            bool aboveZero = activeTime > 0f;
            bool atOrPastEnd = !isInfinite && activeTime >= total;

            int prevIteration = m_CurrentIteration;
            int iteration = 0;
            float iterTime;

            if (iterationCount > 1f || float.IsInfinity(iterationCount))
            {
                float iterSpan = iterationDuration + loopDelay;
                if (iterSpan > 0f)
                {
                    iteration = (int)(activeTime / iterSpan);
                    if (atOrPastEnd) iteration = (int)iterationCount - 1;
                    if (iteration < 0) iteration = 0;
                    float within = activeTime - iteration * iterSpan;
                    iterTime = within > iterationDuration ? iterationDuration : within;
                }
                else
                {
                    iterTime = 0f;
                }
            }
            else
            {
                iterTime = activeTime;
                if (iterTime < 0f) iterTime = 0f;
                if (iterTime > iterationDuration) iterTime = iterationDuration;
            }

            bool isOddIteration = (iteration & 1) == 1;
            bool effectiveReversed = reversed ^ (alternate && isOddIteration);

            if (atOrPastEnd)
                iterTime = effectiveReversed ? 0f : iterationDuration;
            else if (effectiveReversed)
                iterTime = iterationDuration - iterTime;

            m_IterationTime = iterTime;
            m_CurrentIteration = iteration;

            // onBeforeUpdate fires inside the callback gate (same as
            // onUpdate). Editor scrub / external Seek paths go through
            // Render with fireCallbacks=false and intentionally skip it.
            if (fireCallbacks) onBeforeUpdate?.Invoke(this);

            OnAfterRender(iterTime);

            if (fireCallbacks)
            {
                if (aboveZero && !began)
                {
                    began = true;
                    onBegin?.Invoke(this);
                }
                if (aboveZero && iteration != prevIteration && !atOrPastEnd)
                {
                    onLoop?.Invoke(this);
                }
                if (aboveZero) onUpdate?.Invoke(this);
                if (atOrPastEnd && !completed)
                {
                    completed = true;
                    paused = true;
                    onComplete?.Invoke(this);
                }
            }
        }

        protected virtual void OnAfterRender(float iterationTime) { }
    }
}
