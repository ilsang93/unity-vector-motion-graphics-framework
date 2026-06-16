using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VMG.UI
{
    /// Attached to a Graphic that should only render where its parent
    /// VMGMaskGroup has been stamped by any VMGMaskSource. Tests both
    /// the group's bit AND the ancestor standard-Mask bits, so the
    /// visible region is exactly (outer Mask) ∩ (VMG group).
    [AddComponentMenu("UI/VMG/Mask Client", 14)]
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

            int parentBits = group.ParentBits;
            int refValue = parentBits | bit;
            int readMask = parentBits | bit;

            // Render only where the entire (parentBits | bit) pattern
            // matches — i.e. every ancestor Mask stamped its bit AND the
            // VMG group stamped its bit. Keep + writeMask=0 ensures the
            // client never modifies the stencil buffer.
            var mat = StencilMaterial.Add(
                baseMaterial,
                refValue,
                StencilOp.Keep,
                CompareFunction.Equal,
                ColorWriteMask.All,
                readMask,
                0);

            if (m_ClientMaterial != null) StencilMaterial.Remove(m_ClientMaterial);
            m_ClientMaterial = mat;
            return mat;
        }
    }
}
