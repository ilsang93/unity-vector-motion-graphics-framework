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
    [AddComponentMenu("VMG/Masking/Mask Group", 0)]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class VMGMaskGroup : MonoBehaviour
    {
        // Upper-nibble bits. Tried in this order so the topmost group
        // grabs 128 first (matches the bit count the user actually
        // perceives as "slot 1 / 2 / 3 / 4").
        static readonly int[] kBits = { 128, 64, 32, 16 };
        static int s_AllocatedMask;

        [Tooltip("When true the mask shows the OUTSIDE of the source region: clients render everywhere INSIDE the parent region EXCEPT where a source stamped. Mirrors flipping a standard Mask's keep-inside to keep-outside. Default false = show inside the sources.")]
        public bool Invert = false;

        [System.NonSerialized] int m_StencilBit;
        [System.NonSerialized] int m_ParentBits;

        /// 0 while the group is inactive. Otherwise one of {128, 64, 32, 16}.
        public int StencilId => m_StencilBit;

        /// All bits owned by ancestor regions that this group nests inside:
        /// the lower-nibble bits of ancestor standard Masks PLUS the upper
        /// bits of any ancestor VMGMaskGroup. Source/Client combine this with
        /// StencilId so VMG masking only takes effect inside every enclosing
        /// region (standard Mask ∩ parent VMG group ∩ this group).
        public int ParentBits => m_ParentBits;

        void OnEnable()
        {
            RecomputeParentBits();
            if (m_StencilBit == 0)
            {
                m_StencilBit = AllocateBit(m_ParentBits);
                if (m_StencilBit == 0)
                {
                    Debug.LogError($"[VMGMaskGroup] '{name}': no free stencil bit (parentBits=0x{m_ParentBits:X}, allocated mask=0x{s_AllocatedMask:X}); mask disabled. Nesting is limited to 4 VMG mask groups plus ancestor standard Masks.", this);
                }
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

#if UNITY_EDITOR
        // Invert flips the client compare expression; like ShowSource on the
        // source, the change is inert until the client graphics are marked
        // material-dirty.
        void OnValidate()
        {
            if (isActiveAndEnabled) NotifyChildren();
        }
#endif

        /// Recompute the ancestor bit mask (standard Masks + ancestor VMG
        /// groups). Does NOT reallocate this group's own bit. Callable to
        /// repair stale parentBits after a reparent, and used by a parent
        /// group to push a recompute down to nested groups once the parent
        /// has claimed its own bit.
        internal void RecomputeParentBits()
        {
            var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
            int parentDepth = MaskUtilities.GetStencilDepth(transform, rootCanvas);
            if (parentDepth >= 8)
            {
                Debug.LogError($"[VMGMaskGroup] '{name}': ancestor stencil depth >= 8 — no bits left for VMG mask.", this);
                m_ParentBits = 0;
                return;
            }

            int bits = (1 << parentDepth) - 1;

            // Fold in the nearest ENABLED ancestor VMG group's full mask. Each
            // group already folds its own ancestors into ParentBits, so the
            // nearest ancestor's (StencilId | ParentBits) transitively carries
            // the entire VMG-group chain above us. This is what makes a nested
            // group's region the INTERSECTION of every enclosing group.
            var parent = transform.parent;
            if (parent != null)
            {
                var ancestor = parent.GetComponentInParent<VMGMaskGroup>(true);
                if (ancestor != null && ancestor != this && ancestor.isActiveAndEnabled)
                    bits |= ancestor.StencilId | ancestor.ParentBits;
            }

            m_ParentBits = bits;
        }

        void NotifyChildren()
        {
            // Nested VMG groups may have computed their parentBits before this
            // group had its bit. Recompute them first so the chain is consistent
            // regardless of component enable order. RecomputeParentBits only
            // reads ancestor state (no graphic dirtying, no recursion into
            // NotifyChildren), so this is a flat O(n) pass.
            var nested = GetComponentsInChildren<VMGMaskGroup>(true);
            for (int i = 0; i < nested.Length; i++)
            {
                if (nested[i] == null || nested[i] == this) continue;
                nested[i].RecomputeParentBits();
            }

            // Then dirty every graphic under this group (includes those owned
            // by nested groups) so all source/client materials rebuild against
            // the now-consistent bit chain.
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
