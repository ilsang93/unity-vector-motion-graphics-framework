using System.Collections.Generic;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    internal static class VMGKeyClipboard
    {
        public struct Entry
        {
            public VMGChannelBinding binding;
            public VMGChannelType type;
            public float relativeTime;
            public int relativeTrack;       // offset from the topmost selected track
            public VMGAnimationKey key;
        }

        static readonly List<Entry> s_Entries = new List<Entry>();

        public static int Count => s_Entries.Count;
        public static IReadOnlyList<Entry> Entries => s_Entries;
        public static bool HasContent => s_Entries.Count > 0;

        public static void Copy(VMGAnimationClip clip, IReadOnlyList<VMGTimelineSelection.Item> items)
        {
            s_Entries.Clear();
            if (clip == null || items == null || items.Count == 0) return;

            float minTime = float.MaxValue;
            int minTrack = int.MaxValue;
            foreach (var it in items)
            {
                if (it.track < 0 || it.track >= clip.tracks.Count) continue;
                var tr = clip.tracks[it.track];
                if (tr == null || it.key < 0 || it.key >= tr.keys.Count) continue;
                float t = tr.keys[it.key].time;
                if (t < minTime) minTime = t;
                if (it.track < minTrack) minTrack = it.track;
            }
            if (minTime == float.MaxValue) return;

            foreach (var it in items)
            {
                if (it.track < 0 || it.track >= clip.tracks.Count) continue;
                var tr = clip.tracks[it.track];
                if (tr == null || it.key < 0 || it.key >= tr.keys.Count) continue;
                var k = tr.keys[it.key];
                s_Entries.Add(new Entry
                {
                    binding = tr.binding,
                    type = tr.type,
                    relativeTime = k.time - minTime,
                    relativeTrack = it.track - minTrack,
                    key = k,
                });
            }
        }

        public static List<VMGTimelineSelection.Item> Paste(VMGAnimationClip clip, float startTime, int preferredTrack, out List<string> warnings)
        {
            warnings = new List<string>();
            var newSelection = new List<VMGTimelineSelection.Item>();
            if (clip == null || s_Entries.Count == 0) return newSelection;

            // First pass: resolve target track per entry using priority:
            //   1. Exact binding match
            //   2. preferredTrack if its type matches
            //   3. First track with matching type
            var resolvedTargets = new int[s_Entries.Count];
            for (int i = 0; i < s_Entries.Count; i++)
                resolvedTargets[i] = ResolveTarget(clip, s_Entries[i], preferredTrack);

            for (int i = 0; i < s_Entries.Count; i++)
            {
                int targetIdx = resolvedTargets[i];
                if (targetIdx < 0)
                {
                    warnings.Add($"No matching track for type {s_Entries[i].type} (binding '{s_Entries[i].binding.fieldPath}'); skipped.");
                    continue;
                }
                var tr = clip.tracks[targetIdx];
                var k = s_Entries[i].key;
                k.time = Mathf.Max(0f, startTime + s_Entries[i].relativeTime);
                UpsertKey(tr, k);
            }

            var touched = new HashSet<int>();
            for (int i = 0; i < resolvedTargets.Length; i++)
                if (resolvedTargets[i] >= 0) touched.Add(resolvedTargets[i]);
            foreach (var ti in touched) clip.tracks[ti].keys.Sort((a, b) => a.time.CompareTo(b.time));

            // Re-find indices for selection.
            for (int i = 0; i < s_Entries.Count; i++)
            {
                int targetIdx = resolvedTargets[i];
                if (targetIdx < 0) continue;
                float t = Mathf.Max(0f, startTime + s_Entries[i].relativeTime);
                var tr = clip.tracks[targetIdx];
                for (int j = 0; j < tr.keys.Count; j++)
                {
                    if (Mathf.Approximately(tr.keys[j].time, t))
                    {
                        newSelection.Add(new VMGTimelineSelection.Item { track = targetIdx, key = j });
                        break;
                    }
                }
            }
            clip.RecalculateDuration();
            return newSelection;
        }

        static int ResolveTarget(VMGAnimationClip clip, Entry entry, int preferredTrack)
        {
            // When the caller specifies a base track (right-click on a track,
            // or current single-selection for Ctrl+V), honour it strictly:
            // place this entry at `preferredTrack + entry.relativeTrack` so
            // multi-track selections keep their relative layout.
            if (preferredTrack >= 0 && preferredTrack < clip.tracks.Count)
            {
                int target = preferredTrack + entry.relativeTrack;
                if (target < 0 || target >= clip.tracks.Count) return -1;
                var pt = clip.tracks[target];
                if (pt == null) return -1;
                if (pt.type != entry.type) return -1;
                return target;
            }
            // No preference: paste back onto the original track if its binding
            // still exists. This serves "Ctrl+V with nothing selected" =
            // "restore where the key came from".
            for (int i = 0; i < clip.tracks.Count; i++)
            {
                var tr = clip.tracks[i];
                if (tr == null) continue;
                if (tr.type != entry.type) continue;
                if (tr.binding.gameObjectPath == (entry.binding.gameObjectPath ?? string.Empty)
                    && tr.binding.componentTypeName == entry.binding.componentTypeName
                    && tr.binding.fieldPath == entry.binding.fieldPath)
                    return i;
            }
            return -1;
        }

        static int UpsertKey(VMGAnimationTrack track, VMGAnimationKey k)
        {
            for (int i = 0; i < track.keys.Count; i++)
            {
                if (Mathf.Approximately(track.keys[i].time, k.time))
                {
                    track.keys[i] = k;
                    return i;
                }
            }
            track.keys.Add(k);
            return track.keys.Count - 1;
        }
    }
}
