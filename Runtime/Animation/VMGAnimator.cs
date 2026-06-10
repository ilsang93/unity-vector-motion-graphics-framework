using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VMG.Animation
{
    [AddComponentMenu("VMG/Animation/VMG Animator")]
    [ExecuteAlways]
    public class VMGAnimator : MonoBehaviour
    {
        public VMGAnimationClip clip;
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

        VMGBindingResolver m_Resolver;
        VMGAnimationClip m_BoundClip;
        float m_LastProgress;
        bool m_HasLastProgress;

        TaskCompletionSource<bool> m_PlayTcs;
        CancellationTokenRegistration m_PlayCtsReg;
        bool m_PlayCycleArmed;

        void OnEnable()
        {
            EnsureResolved();
        }

        void OnDisable()
        {
            CancelPlayAsync();
            IsPlaying = false;
        }

        public void Play()
        {
            if (mode != VMGPlayMode.Internal) return;
            EnsureResolved();
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

            EnsureResolved();

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
            EnsureResolved();
            SampleAndWrite(Mathf.Clamp01(normalizedTime), fireEvents: false);
        }

        void Update()
        {
            // Auto-advance only at runtime. ExecuteAlways lets LateUpdate
            // sample in Edit mode (so Unity Animation driving `progress`
            // shows a live preview), but we don't want self-advancing time
            // outside Play mode — that's the editor preview's job.
            if (!Application.isPlaying) return;
            if (mode != VMGPlayMode.Internal || !IsPlaying || clip == null) return;
            if (clip.duration <= 0f) return;

            float delta = Time.deltaTime * speed / clip.duration;
            float next = progress + delta;
            bool cycleCompleted = false;

            if (next >= 1f)
            {
                if (clip.loop)
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
            if (clip == null) return;

            bool fireEvents = mode == VMGPlayMode.Internal || fireEventsInExternalMode;
            SampleAndWrite(progress, fireEvents);
        }

        void EnsureResolved()
        {
            if (m_Resolver != null && m_BoundClip == clip) return;

            bool wasReady = IsReady;
            IsReady = false;

            m_Resolver = new VMGBindingResolver(transform);
            m_BoundClip = clip;
            if (clip != null)
            {
                m_Resolver.Resolve(clip);
            }

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

        void SampleAndWrite(float t, bool fireEvents)
        {
            if (m_Resolver == null) return;
            float clipTime = clip != null ? t * clip.duration : 0f;

            foreach (var rt in m_Resolver.Tracks)
            {
                WriteTrack(rt, clipTime);
            }

            if (fireEvents)
            {
                FireEventsInRange(m_HasLastProgress ? m_LastProgress : t, t);
            }

            m_LastProgress = t;
            m_HasLastProgress = true;

            AfterSample?.Invoke();
        }

        void WriteTrack(VMGResolvedTrack rt, float clipTime)
        {
            switch (rt.track.type)
            {
                case VMGChannelType.Float:
                {
                    float v = VMGTrackEvaluator.EvaluateFloat(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastFloat == v) return;
                    rt.writer.Write(v);
                    rt.lastFloat = v;
                    rt.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Int:
                {
                    int v = VMGTrackEvaluator.EvaluateInt(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastInt == v) return;
                    rt.writer.Write(v);
                    rt.lastInt = v;
                    rt.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Bool:
                {
                    bool v = VMGTrackEvaluator.EvaluateBool(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastBool == v) return;
                    rt.writer.Write(v);
                    rt.lastBool = v;
                    rt.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Color:
                {
                    Color v = VMGTrackEvaluator.EvaluateColor(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastColor == v) return;
                    rt.writer.Write(v);
                    rt.lastColor = v;
                    rt.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector2:
                {
                    Vector4 v = VMGTrackEvaluator.EvaluateVector(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastVector == v) return;
                    rt.writer.Write((Vector2)v);
                    rt.lastVector = v;
                    rt.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector3:
                {
                    Vector4 v = VMGTrackEvaluator.EvaluateVector(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastVector == v) return;
                    rt.writer.Write((Vector3)v);
                    rt.lastVector = v;
                    rt.hasLastValue = true;
                    return;
                }
                case VMGChannelType.Vector4:
                {
                    Vector4 v = VMGTrackEvaluator.EvaluateVector(rt.track, clipTime);
                    if (rt.hasLastValue && rt.lastVector == v) return;
                    rt.writer.Write(v);
                    rt.lastVector = v;
                    rt.hasLastValue = true;
                    return;
                }
            }
        }

        void FireEventsInRange(float fromProgress, float toProgress)
        {
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
