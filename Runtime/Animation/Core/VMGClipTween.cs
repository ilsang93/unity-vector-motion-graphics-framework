namespace VMG.Animation.Core
{
    // Clip-driven tween. Evaluation goes through VMGTrackEvaluator so results
    // are bit-identical to the original VMGAnimator path; the writer + last-
    // value cache + skip optimization live on VMGResolvedTrack.
    //
    // This is the tween shape used by VMGClipCompiler. Code-driven tweens use
    // VMGCodeTween (single from->to with an ease curve).
    internal class VMGClipTween : VMGTweenBase
    {
        public VMGResolvedTrack resolved;

        public override void Evaluate(float iterationTime)
        {
            var track = resolved.track;
            switch (track.type)
            {
                case VMGChannelType.Float:
                {
                    float v = VMGTrackEvaluator.EvaluateFloat(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastFloat == v) return;
                    resolved.writer.Write(v);
                    resolved.lastFloat = v;
                    resolved.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Int:
                {
                    int v = VMGTrackEvaluator.EvaluateInt(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastInt == v) return;
                    resolved.writer.Write(v);
                    resolved.lastInt = v;
                    resolved.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Bool:
                {
                    bool v = VMGTrackEvaluator.EvaluateBool(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastBool == v) return;
                    resolved.writer.Write(v);
                    resolved.lastBool = v;
                    resolved.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Color:
                {
                    UnityEngine.Color v = VMGTrackEvaluator.EvaluateColor(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastColor == v) return;
                    resolved.writer.Write(v);
                    resolved.lastColor = v;
                    resolved.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector2:
                {
                    UnityEngine.Vector4 v = VMGTrackEvaluator.EvaluateVector(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastVector == v) return;
                    resolved.writer.Write((UnityEngine.Vector2)v);
                    resolved.lastVector = v;
                    resolved.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector3:
                {
                    UnityEngine.Vector4 v = VMGTrackEvaluator.EvaluateVector(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastVector == v) return;
                    resolved.writer.Write((UnityEngine.Vector3)v);
                    resolved.lastVector = v;
                    resolved.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector4:
                {
                    UnityEngine.Vector4 v = VMGTrackEvaluator.EvaluateVector(track, iterationTime);
                    if (resolved.hasLastValue && resolved.lastVector == v) return;
                    resolved.writer.Write(v);
                    resolved.lastVector = v;
                    resolved.hasLastValue = true;
                    return;
                }
            }
        }
    }
}
