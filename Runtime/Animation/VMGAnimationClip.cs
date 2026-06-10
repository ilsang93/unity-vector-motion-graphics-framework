using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation
{
    [CreateAssetMenu(menuName = "VMG/Animation Clip", fileName = "VMGAnimationClip")]
    public class VMGAnimationClip : ScriptableObject
    {
        [Min(0f)]
        public float duration = 1f;

        public bool loop;

        [Tooltip("When on, duration follows the latest key/event time automatically. Turn off to set duration manually.")]
        public bool autoFitDuration = true;

        public List<VMGAnimationTrack> tracks = new List<VMGAnimationTrack>();

        public List<VMGAnimationEvent> events = new List<VMGAnimationEvent>();

        public VMGHierarchySnapshot hierarchy = new VMGHierarchySnapshot();

        public const float MinAutoDuration = 1f;

        public void RecalculateDurationIfAuto()
        {
            if (!autoFitDuration) return;
            float maxT = 0f;
            if (tracks != null)
            {
                foreach (var track in tracks)
                {
                    if (track == null || track.keys == null) continue;
                    foreach (var k in track.keys) if (k.time > maxT) maxT = k.time;
                }
            }
            if (events != null)
            {
                foreach (var ev in events) if (ev != null && ev.time > maxT) maxT = ev.time;
            }
            duration = Mathf.Max(MinAutoDuration, maxT);
        }
    }
}
