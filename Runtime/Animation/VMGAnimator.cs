using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VMG.Animation.Core;

namespace VMG.Animation
{
    [AddComponentMenu("VMG/Animation/VMG Animator")]
    [ExecuteAlways]
    public class VMGAnimator : MonoBehaviour
    {
        public VMGAnimationClip clip;

        [Tooltip("Optional VMGFx script (plain TextAsset). When set, the script's hierarchy is built under this GameObject on enable, and its animations drive playback. Script takes priority over clip when both are assigned.")]
        public TextAsset script;

        public VMGPlayMode mode = VMGPlayMode.Internal;

        [Tooltip("Normalized playhead 0..1. Input in External mode, output in Internal mode.")]
        [Range(0f, 1f)] public float progress;

        [Min(0f)] public float speed = 1f;

        [Tooltip("If true, External mode also fires events when progress sweeps over them. Off by default because External progress can jump.")]
        public bool fireEventsInExternalMode;

        public bool IsReady { get; private set; }
        public event Action ReadyChanged;

        public bool IsPlaying { get; private set; }

        public event Action AfterSample;

        // Raised when a script's 'call' statement crosses the playhead. Listener
        // receives the event name. No-op listener gets a console log instead.
        public event Action<string> ScriptEvent;

        // ---- Internal state ----

        // Clip-mode artefacts (kept exactly as before).
        VMGAnimation m_Anim;
        VMGAnimationClip m_BoundClip;

        // Script-mode artefacts.
        VMGFxCompiled m_Script;
        TextAsset m_BoundScript;
        string m_BoundScriptHash;
        bool m_ScriptModeActive;

        float m_LastProgress;
        bool m_HasLastProgress;

        TaskCompletionSource<bool> m_PlayTcs;
        CancellationTokenRegistration m_PlayCtsReg;
        bool m_PlayCycleArmed;

        void OnEnable()
        {
            EnsureCompiled();
        }

        void OnDisable()
        {
            CancelPlayAsync();
            IsPlaying = false;
        }

        public void Play()
        {
            if (mode != VMGPlayMode.Internal) return;
            EnsureCompiled();
            IsPlaying = true;
            SampleAndWrite(progress, fireEvents: false);
        }

        public void Pause()
        {
            if (mode != VMGPlayMode.Internal) return;
            IsPlaying = false;
        }

        public void Stop()
        {
            if (mode != VMGPlayMode.Internal) return;
            IsPlaying = false;
            progress = 0f;
            m_HasLastProgress = false;
            SampleAndWrite(0f, fireEvents: false);
            CompletePlayAsync(cancelled: true);
        }

        public Task PlayAsync(CancellationToken cancellationToken = default)
        {
            if (mode == VMGPlayMode.External)
                return Task.FromException(new InvalidOperationException("PlayAsync is only valid in Internal mode."));

            EnsureCompiled();

            CancelPlayAsync();
            m_PlayTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_PlayCycleArmed = true;
            if (cancellationToken.CanBeCanceled)
            {
                m_PlayCtsReg = cancellationToken.Register(() => CompletePlayAsync(cancelled: true));
            }
            Play();
            return m_PlayTcs.Task;
        }

        public void Sample(float normalizedTime)
        {
            EnsureCompiled();
            SampleAndWrite(Mathf.Clamp01(normalizedTime), fireEvents: false);
        }

        void Update()
        {
            // Auto-advance only at runtime. ExecuteAlways lets LateUpdate
            // sample in Edit mode (so Unity Animation driving `progress`
            // shows a live preview), but we don't want self-advancing time
            // outside Play mode — that's the editor preview's job.
            if (!Application.isPlaying) return;
            if (mode != VMGPlayMode.Internal || !IsPlaying) return;
            float dur = EffectiveDuration();
            if (dur <= 0f) return;

            float delta = Time.deltaTime * speed / dur;
            float next = progress + delta;
            bool cycleCompleted = false;

            bool loop = m_ScriptModeActive ? false : (clip != null && clip.loop);

            if (next >= 1f)
            {
                if (loop)
                {
                    while (next >= 1f) next -= 1f;
                    cycleCompleted = true;
                }
                else
                {
                    next = 1f;
                    cycleCompleted = true;
                    IsPlaying = false;
                }
            }

            progress = next;

            if (cycleCompleted && m_PlayCycleArmed)
            {
                m_PlayCycleArmed = false;
                CompletePlayAsync(cancelled: false);
            }
        }

        void LateUpdate()
        {
            if (!m_ScriptModeActive && clip == null) return;

            bool fireEvents = mode == VMGPlayMode.Internal || fireEventsInExternalMode;
            SampleAndWrite(progress, fireEvents);
        }

        void EnsureCompiled()
        {
            // Script takes priority over clip when both are assigned.
            if (script != null)
            {
                EnsureScriptCompiled();
                return;
            }

            // Tear down stale script-mode state if the script was removed.
            if (m_ScriptModeActive)
            {
                TeardownScript();
            }

            if (clip == null)
            {
                if (IsReady) { IsReady = false; ReadyChanged?.Invoke(); }
                return;
            }

            if (m_Anim != null && m_BoundClip == clip) return;

            bool wasReady = IsReady;
            IsReady = false;

            // Compile rebuilds the binding resolver internally and produces a
            // VMGAnimation whose onSampled fires after every write — that's
            // the parity hook for AfterSample (used by editor Record mode).
            m_Anim = VMGClipCompiler.Compile(clip, transform);
            m_Anim.onSampled += OnAnimSampled;
            m_BoundClip = clip;

            m_HasLastProgress = false;

            IsReady = true;
            if (wasReady != IsReady)
            {
                ReadyChanged?.Invoke();
            }
            else if (!wasReady)
            {
                ReadyChanged?.Invoke();
            }
        }

