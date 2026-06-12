namespace VMG.Animation.Core
{
    // Base for all tweens owned by a VMGAnimation. Two concrete kinds today:
    // VMGClipTween (drives a resolved Clip track via VMGTrackEvaluator) and
    // VMGCodeTween (single from->to with an easing curve, built from code).
    internal abstract class VMGTweenBase
    {
        // Time window within the owning animation's iteration.
        public float startTime;
        public float endTime;

        // Back-pointer used by code-driven tweens to register revert
        // baselines on first Evaluate. Set when the tween is added to an
        // animation's list. Null for clip-driven tweens (clip data is the
        // baseline — Revert on clip tweens is a no-op by design).
        public VMGAnimation owner;

        public abstract void Evaluate(float iterationTime);
    }
}
