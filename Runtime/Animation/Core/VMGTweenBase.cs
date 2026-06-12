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

        public abstract void Evaluate(float iterationTime);
    }
}
