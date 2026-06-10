using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    public class VMGEasingEditWindow : EditorWindow
    {
        VMGAnimationClip m_Clip;
        VMGAnimationTrack m_Track;
        int m_KeyIndex;
        int m_Dragging = -1;

        // Multi mode
        bool m_MultiMode;
        List<(int track, int key)> m_MultiItems;
        Vector2 m_TemplateOut;
        Vector2 m_TemplateIn;

        const float k_PadX = 30f;
        const float k_PadY = 30f;
        const float k_HandleHit = 10f;

        internal static void Show(VMGAnimationClip clip, VMGAnimationTrack track, int keyIndex)
        {
            var w = CreateInstance<VMGEasingEditWindow>();
            w.titleContent = new GUIContent("VMG Easing");
            w.m_Clip = clip;
            w.m_Track = track;
            w.m_KeyIndex = keyIndex;
            w.m_MultiMode = false;
            w.minSize = new Vector2(280f, 320f);
            w.maxSize = new Vector2(280f, 380f);
            w.ShowUtility();
        }

        internal static void ShowMulti(VMGAnimationClip clip, List<(int track, int key)> items, Vector2 initialOut, Vector2 initialIn)
        {
            var w = CreateInstance<VMGEasingEditWindow>();
            w.titleContent = new GUIContent("VMG Easing (Multi)");
            w.m_Clip = clip;
            w.m_MultiMode = true;
            w.m_MultiItems = new List<(int, int)>(items);
            w.m_TemplateOut = initialOut;
            w.m_TemplateIn = initialIn;
            w.minSize = new Vector2(280f, 360f);
            w.maxSize = new Vector2(280f, 420f);
            w.ShowUtility();
        }

        void OnGUI()
        {
            if (m_Clip == null)
            {
                EditorGUILayout.HelpBox("Invalid selection.", MessageType.Warning);
                return;
            }
            if (m_MultiMode) DrawMulti();
            else DrawSingle();
        }

        void DrawSingle()
        {
            if (m_Track == null || m_KeyIndex < 0 || m_KeyIndex >= m_Track.keys.Count)
            {
                EditorGUILayout.HelpBox("Invalid key selection.", MessageType.Warning);
                return;
            }
            if (m_KeyIndex + 1 >= m_Track.keys.Count)
            {
                EditorGUILayout.HelpBox("Last key has no following segment to ease into.", MessageType.Info);
                return;
            }

            var key = m_Track.keys[m_KeyIndex];
            var next = m_Track.keys[m_KeyIndex + 1];

            EditorGUILayout.LabelField($"Key {m_KeyIndex} → Key {m_KeyIndex + 1}", EditorStyles.boldLabel);
            var graphRect = ReserveGraphRect();
            DrawGraph(graphRect, key.outTangent, next.inTangent);

            Vector2 newOut = key.outTangent;
            Vector2 newIn = next.inTangent;
            HandleGraphInput(graphRect, ref newOut, ref newIn);
            if (newOut != key.outTangent || newIn != next.inTangent)
            {
                Undo.RecordObject(m_Clip, "Edit VMG Easing");
                key.outTangent = newOut;
                next.inTangent = newIn;
                m_Track.keys[m_KeyIndex] = key;
                m_Track.keys[m_KeyIndex + 1] = next;
                VMGTimelineSelection.MarkDirty(m_Clip);
                Repaint();
            }

            DrawPresetRow(ref newOut, ref newIn, applyToTarget: () =>
            {
                Undo.RecordObject(m_Clip, "Apply VMG Easing Preset");
                var k = m_Track.keys[m_KeyIndex];
                k.outTangent = newOut;
                m_Track.keys[m_KeyIndex] = k;
                var n = m_Track.keys[m_KeyIndex + 1];
                n.inTangent = newIn;
                m_Track.keys[m_KeyIndex + 1] = n;
                VMGTimelineSelection.MarkDirty(m_Clip);
                Repaint();
            });
        }

        void DrawMulti()
        {
            int count = m_MultiItems != null ? m_MultiItems.Count : 0;
            EditorGUILayout.LabelField($"Editing easing for {count} keys", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Edit a template curve, then Apply to overwrite the out-tangent of every selected key and the in-tangent of each key's following neighbour.", MessageType.None);

            var graphRect = ReserveGraphRect();
            DrawGraph(graphRect, m_TemplateOut, m_TemplateIn);
            HandleGraphInput(graphRect, ref m_TemplateOut, ref m_TemplateIn);

            DrawPresetRow(ref m_TemplateOut, ref m_TemplateIn, applyToTarget: null);

            EditorGUILayout.Space();
            if (GUILayout.Button($"Apply to {count} keys"))
            {
                ApplyMulti();
            }
        }

        void ApplyMulti()
        {
            if (m_Clip == null || m_MultiItems == null) return;
            Undo.RecordObject(m_Clip, "Apply VMG Easing (Multi)");
            foreach (var (ti, ki) in m_MultiItems)
            {
                if (ti < 0 || ti >= m_Clip.tracks.Count) continue;
                var tr = m_Clip.tracks[ti];
                if (tr == null || ki < 0 || ki >= tr.keys.Count) continue;
                var k = tr.keys[ki];
                k.outTangent = m_TemplateOut;
                tr.keys[ki] = k;
                int nextIdx = ki + 1;
                if (nextIdx < tr.keys.Count)
                {
                    var nk = tr.keys[nextIdx];
                    nk.inTangent = m_TemplateIn;
                    tr.keys[nextIdx] = nk;
                }
            }
            VMGTimelineSelection.MarkDirty(m_Clip);
            Repaint();
        }

        Rect ReserveGraphRect()
        {
            var graphRect = GUILayoutUtility.GetRect(0f, 220f, GUILayout.ExpandWidth(true), GUILayout.Height(220f));
            return new Rect(graphRect.x + k_PadX, graphRect.y + k_PadY * 0.5f, graphRect.width - k_PadX * 2f, graphRect.height - k_PadY);
        }

        void DrawPresetRow(ref Vector2 outT, ref Vector2 inT, System.Action applyToTarget)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Preset:", GUILayout.Width(50f));
            var preset = (VMGEasingPreset)EditorGUILayout.EnumPopup(VMGEasingPreset.Custom);
            if (preset != VMGEasingPreset.Custom)
            {
                VMGEasingPresets.GetTangents(preset, out var oT, out var iT);
                outT = oT;
                inT = iT;
                applyToTarget?.Invoke();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        static void DrawGraph(Rect rect, Vector2 p1, Vector2 p2)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 1f));
            var border = new Color(0f, 0f, 0f, 0.6f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);

            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            Handles.DrawLine(new Vector3(rect.x, rect.yMax, 0f), new Vector3(rect.xMax, rect.y, 0f));

            const int samples = 64;
            Handles.color = new Color(0.4f, 0.85f, 1f, 1f);
            Vector3 prev = default;
            for (int i = 0; i <= samples; i++)
            {
                float u = i / (float)samples;
                float v = VMGEasing.Evaluate(p1, p2, u);
                var pt = new Vector3(rect.x + u * rect.width, rect.yMax - v * rect.height, 0f);
                if (i > 0) Handles.DrawLine(prev, pt);
                prev = pt;
            }

            Handles.color = new Color(1f, 1f, 1f, 0.4f);
            var p0Pix = new Vector3(rect.x, rect.yMax, 0f);
            var p1Pix = new Vector3(rect.x + p1.x * rect.width, rect.yMax - p1.y * rect.height, 0f);
            var p2Pix = new Vector3(rect.x + p2.x * rect.width, rect.yMax - p2.y * rect.height, 0f);
            var p3Pix = new Vector3(rect.xMax, rect.y, 0f);
            Handles.DrawLine(p0Pix, p1Pix);
            Handles.DrawLine(p3Pix, p2Pix);

            Handles.color = new Color(1f, 0.85f, 0.3f, 1f);
            Handles.DrawSolidDisc(p1Pix, Vector3.forward, 5f);
            Handles.DrawSolidDisc(p2Pix, Vector3.forward, 5f);
        }

        void HandleGraphInput(Rect rect, ref Vector2 outT, ref Vector2 inT)
        {
            var e = Event.current;
            var p1Pix = new Vector2(rect.x + outT.x * rect.width, rect.yMax - outT.y * rect.height);
            var p2Pix = new Vector2(rect.x + inT.x * rect.width, rect.yMax - inT.y * rect.height);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0) return;
                    if (Vector2.Distance(e.mousePosition, p1Pix) <= k_HandleHit) m_Dragging = 1;
                    else if (Vector2.Distance(e.mousePosition, p2Pix) <= k_HandleHit) m_Dragging = 2;
                    if (m_Dragging > 0) { e.Use(); GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); }
                    break;
                case EventType.MouseDrag:
                    if (m_Dragging <= 0) return;
                    float nx = Mathf.Clamp01((e.mousePosition.x - rect.x) / rect.width);
                    float ny = Mathf.Clamp01((rect.yMax - e.mousePosition.y) / rect.height);
                    if (m_Dragging == 1) outT = new Vector2(nx, ny);
                    else inT = new Vector2(nx, ny);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (m_Dragging > 0)
                    {
                        m_Dragging = 0;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }
    }
}
