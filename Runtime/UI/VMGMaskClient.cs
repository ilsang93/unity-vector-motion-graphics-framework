using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VMG.UI
{
    /// Attached to a Graphic that should only render where its parent
    /// VMGMaskGroup has been stamped by any VMGMaskSource. Tests both
    /// the group's bit AND the ancestor standard-Mask bits, so the
    /// visible region is exactly (outer Mask) ∩ (VMG group).
    [AddComponentMenu("VMG/Masking/Mask Client", 2)]
    [RequireComponent(typeof(Graphic))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VMGMaskClient : MonoBehaviour, IMaterialModifier
    {
        Graphic m_Graphic;
        Material m_ClientMaterial;

        Graphic graphic
        {
            get
            {
                if (m_Graphic == null) m_Graphic = GetComponent<Graphic>();
                return m_Graphic;
            }
        }

        void OnEnable() { graphic?.SetMaterialDirty(); }
        void OnDisable()
        {
            if (m_ClientMaterial != null)
            {
                StencilMaterial.Remove(m_ClientMaterial);
                m_ClientMaterial = null;
            }
            graphic?.SetMaterialDirty();
        }

        public Material GetModifiedMaterial(Material baseMaterial)
        {
            var group = GetComponentInParent<VMGMaskGroup>(includeInactive: true);
            if (group == null || !group.isActiveAndEnabled) return baseMaterial;
            int bit = group.StencilId;
            if (bit <= 0) return baseMaterial;

            // A material whose shader has no stencil block can't be masked;
            // warn (once per shader) so the failure is diagnosable.
            VMGMaskMaterialCheck.ValidateStencilCapable(baseMaterial, this, "mask client");

            int parentBits = group.ParentBits;

            // Compute the stencil test:
            //
            // Non-inverted → visible INSIDE the sources:
            //   Comp=Equal, Ref=parentBits|bit, ReadMask=parentBits|bit.
            //   Every enclosing region's bit must be set AND this group's bit
            //   must be set.
            //
            // Inverted → visible OUTSIDE the sources (but still inside any
            // enclosing region):
            //   - Top-level (parentBits==0): the only constraint is "this
            //     group's bit is UNSET". Expressed as Comp=NotEqual, Ref=bit,
            //     ReadMask=bit. NotEqual is required (not Equal/Ref=0) because
            //     Unity's StencilMaterial.Add short-circuits to the unmodified
            //     base material when Ref==0 — which would disable masking
            //     entirely and leak the client everywhere.
            //   - Nested (parentBits!=0): "all ancestor bits set AND this
            //     group's bit unset" = Comp=Equal, Ref=parentBits,
            //     ReadMask=parentBits|bit. Ref!=0 here, so no short-circuit.
            int refValue, readMask;
            CompareFunction comp;
            if (!group.Invert)
            {
                comp = CompareFunction.Equal;
                refValue = parentBits | bit;
                readMask = parentBits | bit;
            }
            else if (parentBits == 0)
            {
                comp = CompareFunction.NotEqual;
                refValue = bit;
                readMask = bit;
            }
            else
            {
                comp = CompareFunction.Equal;
                refValue = parentBits;
                readMask = parentBits | bit;
            }

            // Keep + writeMask=0 ensures the client never modifies the buffer.
            var mat = StencilMaterial.Add(
                baseMaterial,
                refValue,
                StencilOp.Keep,
                comp,
                ColorWriteMask.All,
                readMask,
                0);

            if (m_ClientMaterial != null) StencilMaterial.Remove(m_ClientMaterial);
            m_ClientMaterial = mat;
            return mat;
        }
    }
}
