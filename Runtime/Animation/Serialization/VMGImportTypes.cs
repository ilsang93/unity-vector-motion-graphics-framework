using System.Collections.Generic;

namespace VMG.Animation.Serialization
{
    public enum VMGHierarchyMode
    {
        Auto = 0,
        ForceRecreate = 1,
        SkipRecreate = 2,
    }

    public struct VMGImportOptions
    {
        public VMGHierarchyMode hierarchyMode;

        public static VMGImportOptions Default => default;
    }

    public class VMGImportResult
    {
        public VMGAnimationClip clip;
        public List<string> warnings = new List<string>();
        public List<string> missingShapeAssets = new List<string>();
        public bool hierarchyRecreated;
    }
}
