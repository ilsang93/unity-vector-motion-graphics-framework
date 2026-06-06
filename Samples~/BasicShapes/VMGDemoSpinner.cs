using UnityEngine;
using VMG.Core;
using VMG.UI;

namespace VMG.Samples
{
    /// Drop on a Vector Image. Drives the trim modifier directly each frame,
    /// to show that VMG state is fully runtime-mutable. In production you
    /// would animate the same fields via AnimationClip / Timeline.
    [RequireComponent(typeof(VectorImageGraphic))]
    public sealed class VMGDemoSpinner : MonoBehaviour
    {
        public float speed = 0.5f;
        public float sweep = 0.35f;

        private VectorImageGraphic m_Target;

        private void Awake() { m_Target = GetComponent<VectorImageGraphic>(); }

        private void Update()
        {
            // ref local — TrimPathModifier is now a struct, so the
            // mutations below need to land on the renderer's actual
            // field (not a copy). `ref var ... = ref ...` keeps the
            // reference semantics through the assignment.
            ref var trim = ref m_Target.TrimModifier;
            trim.enabled = true;
            trim.offset = (Time.time * speed) % 1f;
            trim.start = 0f;
            trim.end = sweep;
        }
    }
}
