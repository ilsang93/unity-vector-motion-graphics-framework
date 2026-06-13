using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    internal static class VMGTrackKeyEditor
    {
        public static void Draw(VMGAnimator animator, VMGTimelineSelection selection)
        {
            var clip = animator.clip;
            if (clip == null) return;

            if (selection.HasEventSelection)
            {
                DrawEventPanel(clip, selection);
                return;
            }

            if (selection.trackIndex < 0 || selection.trackIndex >= clip.tracks.Count) return;

            var track = clip.tracks[selection.trackIndex];
            if (track == null) return;

            if (selection.IsMulti)
            {
                DrawMultiKeyPanel(clip, selection);
                return;
            }

            DrawTrackBinding(clip, track, selection.trackIndex, animator);

            if (selection.keyIndex < 0 || selection.keyIndex >= track.keys.Count) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Selected Key — track {selection.trackIndex}, key {selection.keyIndex}", EditorStyles.boldLabel);

            var key = track.keys[selection.keyIndex];

            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.FloatField("Time (s)", key.time);
            // Clip duration is derived from the latest key — clamping the
            // input against it would freeze the *last* key. Only floor at 0.
            newTime = Mathf.Max(0f, newTime);

            DrawValueField(track.type, ref key);

            Vector2 outT = EditorGUILayout.Vector2Field("Out Tangent", key.outTangent);
            Vector2 inT = EditorGUILayout.Vector2Field("In Tangent", key.inTangent);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clip, "Edit VMG Key");
                key.time = newTime;
                key.outTangent = outT;
                key.inTangent = inT;
                track.keys[selection.keyIndex] = key;
                SortKeysByTime(track, ref selection);
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
            }

            DrawEasingSection(clip, track, selection.keyIndex);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Delete Key"))
            {
                Undo.RecordObject(clip, "Delete VMG Key");
                track.keys.RemoveAt(selection.keyIndex);
                selection.Clear();
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
            }
            EditorGUILayout.EndHorizontal();
        }

        static void DrawEventPanel(VMGAnimationClip clip, VMGTimelineSelection selection)
        {
            int idx = selection.EventIndex;
            if (idx < 0 || idx >= clip.events.Count) return;
            var ev = clip.events[idx];

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Event {idx}", EditorStyles.boldLabel);

            // Edit time and label.
            EditorGUI.BeginChangeCheck();
            float newTime = EditorGUILayout.FloatField("Time (s)", ev.time);
            newTime = Mathf.Max(0f, newTime);
            string newLabel = EditorGUILayout.TextField("Label", ev.label ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clip, "Edit VMG Event");
                ev.time = newTime;
                ev.label = newLabel;
                // Re-sort by time and re-locate.
                clip.events.Sort((a, b) => a.time.CompareTo(b.time));
                for (int i = 0; i < clip.events.Count; i++)
                    if (clip.events[i] == ev) { selection.SelectEvent(i); break; }
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
            }

            // UnityEvent invoke via SerializedProperty for native picker UI.
            var so = new SerializedObject(clip);
            var eventsProp = so.FindProperty("events");
            if (eventsProp != null && idx < eventsProp.arraySize)
            {
                var eventProp = eventsProp.GetArrayElementAtIndex(idx);
                var invokeProp = eventProp != null ? eventProp.FindPropertyRelative("invoke") : null;
                if (invokeProp != null)
                {
                    so.Update();
                    EditorGUILayout.PropertyField(invokeProp, new GUIContent("Invoke"), includeChildren: true);
                    so.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Delete Event"))
            {
                Undo.RecordObject(clip, "Delete VMG Event");
                clip.events.RemoveAt(idx);
                selection.ClearEvent();
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
            }
        }

        static void DrawMultiKeyPanel(VMGAnimationClip clip, VMGTimelineSelection selection)
        {
            // Collect valid items + count distinct tracks + check uniform channel type.
            var items = new List<(int track, int key)>();
            var distinctTracks = new HashSet<int>();
            bool hasType = false;
            VMGChannelType commonType = default;
            bool mixedType = false;

            foreach (var it in selection.Items)
            {
                if (it.track < 0 || it.track >= clip.tracks.Count) continue;
                var tr = clip.tracks[it.track];
                if (tr == null || it.key < 0 || it.key >= tr.keys.Count) continue;
                items.Add((it.track, it.key));
                distinctTracks.Add(it.track);
                if (!hasType) { commonType = tr.type; hasType = true; }
                else if (tr.type != commonType) mixedType = true;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{items.Count} keys selected across {distinctTracks.Count} track(s)", EditorStyles.boldLabel);
            if (items.Count == 0) return;

            // ---- Value field (only when uniform type) ----
            if (mixedType)
            {
                EditorGUILayout.HelpBox("Selection contains mixed channel types — value edit disabled.", MessageType.None);
            }
            else
            {
                DrawMultiValueField(clip, items, commonType);
            }

            // ---- Tangent fields (always available; uniform display if all match) ----
            DrawMultiTangentField(clip, items, "Out Tangent",
                getter: k => k.outTangent,
                setter: (ref VMGAnimationKey k, Vector2 v) => k.outTangent = v);
            DrawMultiTangentField(clip, items, "In Tangent",
                getter: k => k.inTangent,
                setter: (ref VMGAnimationKey k, Vector2 v) => k.inTangent = v);

            // ---- Easing preset + bezier (applies outTangent + next key's inTangent for each) ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Easing", EditorStyles.boldLabel);
            var presetMulti = (VMGEasingPreset)EditorGUILayout.EnumPopup("Easing Preset", VMGEasingPreset.Custom);
            if (presetMulti != VMGEasingPreset.Custom)
            {
                Undo.RecordObject(clip, "Apply VMG Easing Preset");
                VMGEasingPresets.GetTangents(presetMulti, out var outT, out var inT);
                foreach (var (ti, ki) in items)
                {
                    var tr = clip.tracks[ti];
                    var k = tr.keys[ki];
                    k.outTangent = outT;
                    tr.keys[ki] = k;
                    int nextIdx = ki + 1;
                    if (nextIdx < tr.keys.Count)
                    {
                        var nk = tr.keys[nextIdx];
                        nk.inTangent = inT;
                        tr.keys[nextIdx] = nk;
                    }
                }
                VMGTimelineSelection.MarkDirty(clip);
            }

            // Mini preview using the first item's out + (next-of-first)'s in
            // as a template — same convention as the single-key panel.
            var (ti0, ki0) = items[0];
            var firstTrack = clip.tracks[ti0];
            var firstKey = firstTrack.keys[ki0];
            Vector2 templateOut = firstKey.outTangent;
            Vector2 templateIn = ki0 + 1 < firstTrack.keys.Count
                ? firstTrack.keys[ki0 + 1].inTangent
                : new Vector2(1f / 3f, 1f / 3f);

            EditorGUILayout.BeginHorizontal();
            var previewRect = GUILayoutUtility.GetRect(80f, 60f, GUILayout.Width(80f), GUILayout.Height(60f));
            DrawBezierPreview(previewRect, templateOut, templateIn);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Edit Curve…", GUILayout.Width(120f)))
            {
                VMGEasingEditWindow.ShowMulti(clip, items, templateOut, templateIn);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button($"Delete {items.Count} Keys"))
            {
                Undo.RecordObject(clip, "Delete VMG Keys");
                // Remove from highest index downward per track to keep indices valid.
                var byTrack = new Dictionary<int, List<int>>();
                foreach (var (ti, ki) in items)
                {
                    if (!byTrack.TryGetValue(ti, out var list)) { list = new List<int>(); byTrack[ti] = list; }
                    list.Add(ki);
                }
                foreach (var pair in byTrack)
                {
                    pair.Value.Sort((a, b) => b.CompareTo(a));
                    var tr = clip.tracks[pair.Key];
                    foreach (var ki in pair.Value)
                        if (ki >= 0 && ki < tr.keys.Count) tr.keys.RemoveAt(ki);
                }
                selection.Clear();
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
            }
        }

        static void DrawMultiValueField(VMGAnimationClip clip, List<(int track, int key)> items, VMGChannelType type)
        {
            // Detect mixed values.
            var firstKey = clip.tracks[items[0].track].keys[items[0].key];
            bool mixed = false;
            foreach (var (ti, ki) in items)
            {
                var k = clip.tracks[ti].keys[ki];
                if (!SameValue(k, firstKey, type)) { mixed = true; break; }
            }

            EditorGUI.BeginChangeCheck();
            var dummy = firstKey;
            EditorGUI.showMixedValue = mixed;
            DrawValueField(type, ref dummy);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clip, "Edit VMG Keys (value)");
                foreach (var (ti, ki) in items)
                {
                    var k = clip.tracks[ti].keys[ki];
                    CopyValueByType(ref k, dummy, type);
                    clip.tracks[ti].keys[ki] = k;
                }
                VMGTimelineSelection.MarkDirty(clip);
            }
        }

        delegate Vector2 KeyVecGetter(VMGAnimationKey k);
        delegate void KeyVecSetter(ref VMGAnimationKey k, Vector2 v);

        static void DrawMultiTangentField(VMGAnimationClip clip, List<(int track, int key)> items, string label, KeyVecGetter getter, KeyVecSetter setter)
        {
            var first = getter(clip.tracks[items[0].track].keys[items[0].key]);
            bool mixed = false;
            foreach (var (ti, ki) in items)
            {
                if (getter(clip.tracks[ti].keys[ki]) != first) { mixed = true; break; }
            }
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = mixed;
            Vector2 newVal = EditorGUILayout.Vector2Field(label, first);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clip, $"Edit VMG Keys ({label})");
                foreach (var (ti, ki) in items)
                {
                    var k = clip.tracks[ti].keys[ki];
                    setter(ref k, newVal);
                    clip.tracks[ti].keys[ki] = k;
                }
                VMGTimelineSelection.MarkDirty(clip);
            }
        }

        static bool SameValue(VMGAnimationKey a, VMGAnimationKey b, VMGChannelType type)
        {
            switch (type)
            {
                case VMGChannelType.Float: return a.floatValue == b.floatValue;
                case VMGChannelType.Int: return a.intValue == b.intValue;
                case VMGChannelType.Bool: return a.boolValue == b.boolValue;
                case VMGChannelType.Color: return a.colorValue == b.colorValue;
                case VMGChannelType.Vector2:
                case VMGChannelType.Vector3:
                case VMGChannelType.Vector4: return a.vectorValue == b.vectorValue;
            }
            return true;
        }

        static void CopyValueByType(ref VMGAnimationKey target, VMGAnimationKey src, VMGChannelType type)
        {
            switch (type)
            {
                case VMGChannelType.Float: target.floatValue = src.floatValue; break;
                case VMGChannelType.Int: target.intValue = src.intValue; break;
                case VMGChannelType.Bool: target.boolValue = src.boolValue; break;
                case VMGChannelType.Color: target.colorValue = src.colorValue; break;
                case VMGChannelType.Vector2:
                case VMGChannelType.Vector3:
                case VMGChannelType.Vector4: target.vectorValue = src.vectorValue; break;
            }
        }

        static void DrawEasingSection(VMGAnimationClip clip, VMGAnimationTrack track, int keyIndex)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Easing (this key → next key)", EditorStyles.boldLabel);

            bool hasNext = keyIndex + 1 < track.keys.Count;
            if (!hasNext)
            {
                EditorGUILayout.HelpBox("Last key — no following segment to ease into.", MessageType.None);
                return;
            }

            var preset = (VMGEasingPreset)EditorGUILayout.EnumPopup("Preset", VMGEasingPreset.Custom);
            if (preset != VMGEasingPreset.Custom)
            {
                Undo.RecordObject(clip, "Apply VMG Easing Preset");
                VMGEasingPresets.GetTangents(preset, out var outT, out var inT);
                var k = track.keys[keyIndex];
                k.outTangent = outT;
                track.keys[keyIndex] = k;
                var next = track.keys[keyIndex + 1];
                next.inTangent = inT;
                track.keys[keyIndex + 1] = next;
                VMGTimelineSelection.MarkDirty(clip);
            }

            EditorGUILayout.BeginHorizontal();
            var key = track.keys[keyIndex];
            var nextKey = track.keys[keyIndex + 1];
            var previewRect = GUILayoutUtility.GetRect(80f, 60f, GUILayout.Width(80f), GUILayout.Height(60f));
            DrawBezierPreview(previewRect, key.outTangent, nextKey.inTangent);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Edit Curve…", GUILayout.Width(120f)))
            {
                VMGEasingEditWindow.Show(clip, track, keyIndex);
            }
            EditorGUILayout.EndHorizontal();
        }

        static void DrawBezierPreview(Rect rect, Vector2 outT, Vector2 inT)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
            var border = new Color(0f, 0f, 0f, 0.6f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);

            const int samples = 32;
            Handles.color = new Color(0.4f, 0.85f, 1f, 1f);
            Vector3 prev = default;
            for (int i = 0; i <= samples; i++)
            {
                float u = i / (float)samples;
                float v = VMGEasing.Evaluate(outT, inT, u);
                var p = new Vector3(rect.x + u * rect.width, rect.yMax - v * rect.height, 0f);
                if (i > 0) Handles.DrawLine(prev, p);
                prev = p;
            }

            // Control point dots (both P1=outTangent and P2=inTangent in absolute [0,1] space)
            Handles.color = new Color(1f, 0.85f, 0.3f, 1f);
            Handles.DrawSolidDisc(new Vector3(rect.x + outT.x * rect.width, rect.yMax - outT.y * rect.height, 0f), Vector3.forward, 2.5f);
            Handles.DrawSolidDisc(new Vector3(rect.x + inT.x * rect.width, rect.yMax - inT.y * rect.height, 0f), Vector3.forward, 2.5f);
        }

        static void DrawTrackBinding(VMGAnimationClip clip, VMGAnimationTrack track, int trackIndex, VMGAnimator animator)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Track {trackIndex} Binding", EditorStyles.boldLabel);

            string go = string.IsNullOrEmpty(track.binding.gameObjectPath) ? "self" : track.binding.gameObjectPath;
            string comp = string.IsNullOrEmpty(track.binding.componentTypeName) ? "<not bound>" : ShortTypeName(track.binding.componentTypeName);
            string field = string.IsNullOrEmpty(track.binding.fieldPath) ? "<not bound>" : track.binding.fieldPath;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("GameObject", go);
                EditorGUILayout.LabelField("Component", comp);
                EditorGUILayout.LabelField("Field", field);
                EditorGUILayout.LabelField("Channel Type", track.type.ToString());
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Re-bind…", GUILayout.Width(120f)))
            {
                VMGChannelPickerWindow.Show(animator.transform, picked =>
                {
                    Undo.RecordObject(clip, "Re-bind VMG Track");
                    track.binding = new VMGChannelBinding
                    {
                        gameObjectPath = picked.gameObjectPath,
                        componentTypeName = picked.componentTypeName,
                        fieldPath = picked.fieldPath,
                    };
                    track.type = picked.channelType;
                    VMGTimelineSelection.MarkDirty(clip);
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        static string ShortTypeName(string assemblyQualified)
        {
            if (string.IsNullOrEmpty(assemblyQualified)) return string.Empty;
            int comma = assemblyQualified.IndexOf(',');
            string typeOnly = comma > 0 ? assemblyQualified.Substring(0, comma) : assemblyQualified;
            int dot = typeOnly.LastIndexOf('.');
            return dot > 0 ? typeOnly.Substring(dot + 1) : typeOnly;
        }

        static void DrawValueField(VMGChannelType type, ref VMGAnimationKey key)
        {
            switch (type)
            {
                case VMGChannelType.Float:
                    key.floatValue = EditorGUILayout.FloatField("Value", key.floatValue);
                    break;
                case VMGChannelType.Int:
                    key.intValue = EditorGUILayout.IntField("Value", key.intValue);
                    break;
                case VMGChannelType.Bool:
                    key.boolValue = EditorGUILayout.Toggle("Value", key.boolValue);
                    break;
                case VMGChannelType.Color:
                    key.colorValue = EditorGUILayout.ColorField("Value", key.colorValue);
                    break;
                case VMGChannelType.Vector2:
                    Vector2 v2 = EditorGUILayout.Vector2Field("Value", key.vectorValue);
                    key.vectorValue = new Vector4(v2.x, v2.y, 0f, 0f);
                    break;
                case VMGChannelType.Vector3:
                    Vector3 v3 = EditorGUILayout.Vector3Field("Value", key.vectorValue);
                    key.vectorValue = new Vector4(v3.x, v3.y, v3.z, 0f);
                    break;
                case VMGChannelType.Vector4:
                    key.vectorValue = EditorGUILayout.Vector4Field("Value", key.vectorValue);
                    break;
            }
        }

        static void SortKeysByTime(VMGAnimationTrack track, ref VMGTimelineSelection selection)
        {
            int trackIdx = selection.trackIndex;
            int idx = selection.keyIndex;
            if (idx < 0 || idx >= track.keys.Count) return;
            var pivot = track.keys[idx];
            track.keys.Sort((a, b) => a.time.CompareTo(b.time));
            for (int i = 0; i < track.keys.Count; i++)
            {
                var k = track.keys[i];
                if (k.time == pivot.time
                    && k.floatValue == pivot.floatValue
                    && k.intValue == pivot.intValue
                    && k.boolValue == pivot.boolValue
                    && k.colorValue == pivot.colorValue
                    && k.vectorValue == pivot.vectorValue)
                {
                    selection.Select(trackIdx, i);
                    return;
                }
            }
        }
    }
}
