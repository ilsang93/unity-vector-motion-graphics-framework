using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace VMG.UI
{
    /// Attached to each child Graphic of a VMGMaskGroup. Injects a
    /// stencil-write material whose write/read masks are restricted to
    /// the group's single bit, so overlapping groups don't trample each
    /// other's stamp. Colour is suppressed by default — the graphic
    /// exists purely to drive the stencil.
    ///
    /// Nesting under a Unity standard Mask is honoured: the group caches
    /// the parent stencil depth, and this source stamps only where the
    /// ancestor Mask region has already been written. Effectively the
    /// VMG mask region is clipped by the standard Mask.
    ///
    /// On enable, also forces the host VectorImageGraphic's Fill to be
    /// enabled. Without rasterised pixels there's nothing to stamp the
    /// stencil with; the fill colour itself is invisible since the
    /// material masks out colour writes when ShowSource is false.
    [AddComponentMenu("UI/VMG/Mask Source", 13)]
    [RequireComponent(typeof(Graphic))]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VMGMaskSource : MonoBehaviour, IMaterialModifier
    {
        [Tooltip("Whether the source graphic itself stays visible. When false (default) the source acts as a pure mask shape and its own pixels are skipped, only the stencil stamp survives. Turn on to debug the mask region.")]
        public bool ShowSource = false;

        Graphic m_Graphic;
        Material m_MaskMaterial;

        Graphic graphic
        {
            get
            {
                if (m_Graphic == null) m_Graphic = GetComponent<Graphic>();
                return m_Graphic;
            }
        }

        void OnEnable()
        {
            EnsureFillEnabled();
            graphic?.SetMaterialDirty();
        }

        void OnDisable()
        {
            if (m_MaskMaterial != null)
            {
                StencilMaterial.Remove(m_MaskMaterial);
                m_MaskMaterial = null;
            }
            graphic?.SetMaterialDirty();
        }

        void EnsureFillEnabled()
        {
            var v = graphic as VectorImageGraphic;
            if (v == null) return;
            if (!v.Fill.enabled || v.Fill.color.a <= 0f)
            {
                var f = v.Fill;
                f.enabled = true;
                f.color = Color.white;
                v.Fill = f;
                v.SetMeshDirty();
            }
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
            int writeMask = bit;

            // CompareFunction.Equal so the stamp only lands where every
            // ancestor standard Mask has already written its bit; this
            // keeps the VMG mask region clipped to the outer Mask. Replace
            // writes Ref & writeMask = `bit`, so ancestor bits stay intact.
            var mat = StencilMaterial.Add(
                baseMaterial,
                refValue,
                StencilOp.Replace,
                CompareFunction.Equal,
                ShowSource ? ColorWriteMask.All : 0,
                readMask,
                writeMask);

            if (m_MaskMaterial != null) StencilMaterial.Remove(m_MaskMaterial);
            m_MaskMaterial = mat;
            return mat;
        }
    }
}
