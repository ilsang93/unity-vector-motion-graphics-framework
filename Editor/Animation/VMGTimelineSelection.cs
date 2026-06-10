using System;
using System.Collections.Generic;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    internal class VMGTimelineSelection
    {
        public struct Item : IEquatable<Item>
        {
            public int track;
            public int key;
            public bool Equals(Item o) => track == o.track && key == o.key;
            public override bool Equals(object obj) => obj is Item o && Equals(o);
            public override int GetHashCode() => (track * 397) ^ key;
        }

        readonly List<Item> m_Items = new List<Item>();
        int m_EventIndex = -1;

        public IReadOnlyList<Item> Items => m_Items;
        public int Count => m_Items.Count;
        public bool HasSelection => m_Items.Count > 0;
        public bool IsMulti => m_Items.Count > 1;
        public int EventIndex => m_EventIndex;
        public bool HasEventSelection => m_EventIndex >= 0;

        // Convenience: last item — back-compat for single-key paths.
        public int trackIndex => m_Items.Count > 0 ? m_Items[m_Items.Count - 1].track : -1;
        public int keyIndex => m_Items.Count > 0 ? m_Items[m_Items.Count - 1].key : -1;

        public static event Action Changed;

        /// Broadcast for any clip data change (key/event/track add/move/edit)
        /// so the inspector and floating window stay in sync even when
        /// selection itself didn't change. Call after EditorUtility.SetDirty.
        public static event Action DataChanged;

        public static void RaiseDataChanged()
        {
            DataChanged?.Invoke();
        }

        /// Convenience: mark the clip dirty AND broadcast that views should
        /// refresh. Use this instead of raw EditorUtility.SetDirty whenever
        /// you've just mutated clip data, so the inspector + window stay
        /// in sync without explicit per-view repaint code.
        public static void MarkDirty(UnityEngine.Object obj)
        {
            if (obj != null) UnityEditor.EditorUtility.SetDirty(obj);
            DataChanged?.Invoke();
        }

        public bool Contains(int track, int key)
        {
            foreach (var it in m_Items) if (it.track == track && it.key == key) return true;
            return false;
        }

        public void Clear()
        {
            if (m_Items.Count == 0 && m_EventIndex < 0) return;
            m_Items.Clear();
            m_EventIndex = -1;
            Changed?.Invoke();
        }

        public void Select(int track, int key)
        {
            if (m_Items.Count == 1 && m_Items[0].track == track && m_Items[0].key == key && m_EventIndex < 0) return;
            m_Items.Clear();
            m_Items.Add(new Item { track = track, key = key });
            m_EventIndex = -1;
            Changed?.Invoke();
        }

        public void SelectEvent(int eventIndex)
        {
            if (m_Items.Count == 0 && m_EventIndex == eventIndex) return;
            m_Items.Clear();
            m_EventIndex = eventIndex;
            Changed?.Invoke();
        }

        public void ClearEvent()
        {
            if (m_EventIndex < 0) return;
            m_EventIndex = -1;
            Changed?.Invoke();
        }

        public void AddOrRemove(int track, int key)
        {
            int idx = -1;
            for (int i = 0; i < m_Items.Count; i++)
                if (m_Items[i].track == track && m_Items[i].key == key) { idx = i; break; }
            if (idx >= 0) m_Items.RemoveAt(idx);
            else m_Items.Add(new Item { track = track, key = key });
            Changed?.Invoke();
        }

        public void ReplaceWith(IEnumerable<Item> items)
        {
            m_Items.Clear();
            if (items != null) m_Items.AddRange(items);
            m_EventIndex = -1;
            Changed?.Invoke();
        }

        public bool TryFindFirstFor(int track, out int keyIdx)
        {
            keyIdx = -1;
            foreach (var it in m_Items)
                if (it.track == track) { keyIdx = it.key; return true; }
            return false;
        }

        static readonly Dictionary<int, VMGTimelineSelection> s_PerAnimator = new Dictionary<int, VMGTimelineSelection>();

        public static VMGTimelineSelection For(VMGAnimator animator)
        {
            if (animator == null) return new VMGTimelineSelection();
            int id = animator.GetInstanceID();
            if (!s_PerAnimator.TryGetValue(id, out var sel))
            {
                sel = new VMGTimelineSelection();
                s_PerAnimator[id] = sel;
            }
            return sel;
        }
    }
}
