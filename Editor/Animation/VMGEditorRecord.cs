using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VMG.Animation;
#if UNITY_6000_5_OR_NEWER
using VMGObjectId = UnityEngine.EntityId;
#else
using VMGObjectId = System.Int32;
#endif

namespace VMG.EditorTools.Animation
{
    internal class VMGEditorRecord
    {
        VMGAnimator m_Animator;
        bool m_Recording;
        bool m_Registered;

        static readonly Dictionary<VMGObjectId, VMGEditorRecord> s_PerAnimator = new Dictionary<VMGObjectId, VMGEditorRecord>();
#if UNITY_6000_5_OR_NEWER
        static VMGObjectId ObjectId(UnityEngine.Object o) => o.GetEntityId();
#else
        static VMGObjectId ObjectId(UnityEngine.Object o) => o.GetInstanceID();
#endif


        public static VMGEditorRecord For(VMGAnimator animator)
        {
            if (animator == null) return null;
            VMGObjectId id = ObjectId(animator);
            if (!s_PerAnimator.TryGetValue(id, out var rec) || rec == null)
            {
                rec = new VMGEditorRecord();
                rec.Bind(animator);
                s_PerAnimator[id] = rec;
            }
            else if (rec.m_Animator == null)
            {
                rec.Bind(animator);
            }
            return rec;
        }

        struct Probe
        {
            public VMGChannelCandidate candidate;
            public VMGChannelReader reader;
            public object lastValue;
        }

        readonly List<Probe> m_Probes = new List<Probe>();

        public bool IsRecording => m_Recording;

        public void Bind(VMGAnimator animator)
        {
            if (m_Animator == animator) return;
            if (m_Recording) Stop();
            m_Animator = animator;
        }

        public void Unbind()
        {
            // Intentionally do not Stop here — record state lives in a static
            // dict keyed by animator instance and must survive inspector
            // teardown / target swap. Only Stop on explicit user toggle or on
            // playmode/destroy/playback start.
        }

        public void Start()
        {
            if (m_Animator == null || m_Animator.clip == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            BuildProbes();
            m_Recording = true;
            EnsureRegistered();
            if (m_Animator != null) m_Animator.AfterSample += OnAfterSample;
        }

        public void Stop()
        {
            if (m_Animator != null) m_Animator.AfterSample -= OnAfterSample;
            m_Recording = false;
            m_Probes.Clear();
            Unregister();
            CleanupEmptyTracks();
        }

        public void DrawControls()
        {
            using (new EditorGUI.DisabledScope(m_Animator == null || m_Animator.clip == null))
            {
                EditorGUILayout.BeginHorizontal();
                bool wasRecording = m_Recording;
                bool nowRecording = GUILayout.Toggle(wasRecording, "● Record", EditorStyles.miniButton);
                EditorGUILayout.EndHorizontal();
                if (nowRecording != wasRecording)
                {
                    if (nowRecording) Start();
                    else Stop();
                }
            }

            if (m_Recording)
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.9f, 0.25f, 0.25f, 1f);
                EditorGUILayout.HelpBox("RECORDING — changes to this animator's hierarchy are captured at the current playhead.", MessageType.Warning);
                GUI.backgroundColor = prev;
            }
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

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Stop();
        }

        void BuildProbes()
        {
            m_Probes.Clear();
            if (m_Animator == null) return;
            var candidates = VMGChannelTreeBuilder.Build(m_Animator.transform);
            foreach (var c in candidates)
            {
                if (!TryBuildReader(c, out var reader)) continue;
                m_Probes.Add(new Probe { candidate = c, reader = reader, lastValue = reader.Read() });
            }
        }