        void EnsureScriptCompiled()
        {
            string hash = ComputeScriptHash(script);
            if (m_ScriptModeActive && m_BoundScript == script && m_BoundScriptHash == hash) return;

            if (clip != null)
            {
                Debug.LogWarning($"[VMG.Animation] VMGAnimator on '{name}' has both `script` and `clip` assigned; script takes priority. Clear `clip` to silence this warning.", this);
            }

            bool wasReady = IsReady;
            IsReady = false;

            // Drop any clip-mode core; script-mode has its own pipeline.
            m_Anim = null;
            m_BoundClip = null;

            // Detach the previous compile from the engine before replacing.
            if (m_Script != null)
            {
                m_Script.DetachFromEngine();
                m_Script.OnEvent -= OnScriptEvent;
            }

            try
            {
                m_Script = VMGFxScript.Compile(script.text, transform);
                m_Script.OnEvent += OnScriptEvent;
                // VMGAnimator drives Seek directly each LateUpdate; the engine
                // must NOT tick these animations standalone.
                m_Script.DetachFromEngine();
                m_ScriptModeActive = true;
                m_BoundScript = script;
                m_BoundScriptHash = hash;
                m_HasLastProgress = false;
                IsReady = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VMG.Animation] script compile failed on '{name}': {ex.Message}", this);
                m_Script = null;
                m_ScriptModeActive = false;
                m_BoundScript = null;
                m_BoundScriptHash = null;
                IsReady = false;
            }

            if (wasReady != IsReady || IsReady) ReadyChanged?.Invoke();
        }

        void TeardownScript()
        {
            if (m_Script != null)
            {
                m_Script.DetachFromEngine();
                m_Script.OnEvent -= OnScriptEvent;
                m_Script = null;
            }
            m_BoundScript = null;
            m_BoundScriptHash = null;
            m_ScriptModeActive = false;
        }

        void OnAnimSampled(VMGAnimation _)
        {
            AfterSample?.Invoke();
        }

        void OnScriptEvent(string evName)
        {
            ScriptEvent?.Invoke(evName);
        }

        void SampleAndWrite(float t, bool fireEvents)
        {
            float duration = EffectiveDuration();

            if (m_ScriptModeActive && m_Script != null)
            {
                m_Script.SeekAll(t * duration);
                AfterSample?.Invoke();
            }
            else if (m_Anim != null)
            {
                // progress is authoritative in the adapter — Seek drives the
                // VMGAnimation to the requested iteration time. Seek uses
                // fireCallbacks:false (no onBegin/onUpdate/onLoop/onComplete),
                // but OnAfterRender — which writes tween values and fires
                // onSampled — runs unconditionally.
                m_Anim.Seek(t * duration);
            }
            else return;

            if (fireEvents)
            {
                FireEventsInRange(m_HasLastProgress ? m_LastProgress : t, t);
            }

            m_LastProgress = t;
            m_HasLastProgress = true;
        }

        float EffectiveDuration()
        {
            if (m_ScriptModeActive && m_Script != null) return m_Script.TotalDuration;
            return clip != null ? clip.duration : 0f;
        }

        void FireEventsInRange(float fromProgress, float toProgress)
        {
            // Script-mode events live on the timeline as Call slots — the
            // VMGTimeline itself fires them as progress sweeps. No per-event
            // loop here.
            if (m_ScriptModeActive) return;

            if (clip == null || clip.events == null) return;
            if (fromProgress == toProgress) return;

            float duration = clip.duration;
            if (duration <= 0f) return;

            float fromT = fromProgress * duration;
            float toT = toProgress * duration;

            if (fromT < toT)
            {
                foreach (var e in clip.events)
                {
                    if (e == null) continue;
                    if (e.time > fromT && e.time <= toT) e.invoke?.Invoke();
                }
            }
            else
            {
                // Wrapped around a loop boundary; fire (fromT, duration] then (0, toT].
                foreach (var e in clip.events)
                {
                    if (e == null) continue;
                    if ((e.time > fromT && e.time <= duration) || (e.time >= 0f && e.time <= toT))
                        e.invoke?.Invoke();
                }
            }
        }

        static string ComputeScriptHash(TextAsset t)
        {
            if (t == null) return null;
            // Cheap content hash so a re-saved TextAsset triggers a recompile
            // without keeping the full string around.
            string s = t.text;
            if (string.IsNullOrEmpty(s)) return "";
            unchecked
            {
                int h = 17;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h.ToString() + ":" + s.Length;
            }
        }

        void CompletePlayAsync(bool cancelled)
        {
            m_PlayCtsReg.Dispose();
            m_PlayCtsReg = default;
            var tcs = m_PlayTcs;
            m_PlayTcs = null;
            m_PlayCycleArmed = false;
            if (tcs == null) return;
            if (cancelled) tcs.TrySetCanceled();
            else tcs.TrySetResult(true);
        }

        void CancelPlayAsync()
        {
            CompletePlayAsync(cancelled: true);
        }
    }
}
