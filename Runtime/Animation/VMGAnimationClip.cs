using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation
{
    [CreateAssetMenu(menuName = "VMG/Animation Clip", fileName = "VMGAnimationClip")]
    public class VMGAnimationClip : ScriptableObject
    {
        // Derived: equals the latest key/event time exactly (no floor) so
        // sub-second clips are representable, or EmptyClipDuration when no
        // keys/events exist. Editor shows it read-only; the timeline
        // window's drag/zoom-out provides empty trailing space ("headroom")
        // for extending past the current end.
        [Min(0f)]
        public float duration = 1f;

        public bool loop;

        [Tooltip("Divides 1 second into N snap intervals. Used by editor drag/scrub/add-key (Shift to temporarily disable). 0 = no snap. Default 60 means keys snap to 1/60 second.")]
        [Min(0)]
        public int snapDivisor = 60;

        public List<VMGAnimationTrack> tracks = new List<VMGAnimationTrack>();

        public List<VMGAnimationEvent> events = new List<VMGAnimationEvent>();

        // User-defined composition groups. Tracks reference these by id
        // via VMGAnimationTrack.groupId. Empty groups are valid (user can
        // create the group first, then assign tracks). Ids are managed
        // via NextGroupId() so duplicate-name groups stay distinct.
        public List<VMGTrackGroup> userGroups = new List<VMGTrackGroup>();

        public VMGHierarchySnapshot hierarchy = new VMGHierarchySnapshot();

        // Default duration for empty clips. Keeps the timeline view from
        // collapsing to zero width before the first key is placed. Once any
        // key or event exists, duration tracks the latest one exactly — no
        // floor — so sub-second animations are representable.
        public const float EmptyClipDuration = 1f;

        public void RecalculateDuration()
        {
            float maxT = 0f;
            bool hasAny = false;
            if (tracks != null)
            {
                foreach (var track in tracks)
                {
                    if (track == null || track.keys == null) continue;
                    foreach (var k in track.keys)
                    {
                        hasAny = true;
                        if (k.time > maxT) maxT = k.time;
                    }
                }
            }
            if (events != null)
            {
                foreach (var ev in events)
                {
                    if (ev == null) continue;
                    hasAny = true;
                    if (ev.time > maxT) maxT = ev.time;
                }
            }
            duration = hasAny ? maxT : EmptyClipDuration;
        }

        // Allocate a fresh group id larger than any existing one. Starts
        // at 1 (0 is reserved for "no group"). Importers / editor actions
        // use this so re-imports never collide with existing groups.
        public int NextGroupId()
        {
            int max = 0;
            if (userGroups != null)
            {
                foreach (var g in userGroups)
                {
                    if (g != null && g.id > max) max = g.id;
                }
            }
            return max + 1;
        }
    }
}
