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
    }
}
