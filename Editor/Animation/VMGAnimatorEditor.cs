using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    [CustomEditor(typeof(VMGAnimator))]
    public class VMGAnimatorEditor : Editor
    {
        SerializedProperty m_Clip;
        SerializedProperty m_Mode;
        SerializedProperty m_Progress;
        SerializedProperty m_Speed;
        SerializedProperty m_FireEventsInExternalMode;

        static readonly GUIContent k_ClipLabel = new GUIContent("Clip");
        static readonly GUIContent k_ModeLabel = new GUIContent("Mode");
        static readonly GUIContent k_SpeedLabel = new GUIContent("Speed");
        static readonly GUIContent k_ProgressLabel = new GUIContent("Progress");
        static readonly GUIContent k_FireEventsLabel = new GUIContent("Fire Events In External Mode");
        static readonly GUIContent k_IsReadyLabel = new GUIContent("Is Ready");
        static readonly GUIContent k_IsPlayingLabel = new GUIContent("Is Playing");

        VMGTimelineView m_Timeline;
        VMGEditorPlayback m_Playback;

        void OnEnable()
        {
            m_Clip = serializedObject.FindProperty(nameof(VMGAnimator.clip));
            m_Mode = serializedObject.FindProperty(nameof(VMGAnimator.mode));
            m_Progress = serializedObject.FindProperty(nameof(VMGAnimator.progress));
            m_Speed = serializedObject.FindProperty(nameof(VMGAnimator.speed));
            m_FireEventsInExternalMode = serializedObject.FindProperty(nameof(VMGAnimator.fireEventsInExternalMode));
            m_Timeline = new VMGTimelineView();
            m_Playback = new VMGEditorPlayback();
            m_Playback.Bind((VMGAnimator)target);
            var rec = VMGEditorRecord.For((VMGAnimator)target);
            if (rec != null) m_Playback.Record = rec;
            VMGTimelineSelection.Changed += OnSelectionChanged;
            VMGTimelineSelection.DataChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            VMGTimelineSelection.Changed -= OnSelectionChanged;
            VMGTimelineSelection.DataChanged -= OnSelectionChanged;
            if (m_Playback != null) m_Playback.Unbind();
        }

        void OnSelectionChanged()
        {
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var animator = (VMGAnimator)target;

            DrawClipSection();
            EditorGUILayout.Space();
            DrawPlaybackSection();
            EditorGUILayout.Space();
            DrawStatusSection(animator);
            EditorGUILayout.Space();
            DrawTimelineSection(animator);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawClipSection()
        {
            EditorGUILayout.LabelField("Clip", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Clip, k_ClipLabel);

            var clip = ((VMGAnimator)target).clip;
            if (clip != null)
            {
                var clipSo = new SerializedObject(clip);
                var durationProp = clipSo.FindProperty("duration");
                var loopProp = clipSo.FindProperty("loop");
                var autoFitProp = clipSo.FindProperty("autoFitDuration");
                clipSo.Update();
                EditorGUILayout.PropertyField(autoFitProp, new GUIContent("Auto-fit Duration"));
                using (new EditorGUI.DisabledScope(autoFitProp.boolValue))
                {
                    EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (s)"));
                }
                EditorGUILayout.PropertyField(loopProp, new GUIContent("Loop"));
                clipSo.ApplyModifiedProperties();
            }
        }

        void DrawPlaybackSection()
        {
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Mode, k_ModeLabel);

            var mode = (VMGPlayMode)m_Mode.enumValueIndex;
            using (new EditorGUI.DisabledScope(mode != VMGPlayMode.Internal))
            {
                m_Speed.floatValue = EditorGUILayout.FloatField(k_SpeedLabel, m_Speed.floatValue);
            }
            m_Progress.floatValue = EditorGUILayout.Slider(k_ProgressLabel, m_Progress.floatValue, 0f, 1f);

            using (new EditorGUI.DisabledScope(mode != VMGPlayMode.External))
            {
                m_FireEventsInExternalMode.boolValue = EditorGUILayout.Toggle(k_FireEventsLabel, m_FireEventsInExternalMode.boolValue);
            }
        }

        void DrawStatusSection(VMGAnimator animator)
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(k_IsReadyLabel, animator.IsReady);
                EditorGUILayout.Toggle(k_IsPlayingLabel, animator.IsPlaying);
            }
        }

        void DrawTimelineSection(VMGAnimator animator)
        {
            EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
            if (animator.clip == null)
            {
                EditorGUILayout.HelpBox("Assign a VMGAnimationClip to edit tracks.", MessageType.Info);
                return;
            }
            m_Playback.Bind(animator);
            m_Timeline.Playback = m_Playback;

            bool windowOpen = VMGTimelineWindow.IsOpenFor(animator);

            EditorGUILayout.BeginHorizontal();
            if (!windowOpen)
            {
                if (GUILayout.Button("Open in Window", EditorStyles.miniButton, GUILayout.Width(120f)))
                {
                    VMGTimelineWindow.OpenFor(animator);
                }
            }
            else
            {
                EditorGUILayout.LabelField("Timeline opened in separate window.", EditorStyles.miniLabel);
                if (GUILayout.Button("Focus Window", EditorStyles.miniButton, GUILayout.Width(120f)))
                {
                    VMGTimelineWindow.FocusFor(animator);
                }
            }
            EditorGUILayout.EndHorizontal();

            var record = VMGEditorRecord.For(animator);
            if (record != null)
            {
                m_Playback.Record = record;
                record.DrawControls();
            }

            if (!windowOpen)
            {
                m_Playback.DrawControls();
                m_Timeline.Draw(animator);
            }

            var selection = VMGTimelineSelection.For(animator);
            VMGTrackKeyEditor.Draw(animator, selection);
        }
    }
}
