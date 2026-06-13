using System;
using System.Collections.Generic;

namespace VMG.Animation
{
    [Serializable]
    public class VMGAnimationTrack
    {
        public VMGChannelBinding binding;
        public VMGChannelType type;
        public List<VMGAnimationKey> keys = new List<VMGAnimationKey>();
        // 0 = no user group (track sits at the top level under the
        // auto-derived GO+component grouping). Non-zero references
        // VMGAnimationClip.userGroups[*].id.
        public int groupId;
    }
}
