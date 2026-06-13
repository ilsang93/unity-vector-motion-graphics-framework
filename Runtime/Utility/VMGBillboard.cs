using UnityEngine;

namespace VMG.Utility
{
    /// Rotates a GameObject so a chosen local axis (default +Z) faces the
    /// active camera or a user-set Transform. Default is screen-aligned —
    /// the GameObject inherits the camera's rotation so it always reads
    /// flat to the viewer. Constrained modes (YAxis / ZAxis) restrict
    /// rotation to one axis for sign-board or 2D-marker behaviour.
    ///
    /// Slot policy (no enum):
    ///   - `TargetTransform` set → Transform-follow mode
    ///   - `TargetTransform` null + `TargetCamera` set → that camera
    ///   - both null → Camera.main → any active camera → SceneView (edit-mode)
    ///
    /// Always face-axis is +Z by default to match VMG renderer convention
    /// (front of a 2D shape mesh). Use `FaceAxis` for other mesh layouts.
    ///
    /// `TiltOffset` is a local-space Euler rotation applied AFTER alignment
    /// — animate it via VMGAnimator for "wobble while facing camera"
    /// patterns.
    [AddComponentMenu("VMG/Billboard")]
    [ExecuteAlways]
    [DefaultExecutionOrder(int.MaxValue / 2)]
    public sealed class VMGBillboard : MonoBehaviour
    {
        public enum BillboardMode
        {
            /// Full alignment to camera plane. Flat to viewer at any angle.
            Full = 0,
            /// Rotate only around Y. Signboard / tree / 3D-world label.
            YAxis = 1,
            /// Rotate only around Z. 2D marker / screen-space arrow.
            ZAxis = 2,
        }

        public enum Axis
        {
            ZPositive = 0,
            ZNegative = 1,
            YPositive = 2,
            YNegative = 3,
            XPositive = 4,
            XNegative = 5,
        }

        [Tooltip("Follow this Transform instead of a camera. When set, the camera slot is ignored.")]
        public Transform TargetTransform;

        [Tooltip("Camera to face when TargetTransform is null. Empty = auto (Camera.main → any active camera → SceneView in edit mode).")]
        public Camera TargetCamera;

        [Tooltip("Rotation mode. Full = screen-aligned (always flat to camera). YAxis = signboard. ZAxis = 2D marker.")]
        public BillboardMode Mode = BillboardMode.Full;

        [Tooltip("Which local axis points toward the camera/target. Default +Z matches VMG renderer convention.")]
        public Axis FaceAxis = Axis.ZPositive;

        [Tooltip("Additional local-space Euler rotation applied AFTER billboard alignment. Keyframable — animate for wobble/tilt on top of camera-facing.")]
        public Vector3 TiltOffset;

        // One-shot warning state for non-uniform parent scale (decision 7).
        bool m_WarnedNonUniformParent;

        void LateUpdate()
        {
            Apply();
        }

#if UNITY_EDITOR
        void OnRenderObject()
        {
            // In edit mode without play, LateUpdate fires only when something
            // changes the scene. Re-apply on every render so the billboard
            // tracks SceneView camera orbit smoothly.
            if (!Application.isPlaying) Apply();
        }
#endif

        void Apply()
        {
            Vector3 targetPos;
            Quaternion cameraRot;
            if (!ResolveTarget(out targetPos, out cameraRot)) return;

            CheckNonUniformParent();

            Quaternion aligned = ComputeAlignment(targetPos, cameraRot);
            Quaternion withAxis = aligned * AxisRemap(FaceAxis);
            Quaternion withTilt = withAxis * Quaternion.Euler(TiltOffset);

            // World-space set so parent rotation is naturally bypassed
            // (decision 7). Unity converts to localRotation internally.
            transform.rotation = withTilt;
        }

        bool ResolveTarget(out Vector3 pos, out Quaternion camRot)
        {
            pos = default;
            camRot = Quaternion.identity;

            if (TargetTransform != null)
            {
                pos = TargetTransform.position;
                // Transform-follow uses Full/YAxis/ZAxis LookAt math even in
                // Full mode (no "camera rotation" to copy when target is a
                // Transform, not a camera).
                camRot = Quaternion.LookRotation(pos - transform.position, Vector3.up);
                return true;
            }

            Camera cam = ResolveCamera();
            if (cam == null) return false;
            pos = cam.transform.position;
            camRot = cam.transform.rotation;
            return true;
        }

        Camera ResolveCamera()
        {
            if (TargetCamera != null) return TargetCamera;
            if (Camera.main != null) return Camera.main;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null) return sv.camera;
            }
#endif

            // Last resort: any active Camera in the scene. Cheap fallback,
            // not cached because scene cameras come and go in edit mode.
            return Camera.current != null ? Camera.current
                                          : FindAnyActiveCamera();
        }

        static Camera FindAnyActiveCamera()
        {
            var all = Camera.allCameras;
            return all != null && all.Length > 0 ? all[0] : null;
        }

        Quaternion ComputeAlignment(Vector3 targetPos, Quaternion cameraRot)
        {
            switch (Mode)
            {
                case BillboardMode.Full:
                    // Screen-aligned: inherit camera rotation so we lie flat
                    // to the viewer regardless of position on screen.
                    return cameraRot;

                case BillboardMode.YAxis:
                {
                    // Rotate around world Y only — signboard.
                    Vector3 toTarget = targetPos - transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude < 1e-8f) return transform.rotation;
                    return Quaternion.LookRotation(toTarget, Vector3.up);
                }

                case BillboardMode.ZAxis:
                {
                    // Rotate around local Z only — 2D marker that spins
                    // within the screen plane to point at the target.
                    // Project target into the GameObject's local XY plane.
                    Vector3 local = transform.parent != null
                        ? transform.parent.InverseTransformPoint(targetPos)
                        : targetPos;
                    Vector3 selfLocal = transform.localPosition;
                    float dx = local.x - selfLocal.x;
                    float dy = local.y - selfLocal.y;
                    if (dx * dx + dy * dy < 1e-8f) return transform.rotation;
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    Quaternion baseLocal = Quaternion.Euler(0f, 0f, angle);
                    return transform.parent != null
                        ? transform.parent.rotation * baseLocal
                        : baseLocal;
                }
            }
            return cameraRot;
        }

        static readonly Quaternion[] s_AxisRemap = new Quaternion[]
        {
            Quaternion.identity,                            // Z+ (default; matches Quad/LookRotation conv)
            Quaternion.Euler(0f, 180f, 0f),                 // Z-
            Quaternion.Euler(90f, 0f, 0f),                  // Y+
            Quaternion.Euler(-90f, 0f, 0f),                 // Y-
            Quaternion.Euler(0f, -90f, 0f),                 // X+
            Quaternion.Euler(0f, 90f, 0f),                  // X-
        };

        static Quaternion AxisRemap(Axis a)
        {
            int i = (int)a;
            return i >= 0 && i < s_AxisRemap.Length ? s_AxisRemap[i] : Quaternion.identity;
        }

        void CheckNonUniformParent()
        {
            if (m_WarnedNonUniformParent) return;
            if (transform.parent == null) return;
            Vector3 s = transform.parent.lossyScale;
            if (Mathf.Approximately(s.x, s.y) && Mathf.Approximately(s.y, s.z)) return;
            m_WarnedNonUniformParent = true;
            Debug.LogWarning(
                $"[VMG Billboard] '{name}' has a non-uniform parent scale " +
                $"({s.x:F3}, {s.y:F3}, {s.z:F3}). Billboard alignment may shear. " +
                $"Use uniform scale on the parent chain.", this);
        }
    }
}
