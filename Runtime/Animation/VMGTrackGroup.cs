using System;

namespace VMG.Animation
{
    // User-defined composition group. Tracks reference a group by `id`
    // (0 = unassigned). Groups are purely an editor / authoring concept
    // — runtime sampling never reads them. Same-name groups carry
    // distinct ids so duplicate names from DSL imports stay separate.
    [Serializable]
    public class VMGTrackGroup
    {
        public int id;
        public string name;
    }
}
