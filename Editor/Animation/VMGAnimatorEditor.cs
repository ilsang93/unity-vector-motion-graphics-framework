using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    [CustomEditor(typeof(VMGAnimator))]
    public class VMGAnimatorEditor : Editor
    {
        SerializedProperty m_Clip;
        SerializedProperty m_Script;
        SerializedProperty m_Mode;
        SerializedProperty m_Progress;
        SerializedProperty m_Speed;
        SerializedProperty m_FireEventsInExternalMode;
        SerializedProperty m_PlayOnEnable;
        SerializedProperty m_LoopScript;

        static readonly GUIContent k_ScriptLabel = new GUIContent("Script", "Optional VMGFx TextAsset (.vmgfx or .txt). When set, takes priority over Clip.");
        static readonly GUIContent k_ClipLabel = new GUIContent("Clip");
        static readonly GUIContent k_ModeLabel = new GUIContent("Mode");
        static readonly GUIContent k_SpeedLabel = new GUIContent("Speed");
        static readonly GUIContent k_ProgressLabel = new GUIContent("Progress");
        static readonly GUIContent k_FireEventsLabel = new GUIContent("Fire Events In External Mode");
        static readonly GUIContent k_PlayOnEnableLabel = new GUIContent("Play On Enable", "Call Play() at runtime OnEnable. No effect in Edit mode or External play mode.");
        static readonly GUIContent k_LoopScriptLabel = new GUIContent("Loop (Script Mode)", "Wrap progress 1→0 when playing a script. Clip mode uses VMGAnimationClip.loop instead.");
        static readonly GUIContent k_IsReadyLabel = new GUIContent("Is Ready");
        static readonly GUIContent k_IsPlayingLabel = new GUIContent("Is Playing");

        VMGTimelineView m_Timeline;
        VMGEditorPlayback m_Playback;

        void OnEnable()
        {
            m_Clip = serializedObject.FindProperty(nameof(VMGAnimator.clip));
            m_Script = serializedObject.FindProperty(nameof(VMGAnimator.script));
            m_Mode = serializedObject.FindProperty(nameof(VMGAnimator.mode));
            m_Progress = serializedObject.FindProperty(nameof(VMGAnimator.progress));
            m_Speed = serializedObject.FindProperty(nameof(VMGAnimator.speed));
            m_FireEventsInExternalMode = serializedObject.FindProperty(nameof(VMGAnimator.fireEventsInExternalMode));
            m_PlayOnEnable = serializedObject.FindProperty(nameof(VMGAnimator.playOnEnable));
            m_LoopScript = serializedObject.FindProperty(nameof(VMGAnimator.loopScript));
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

            DrawScriptSection();
            EditorGUILayout.Space();
            DrawClipSection();
            EditorGUILayout.Space();
            DrawPlaybackSection();
            EditorGUILayout.Space();
            DrawStatusSection(animator);
            EditorGUILayout.Space();
            DrawTimelineSection(animator);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawScriptSection()
        {
            EditorGUILayout.LabelField("Script", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Script, k_ScriptLabel);
            if (m_Script.objectReferenceValue != null)
            {
                EditorGUILayout.HelpBox("Script is set — it takes priority over Clip at runtime.", MessageType.None);
            }
        }

        void DrawClipSection()
        {
            using (new EditorGUI.DisabledScope(m_Script.objectReferenceValue != null))
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
                    var snapProp = clipSo.FindProperty("snapDivisor");
                    clipSo.Update();
                    EditorGUILayout.PropertyField(autoFitProp, new GUIContent("Auto-fit Duration"));
                    using (new EditorGUI.DisabledScope(autoFitProp.boolValue))
                    {
                        EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (s)"));
                    }
                    EditorGUILayout.PropertyField(loopProp, new GUIContent("Loop"));
                    EditorGUILayout.PropertyField(snapProp, new GUIContent("Snap (per second)"));
                    EditorGUILayout.HelpBox("Drag/scrub/add-key snaps to 1/N second intervals. Hold Shift to disable snap temporarily. Set 0 for no snap.", MessageType.None);
                    clipSo.ApplyModifiedProperties();
                }
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
            using (new EditorGUI.DisabledScope(mode != VMGPlayMode.Internal))
            {
                m_PlayOnEnable.boolValue = EditorGUILayout.Toggle(k_PlayOnEnableLabel, m_PlayOnEnable.boolValue);
            }
            using (new EditorGUI.DisabledScope(mode != VMGPlayMode.Internal || m_Script.objectReferenceValue == null))
            {
                m_LoopScript.boolValue = EditorGUILayout.Toggle(k_LoopScriptLabel, m_LoopScript.boolValue);
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
