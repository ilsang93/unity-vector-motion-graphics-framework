using System.IO;
using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    [CustomEditor(typeof(VMGAnimator))]
    public class VMGAnimatorEditor : Editor
    {
        // Minimal but valid `animate` statement so the new script runs without
        // a parser error and shows up in Timeline after assignment. Newline
        // pattern matches CSS-importer output for visual consistency.
        const string k_VmgFxStarter =
            "# New VMGFx script.\n" +
            "# Reference any child GameObject by name, or 'self' for this one.\n" +
            "# Field paths follow Unity component members (e.g. localPosition.y).\n" +
            "\n" +
            "animate self localPosition.y -> 1 duration=1 ease=outQuad\n";

        SerializedProperty m_Clip;
        SerializedProperty m_Script;
        SerializedProperty m_Mode;
        SerializedProperty m_Progress;
        SerializedProperty m_Speed;
        SerializedProperty m_FireEventsInExternalMode;
        SerializedProperty m_PlayOnEnable;
        SerializedProperty m_LoopScript;
        SerializedProperty m_Assets;

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
            m_Assets = serializedObject.FindProperty(nameof(VMGAnimator.assets));
            VMGTimelineSelection.Changed += OnSelectionChanged;
            VMGTimelineSelection.DataChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            VMGTimelineSelection.Changed -= OnSelectionChanged;
            VMGTimelineSelection.DataChanged -= OnSelectionChanged;
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
            DrawAssetsSection();
            EditorGUILayout.Space();
            DrawClipSection();
            DrawEmptyStateCreateBar(animator);
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

        // Named-asset registry the script can look up via asset(name). Surfaced
        // alongside Script because that's the only consumer; visible only when
        // a Script is assigned to keep the inspector minimal in clip mode.
        void DrawAssetsSection()
        {
            if (m_Script.objectReferenceValue == null) return;
            EditorGUILayout.PropertyField(m_Assets, true);
        }

        void DrawEmptyStateCreateBar(VMGAnimator animator)
        {
            // Surfaced only when neither authoring slot is filled — first-run
            // affordance to avoid the "what now?" dead end.
            if (m_Script.objectReferenceValue != null) return;
            if (m_Clip.objectReferenceValue != null) return;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("No script or clip assigned. Create one to start authoring.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Create new VMGFx…", "Create a starter .vmgfx text file and assign it to Script."), EditorStyles.miniButton))
            {
                CreateNewVmgFx(animator);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(new GUIContent("Create new Clip…", "Create an empty VMGAnimationClip asset and assign it to Clip."), EditorStyles.miniButton))
            {
                CreateNewClip(animator);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        static string GuessDefaultDirectory(VMGAnimator animator)
        {
            // Prefer the animator's own asset folder (prefab instances and
            // saved prefabs both surface a sane path). Falls back to Assets/.
            var go = animator != null ? animator.gameObject : null;
            string path = go != null ? UnityEditor.AssetDatabase.GetAssetPath(go) : null;
            if (!string.IsNullOrEmpty(path))
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) return dir.Replace('\\', '/');
            }
            return "Assets";
        }

        void CreateNewVmgFx(VMGAnimator animator)
        {
            string dir = GuessDefaultDirectory(animator);
            string defaultName = (animator != null ? animator.name : "New") + ".vmgfx";
            string fullPath = EditorUtility.SaveFilePanel("Create new VMGFx", dir, defaultName, "vmgfx");
            if (string.IsNullOrEmpty(fullPath)) return;
            if (!TryMakeProjectRelative(fullPath, out string assetPath))
            {
                EditorUtility.DisplayDialog("VMG", "File must be saved inside the project's Assets or Packages folder.", "OK");
                return;
            }
            try
            {
                File.WriteAllText(fullPath, k_VmgFxStarter);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("VMG", $"Failed to write file:\n{ex.Message}", "OK");
                return;
            }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset == null)
            {
                EditorUtility.DisplayDialog("VMG", "VMGFx file was created but could not be loaded as a TextAsset.", "OK");
                return;
            }
            // Route through SerializedProperty so Undo + dirtying go through
            // the inspector's standard path.
            serializedObject.Update();
            m_Script.objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
        }

        void CreateNewClip(VMGAnimator animator)
        {
            string dir = GuessDefaultDirectory(animator);
            string defaultName = (animator != null ? animator.name : "New") + "Clip.asset";
            string fullPath = EditorUtility.SaveFilePanel("Create new VMG Animation Clip", dir, defaultName, "asset");
            if (string.IsNullOrEmpty(fullPath)) return;
            if (!TryMakeProjectRelative(fullPath, out string assetPath))
            {
                EditorUtility.DisplayDialog("VMG", "Asset must be saved inside the project's Assets or Packages folder.", "OK");
                return;
            }
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            var clip = ScriptableObject.CreateInstance<VMGAnimationClip>();
            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            serializedObject.Update();
            m_Clip.objectReferenceValue = clip;
            serializedObject.ApplyModifiedProperties();
        }

        static bool TryMakeProjectRelative(string fullPath, out string projectRelative)
        {
            projectRelative = null;
            if (string.IsNullOrEmpty(fullPath)) return false;
            string norm = fullPath.Replace('\\', '/');
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length).Replace('\\', '/');
            if (!norm.StartsWith(projectRoot)) return false;
            string rel = norm.Substring(projectRoot.Length);
            // Accept paths under Assets/ or Packages/.
            if (!rel.StartsWith("Assets/") && !rel.StartsWith("Packages/")) return false;
            projectRelative = rel;
            return true;
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
                    var loopProp = clipSo.FindProperty("loop");
                    var snapProp = clipSo.FindProperty("snapDivisor");
                    clipSo.Update();
                    // Duration is always derived from the latest key/event
                    // time — show read-only. Extend it by dragging the last
                    // key further right in the timeline view.
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.FloatField(new GUIContent("Duration (s)", "Read-only. Equals the time of the latest key or event. Extend by dragging keys past the current end."), clip.duration);
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

            // The full timeline now lives in VMGTimelineWindow only.
            // Inspector embed was removed because (a) it duplicated state
            // with the floating window, and (b) inspector focus changes
            // tore down the embed mid-interaction. The single source of
            // truth for keyframe editing is the window — the inspector
            // just opens / focuses it and shows the selected-key editor.
            bool windowOpen = VMGTimelineWindow.IsOpenFor(animator);
            EditorGUILayout.BeginHorizontal();
            if (!windowOpen)
            {
                if (GUILayout.Button("Open Timeline Window", EditorStyles.miniButton, GUILayout.Width(160f)))
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

            var selection = VMGTimelineSelection.For(animator);
            VMGTrackKeyEditor.Draw(animator, selection);
        }
    }
}