        bool TryBuildReader(VMGChannelCandidate c, out VMGChannelReader reader)
        {
            reader = null;
            if (m_Animator == null) return false;
            Transform go;
            if (string.IsNullOrEmpty(c.gameObjectPath)) go = m_Animator.transform;
            else { var t = m_Animator.transform.Find(c.gameObjectPath); if (t == null) return false; go = t; }
            var type = ResolveType(c.componentTypeName);
            if (type == null) return false;
            var component = go.GetComponent(type);
            if (component == null) return false;
            if (!VMGFieldPathCompiler.TryCompile(type, c.fieldPath, out var path, out _)) return false;
            reader = new VMGChannelReader(component, path, c.channelType);
            return true;
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

        void OnAfterSample()
        {
            // Re-snapshot all probes so that values written by Sample don't
            // get misread as user edits on the next tick.
            for (int i = 0; i < m_Probes.Count; i++)
            {
                var p = m_Probes[i];
                p.lastValue = p.reader.Read();
                m_Probes[i] = p;
            }
        }

        void OnEditorTick()
        {
            if (m_Animator == null)
            {
                // Animator was destroyed under us — clean up self.
                Stop();
                return;
            }
            if (!m_Recording || m_Animator.clip == null) return;
            var clip = m_Animator.clip;
            float time = m_Animator.progress * Mathf.Max(0.0001f, clip.duration);

            bool changed = false;
            for (int i = 0; i < m_Probes.Count; i++)
            {
                var p = m_Probes[i];
                object cur = p.reader.Read();
                if (!ValuesEqual(p.lastValue, cur))
                {
                    ApplyChange(clip, p.candidate, p.reader.Type, p.lastValue, cur, time);
                    p.lastValue = cur;
                    m_Probes[i] = p;
                    changed = true;
                }
            }
            if (changed)
            {
                clip.RecalculateDuration();
                VMGTimelineSelection.MarkDirty(clip);
                foreach (var ed in Resources.FindObjectsOfTypeAll<VMGAnimatorEditor>()) ed.Repaint();
                foreach (var w in Resources.FindObjectsOfTypeAll<VMGTimelineWindow>()) w.Repaint();
            }
        }

        static bool ValuesEqual(object a, object b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Equals(b);
        }

        static void ApplyChange(VMGAnimationClip clip, VMGChannelCandidate c, VMGChannelType type, object baselineValue, object newValue, float time)
        {
            var track = FindOrCreateTrack(clip, c, type, baselineValue);
            UpsertKey(track, type, newValue, time);
        }

        static VMGAnimationTrack FindOrCreateTrack(VMGAnimationClip clip, VMGChannelCandidate c, VMGChannelType type, object baselineValue)
        {
            foreach (var t in clip.tracks)
            {
                if (t == null) continue;
                if (t.binding.gameObjectPath == (c.gameObjectPath ?? string.Empty)
                    && t.binding.componentTypeName == c.componentTypeName
                    && t.binding.fieldPath == c.fieldPath)
                {
                    return t;
                }
            }
            Undo.RecordObject(clip, "VMG Record — Add Track");
            var track = new VMGAnimationTrack
            {
                type = type,
                binding = new VMGChannelBinding
                {
                    gameObjectPath = c.gameObjectPath ?? string.Empty,
                    componentTypeName = c.componentTypeName,
                    fieldPath = c.fieldPath,
                },
            };
            // Seed a t=0 baseline key so the interpolation between baseline
            // and the just-recorded value makes sense.
            var baselineKey = new VMGAnimationKey { time = 0f };
            VMGEasingPresets.GetTangents(VMGEasingPreset.Linear, out var outT, out var inT);
            baselineKey.outTangent = outT;
            baselineKey.inTangent = inT;
            SetKeyValue(ref baselineKey, type, baselineValue);
            track.keys.Add(baselineKey);
            clip.tracks.Add(track);
            return track;
        }

        static void UpsertKey(VMGAnimationTrack track, VMGChannelType type, object value, float time)
        {
            for (int i = 0; i < track.keys.Count; i++)
            {
                if (Mathf.Approximately(track.keys[i].time, time))
                {
                    var k = track.keys[i];
                    SetKeyValue(ref k, type, value);
                    track.keys[i] = k;
                    return;
                }
            }
            var nk = new VMGAnimationKey { time = time };
            VMGEasingPresets.GetTangents(VMGEasingPreset.Linear, out var outT, out var inT);
            nk.outTangent = outT;
            nk.inTangent = inT;
            SetKeyValue(ref nk, type, value);
            track.keys.Add(nk);
            track.keys.Sort((a, b) => a.time.CompareTo(b.time));
        }

        static void SetKeyValue(ref VMGAnimationKey key, VMGChannelType type, object value)
        {
            switch (type)
            {
                case VMGChannelType.Float: key.floatValue = (float)value; break;
                case VMGChannelType.Int: key.intValue = (int)value; break;
                case VMGChannelType.Bool: key.boolValue = (bool)value; break;
                case VMGChannelType.Color: key.colorValue = (Color)value; break;
                case VMGChannelType.Vector2: { var v = (Vector2)value; key.vectorValue = new Vector4(v.x, v.y, 0f, 0f); break; }
                case VMGChannelType.Vector3: { var v = (Vector3)value; key.vectorValue = new Vector4(v.x, v.y, v.z, 0f); break; }
                case VMGChannelType.Vector4: key.vectorValue = (Vector4)value; break;
            }
        }

        void CleanupEmptyTracks()
        {
            if (m_Animator == null || m_Animator.clip == null) return;
            var clip = m_Animator.clip;
            int removed = clip.tracks.RemoveAll(t => t == null || t.keys == null || t.keys.Count == 0);
            if (removed > 0) VMGTimelineSelection.MarkDirty(clip);
        }
    }
}
