using UnityEngine;
using UnityEngine.UI;

namespace VMG.UI
{
    /// Marker for a GameObject that owns a stencil-based mask region whose
    /// shape is the union of one or more child VectorImageGraphics tagged
    /// with VMGMaskSource. Consumers under the same group are tagged with
    /// VMGMaskClient and only render where the union evaluates true.
    ///
    /// Stencil ID is a single BIT in the upper nibble of the 8-bit stencil
    /// buffer (128 / 64 / 32 / 16). Source/Client operations use read+write
    /// masks restricted to that bit so two overlapping mask groups can stamp
    /// the same pixel without overwriting each other.
    ///
    /// Nests inside Unity's standard Mask: at OnEnable the group queries
    /// the ambient stencil depth (count of enabled standard Masks above
    /// this transform). The parent bits (1<<depth)-1 are cached and used
    /// by Source/Client to test the standard-Mask region first — VMG
    /// stamping only happens INSIDE that region, and clients only render
    /// where both the standard Mask and the VMG group agree. The allocator
    /// also skips any upper-nibble bit that overlaps the parent depth
    /// range, so a sufficiently deep nesting (parent depth >= 7) simply
    /// reports "no slot available" instead of silently colliding.
    [AddComponentMenu("UI/VMG/Mask Group", 12)]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VMGMaskGroup : MonoBehaviour
    {
        // Upper-nibble bits. Tried in this order so the topmost group
        // grabs 128 first (matches the bit count the user actually
        // perceives as "slot 1 / 2 / 3 / 4").
        static readonly int[] kBits = { 128, 64, 32, 16 };
        static int s_AllocatedMask;

        [System.NonSerialized] int m_StencilBit;
        [System.NonSerialized] int m_ParentBits;

        /// 0 while the group is inactive. Otherwise one of {128, 64, 32, 16}.
        public int StencilId => m_StencilBit;

        /// Lower-bit mask owned by ancestor standard Masks at OnEnable time.
        /// E.g. depth=2 → 0b011. Source/Client combine this with StencilId
        /// when configuring stencil state so VMG masking only takes effect
        /// inside the ancestor Mask region.
        public int ParentBits => m_ParentBits;

        void OnEnable()
        {
            var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
            int parentDepth = MaskUtilities.GetStencilDepth(transform, rootCanvas);
            if (parentDepth >= 8)
            {
                Debug.LogError($"[VMGMaskGroup] '{name}': ancestor stencil depth >= 8 — no bits left for VMG mask.", this);
                m_ParentBits = 0;
                m_StencilBit = 0;
                NotifyChildren();
                return;
            }
            m_ParentBits = (1 << parentDepth) - 1;
            m_StencilBit = AllocateBit(m_ParentBits);
            if (m_StencilBit == 0)
            {
                Debug.LogError($"[VMGMaskGroup] '{name}': no free stencil bit (parent depth={parentDepth}, allocated mask=0x{s_AllocatedMask:X}); mask disabled.", this);
            }
            NotifyChildren();
        }

        void OnDisable()
        {
            if (m_StencilBit != 0)
            {
                ReleaseBit(m_StencilBit);
                m_StencilBit = 0;
            }
            m_ParentBits = 0;
            NotifyChildren();
        }

        void NotifyChildren()
        {
            var graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null) graphics[i].SetMaterialDirty();
            }
        }

        // Skip bits that overlap the parent's claimed range so VMG and
        // standard Mask never share a bit position.
        static int AllocateBit(int parentBits)
        {
            for (int i = 0; i < kBits.Length; i++)
            {
                int bit = kBits[i];
                if ((bit & parentBits) != 0) continue;
                if ((s_AllocatedMask & bit) == 0)
                {
                    s_AllocatedMask |= bit;
                    return bit;
                }
            }
            return 0;
        }

        static void ReleaseBit(int bit)
        {
            s_AllocatedMask &= ~bit;
        }
    }
}
