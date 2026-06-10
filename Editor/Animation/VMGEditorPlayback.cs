using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VMG.Animation;

namespace VMG.EditorTools.Animation
{
    internal class VMGEditorPlayback
    {
        VMGAnimator m_Animator;
        bool m_Playing;
        double m_LastTime;
        bool m_Registered;

        struct BaselineEntry
        {
            public VMGChannelWriter writer;
            public VMGChannelType type;
            public object value;
        }
        readonly List<BaselineEntry> m_Baseline = new List<BaselineEntry>();
        bool m_BaselineCaptured;

        public bool IsPlaying => m_Playing;
        public bool HasBaseline => m_BaselineCaptured;

        public VMGEditorRecord Record { get; set; }

        public void Bind(VMGAnimator animator)
        {
            if (m_Animator == animator) return;
            Stop();
            m_Animator = animator;
        }

        public void Unbind()
        {
            Stop();
            m_Animator = null;
        }

        public void Play()
        {
            if (m_Animator == null) return;
            if (m_Animator.clip == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Starting playback always exits record mode — capturing values
            // while playback overwrites them would create nonsense keys.
            Record?.Stop();
            EnsureBaselineCaptured();
            // Pressing Play after the clip has finished restarts from the top.
            if (m_Animator.progress >= 1f) m_Animator.progress = 0f;
            m_Playing = true;
            m_LastTime = EditorApplication.timeSinceStartup;
            EnsureRegistered();
        }

        public void Pause()
        {
            m_Playing = false;
        }

        public void Stop()
        {
            m_Playing = false;
            if (m_Animator != null)
            {
                m_Animator.progress = 0f;
            }
            RestoreBaselineAndClear();
        }

        public void EnsureBaselineCaptured()
        {
            if (m_BaselineCaptured) return;
            if (m_Animator == null || m_Animator.clip == null) return;
            m_Baseline.Clear();
            var clip = m_Animator.clip;
            foreach (var track in clip.tracks)
            {
                if (track == null) continue;
                if (!TryBuildWriterAndReader(track, out var writer, out var reader)) continue;
                m_Baseline.Add(new BaselineEntry
                {
                    writer = writer,
                    type = track.type,
                    value = reader.Read(),
                });
            }
            m_BaselineCaptured = true;
        }

        void RestoreBaselineAndClear()
        {
            if (!m_BaselineCaptured) return;
            foreach (var e in m_Baseline)
            {
                if (e.writer == null) continue;
                switch (e.type)
                {
                    case VMGChannelType.Float: e.writer.Write((float)e.value); break;
                    case VMGChannelType.Int: e.writer.Write((int)e.value); break;
                    case VMGChannelType.Bool: e.writer.Write((bool)e.value); break;
                    case VMGChannelType.Color: e.writer.Write((Color)e.value); break;
                    case VMGChannelType.Vector2: e.writer.Write((Vector2)e.value); break;
                    case VMGChannelType.Vector3: e.writer.Write((Vector3)e.value); break;
                    case VMGChannelType.Vector4: e.writer.Write((Vector4)e.value); break;
                }
            }
            m_Baseline.Clear();
            m_BaselineCaptured = false;
            SceneView.RepaintAll();
        }

        bool TryBuildWriterAndReader(VMGAnimationTrack track, out VMGChannelWriter writer, out VMGChannelReader reader)
        {
            writer = null;
            reader = null;
            var go = ResolveGameObject(track.binding.gameObjectPath);
            if (go == null) return false;
            var type = ResolveType(track.binding.componentTypeName);
            if (type == null) return false;
            var component = go.GetComponent(type);
            if (component == null) return false;
            if (!VMGFieldPathCompiler.TryCompile(type, track.binding.fieldPath, out var path, out _)) return false;
            writer = new VMGChannelWriter(component, path, track.type);
            if (!writer.IsTypeCompatible(out _)) { writer = null; return false; }
            reader = new VMGChannelReader(component, path, track.type);
            return true;
        }

        GameObject ResolveGameObject(string path)
        {
            if (m_Animator == null) return null;
            if (string.IsNullOrEmpty(path)) return m_Animator.gameObject;
            var t = m_Animator.transform.Find(path);
            return t != null ? t.gameObject : null;
        }

        static readonly Dictionary<string, Type> s_TypeCache = new Dictionary<string, Type>();
        static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_TypeCache.TryGetValue(name, out var cached)) return cached;
            var t = Type.GetType(name, false);
            if (t == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(name, false);
                    if (t != null) break;
                }
            }
            s_TypeCache[name] = t;
            return t;
        }

        public void DrawControls()
        {
            using (new EditorGUI.DisabledScope(m_Animator == null || m_Animator.clip == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (!m_Playing)
                {
                    if (GUILayout.Button("Play", EditorStyles.miniButton)) Play();
                }
                else
                {
                    if (GUILayout.Button("Pause", EditorStyles.miniButton)) Pause();
                }
                if (GUILayout.Button("Stop", EditorStyles.miniButton)) Stop();
                EditorGUILayout.EndHorizontal();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode && m_Playing) Pause();
        }

        void EnsureRegistered()
        {
            if (m_Registered) return;
            EditorApplication.update += OnEditorTick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            m_Registered = true;
        }

        void Unregister()
        {
            if (!m_Registered) return;
            EditorApplication.update -= OnEditorTick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            m_Registered = false;
        }

        void OnEditorTick()
        {
            if (m_Animator == null) { Unregister(); return; }
            if (!m_Playing) return;

            var clip = m_Animator.clip;
            if (clip == null || clip.duration <= 0f) { Pause(); return; }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - m_LastTime);
            m_LastTime = now;

            float delta = dt * m_Animator.speed / clip.duration;
            float next = m_Animator.progress + delta;
            bool cycleEnd = false;

            if (next >= 1f)
            {
                if (clip.loop) { while (next >= 1f) next -= 1f; }
                else { next = 1f; cycleEnd = true; }
            }

            m_Animator.progress = next;
            SafeSample(next);

            SceneView.RepaintAll();
            foreach (var ed in Resources.FindObjectsOfTypeAll<VMGAnimatorEditor>())
            {
                ed.Repaint();
            }
            foreach (var w in Resources.FindObjectsOfTypeAll<VMGTimelineWindow>())
            {
                w.Repaint();
            }

            if (cycleEnd) Pause();
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Pause();
        }

        void SafeSample(float t)
        {
            try { m_Animator.Sample(t); }
            catch (Exception ex)
            {
                Debug.LogError($"[VMG.Animation] Editor sample failed: {ex.Message}");
                Pause();
            }
        }
    }
}
