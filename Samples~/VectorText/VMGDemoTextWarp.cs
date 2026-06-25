using UnityEngine;
using VMG.Text;

namespace VMG.Samples
{
    /// Drop on any GameObject that already has a Vector Text (UI, TMP) or
    /// Vector Text World (TMP) component. Animates the text Warp live each
    /// frame to show that the whole vector-text pipeline — glyph outlines,
    /// fill, stroke and the PowerPoint-style warp — is fully runtime-mutable
    /// without an Animator. In production you'd keyframe `Warp.amount` /
    /// `Warp.secondary` on the component directly.
    ///
    /// Works against the shared base, so it drives either the Canvas or the
    /// World variant. Only the warp is animated here (a base-class field);
    /// fill / stroke / wiggle live on the concrete subclasses and are left to
    /// the inspector.
    [RequireComponent(typeof(VMGVectorTextBase))]
    public sealed class VMGDemoTextWarp : MonoBehaviour
    {
        [Tooltip("Warp shape to animate.")]
        public WarpMode mode = WarpMode.Wave;

        [Tooltip("Peak distortion strength the animation reaches.")]
        [Range(0f, 1f)] public float maxAmount = 0.5f;

        [Tooltip("Oscillations per second of the amount sweep.")]
        public float speed = 0.5f;

        [Tooltip("Secondary warp control (Wave: crest count; Circle: sweep degrees). Held constant.")]
        public float secondary = 2f;

        private VMGVectorTextBase m_Target;

        private void Awake() { m_Target = GetComponent<VMGVectorTextBase>(); }

        private void Update()
        {
            // Warp is a STRUCT field: mutate a local copy, then write it back.
            // (Editing m_Target.Warp.amount in place would change a value-copy
            // and be lost — the same gotcha the package documents elsewhere.)
            var w = m_Target.Warp;
            w.mode = mode;
            w.secondary = secondary;
            // Ping-pong the amount through a smooth sine so the text breathes
            // between flat and fully warped.
            float s = (Mathf.Sin(Time.time * speed * Mathf.PI * 2f) * 0.5f) + 0.5f;
            w.amount = s * maxAmount;
            m_Target.Warp = w;

            // The component's own dirty gate sees the warp struct change and
            // re-meshes; no explicit rebuild call needed in play mode.
        }
    }
}
