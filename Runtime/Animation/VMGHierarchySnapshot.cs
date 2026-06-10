using System;
using System.Collections.Generic;

namespace VMG.Animation
{
    [Serializable]
    public class VMGHierarchySnapshot
    {
        public List<VMGHierarchyNode> nodes = new List<VMGHierarchyNode>();
    }

    [Serializable]
    public class VMGHierarchyNode
    {
        public string path;
        public List<VMGComponentSnapshot> components = new List<VMGComponentSnapshot>();
    }

    [Serializable]
    public class VMGComponentSnapshot
    {
        public string componentTypeName;
        public string jsonState;
    }
}
