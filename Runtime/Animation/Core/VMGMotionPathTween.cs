using UnityEngine;

namespace VMG.Animation.Core
{
    // Drives a Vector3 channel ("position", or any other Vector3 field path)
    // along an arc-length parametrized VMGMotionPath. Eased local time -> u,
    // sampled point + tangent. If a rotation writer is attached, the tween
    // also writes the tangent angle into a float channel (default
    // eulerAngles.z) so the target rotates to face along the curve.
    //
    // Not a VMGCodeTween subclass: code tween is a from->to lerp, this is a
    // sampled-at-t curve walker. Sharing the base keeps the time window
    // semantics + composition rules ("later wins per channel") identical.
    internal sealed class VMGMotionPathTween : VMGTweenBase
    {
        public VMGChannelWriter positionWriter;
        public VMGChannelReader positionReader;
        public VMGMotionPath path;
        public VMGEase ease;

        // The path's points live in 2D. When the writer expects a Vector3
        // (most common — Transform.position), we treat the 2D point as XY
        // and preserve the current Z, captured lazily on first evaluate.
        // For a Vector2 writer we just write the XY directly.
        public bool isVector3Channel;
        float m_BaselineZ;
        bool m_CapturedBaselineZ;

        // Optional rotation slave (auto-rotate). Wired by VMGAnimate when the
        // user calls .AutoRotate(). When null, only position is written.
        // rotationReader is wired alongside the writer so Revert can snapshot
        // the pre-tween angle (independent from the Z-preservation baseline,
        // which is a runtime concern).
        public VMGChannelWriter rotationWriter;
        public VMGChannelReader rotationReader;
        public float rotationOffsetDeg;

        // Revert baseline captured once on first Evaluate. Independent of
        // m_BaselineZ (which is a runtime-only Z preservation for Vector3
        // channels). VMGAnimation.Revert clears the flag via
        // ResetBaselineForRevert so a Revert→Restart re-snapshots.
        bool m_HasRegisteredRevert;

        public override void Evaluate(float iterationTime)
        {
            if (path == null || !path.IsValid) return;

            if (!m_HasRegisteredRevert) RegisterRevertBaseline();

            float span = endTime - startTime;
            float local = span > 0f ? (iterationTime - startTime) / span : 1f;
            if (local < 0f) local = 0f;
            else if (local > 1f) local = 1f;

            float u = ease.Evaluate(local);
            path.Sample(u, out Vector2 point, out Vector2 tangent);

            if (isVector3Channel)
            {
                if (!m_CapturedBaselineZ && positionReader != null)
                {
                    object boxed = positionReader.Read();
                    if (boxed is Vector3 v3) m_BaselineZ = v3.z;
                    m_CapturedBaselineZ = true;
                }
                positionWriter.Write(new Vector3(point.x, point.y, m_BaselineZ));
            }
            else
            {
                positionWriter.Write(point);
            }

            if (rotationWriter != null)
            {
                float angleDeg = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + rotationOffsetDeg;
                rotationWriter.Write(angleDeg);
            }
        }

        void RegisterRevertBaseline()
        {
            m_HasRegisteredRevert = true;
            if (owner == null) return;
            if (positionWriter != null && positionReader != null)
            {
                object posBoxed = positionReader.Read();
                if (posBoxed != null)
                {
                    var kind = isVector3Channel
                        ? VMGAnimation.RevertWriteKind.Vector3
                        : VMGAnimation.RevertWriteKind.Vector2;
                    owner.EnsureRevertBaseline(positionWriter, kind, posBoxed);
                }
            }
            if (rotationWriter != null && rotationReader != null)
            {
                object rotBoxed = rotationReader.Read();
                if (rotBoxed != null)
                {
                    owner.EnsureRevertBaseline(rotationWriter, VMGAnimation.RevertWriteKind.Float, rotBoxed);
                }
            }
        }

        // Called by VMGAnimation.Revert(): drop the captured flag so the
        // next play cycle re-snapshots. Also drops the runtime Z baseline so
        // a Revert→Restart cycle re-reads the (just-restored) Z value rather
        // than holding the pre-Revert Z.
        internal void ResetBaselineForRevert()
        {
            m_HasRegisteredRevert = false;
            m_CapturedBaselineZ = false;
        }
    }
}
