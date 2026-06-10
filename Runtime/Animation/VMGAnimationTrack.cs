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
    }
}
