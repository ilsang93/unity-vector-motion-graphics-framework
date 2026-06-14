using UnityEngine;
using VMG.UI;

namespace VMG.Samples
{
    /// Drop on a Vector Image whose ShapeStack has at least two slots
    /// pre-configured (e.g. Slot 0 = Circle, Slot 1 = Rectangle). The
    /// component ping-pongs `Slot 0` ↔ `Slot 1` intensities so the shape
    /// continuously morphs between the two without requiring an
    /// AnimationClip. Production code typically drives the same fields
    /// from the Animator instead — this is just the simplest live demo.
    [RequireComponent(typeof(VectorImageGraphic))]
    public sealed class VMGDemoShapeMorph : MonoBehaviour
    {
        [Tooltip("Full morph cycles per second (one cycle = A→B→A).")]
        public float speed = 0.4f;

        [Tooltip("Smoothstep the linear ping-pong so the morph dwells on each shape briefly. 0 = linear, 1 = full smoothstep.")]
        [Range(0f, 1f)] public float ease = 1f;

        private VectorImageGraphic m_Target;

        private void Awake() { m_Target = GetComponent<VectorImageGraphic>(); }

        private void Update()
        {
            // Triangle wave in [0, 1] — equal time on the rising and
            // falling edges so neither shape is favoured.
            float t = Mathf.PingPong(Time.time * speed * 2f, 1f);
            float w = Mathf.Lerp(t, t * t * (3f - 2f * t), ease);

            // ShapeStack is a struct field; need ref semantics so the
            // intensity writes land on the renderer's actual instance.
            // The renderer's dirty-flag picks up the change via its
            // snapshot equality check — no explicit SetMeshDirty needed.
            ref var stack = ref m_Target.ShapeStack;
            stack.Slot0.intensity = 1f - w;
            stack.Slot1.intensity = w;
        }
    }
}
