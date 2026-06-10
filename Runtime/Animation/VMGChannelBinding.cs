using System;

namespace VMG.Animation
{
    [Serializable]
    public struct VMGChannelBinding
    {
        public string gameObjectPath;
        public string componentTypeName;
        public string fieldPath;
    }
}
