using System;
using UnityEditor;
using UnityEngine;

namespace VMG.EditorTools.Animation
{
    // Minimal text-input popup used by group create / rename. EditorWindow
    // ShowUtility() is the lightest way to get a focused single-field
    // prompt without dragging in IMGUI dialog hacks.
    internal class VMGNameInputPopup : EditorWindow
    {
        string m_Title;
        string m_Label;
        string m_Value;
        Action<string> m_OnConfirm;
        bool m_FocusedOnce;

        public static void Show(string title, string label, string seed, Action<string> onConfirm)
        {
            var w = CreateInstance<VMGNameInputPopup>();
            w.titleContent = new GUIContent(title);
            w.m_Title = title;
            w.m_Label = label;
            w.m_Value = seed ?? string.Empty;
            w.m_OnConfirm = onConfirm;
            var size = new Vector2(320f, 80f);
            w.minSize = size;
            w.maxSize = size;
            // Center on the mouse.
            var pos = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            w.position = new Rect(pos.x - size.x * 0.5f, pos.y - size.y * 0.5f, size.x, size.y);
            w.ShowUtility();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField(m_Label, EditorStyles.boldLabel);

            GUI.SetNextControlName("VMGGroupNameField");
            m_Value = EditorGUILayout.TextField(m_Value);
            if (!m_FocusedOnce)
            {
                EditorGUI.FocusTextInControl("VMGGroupNameField");
                m_FocusedOnce = true;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
            {
                Close();
                return;
            }
            bool enterPressed = Event.current.type == EventType.KeyDown &&
                                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (GUILayout.Button("OK", GUILayout.Width(80f)) || enterPressed)
            {
                var cb = m_OnConfirm;
                var val = m_Value;
                Close();
                cb?.Invoke(val);
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
            }
        }
    }
}
