using System.Collections.Generic;
using UnityEngine;

namespace VMG.Animation.Core
{
    // Global tick dispatcher for code-built animations. anime.js owns its own
    // rAF loop; we ride Unity's Update via a hidden DontDestroyOnLoad host.
    // This keeps VMGAnimation a plain C# object so it can be used outside of
    // a MonoBehaviour (tests, code-driven animations).
    //
    // Deferred registration: VMG.Animate(...) wants "create and auto-play"
    // semantics like anime.js, but if a freshly-built animation is handed
    // off to a Timeline before the next tick, the engine must not drive it
    // (the timeline owns time for its children). RegisterDeferred enqueues
    // the animation for the *next* tick; CancelDeferred removes it if the
    // timeline claims it first.
    public static class VMGEngine
    {
        static readonly List<VMGAnimation> s_Animations = new List<VMGAnimation>();
        static readonly List<VMGTimer> s_Timers = new List<VMGTimer>();
        static readonly List<VMGAnimate> s_DeferredBuilders = new List<VMGAnimate>();
        static readonly List<VMGTimeline> s_DeferredTimelines = new List<VMGTimeline>();
        static Host s_Host;

        public static void Register(VMGAnimation anim)
        {
            if (anim == null) return;
            EnsureHost();
            if (!s_Animations.Contains(anim)) s_Animations.Add(anim);
        }

        public static void Unregister(VMGAnimation anim)
        {
            if (anim == null) return;
            s_Animations.Remove(anim);
        }

        // Queue for activation on the next LateUpdate. We hold the *builder*
        // rather than the animation so we can call EnsureFinalized on it
        // exactly once just before it starts ticking — that's what builds the
        // tween list from the pending .To/.FromTo calls. If a Timeline takes
        // ownership first, CancelDeferred removes the builder from the queue.
        internal static void RegisterDeferred(VMGAnimate builder)
        {
            if (builder == null) return;
            EnsureHost();
            if (!s_DeferredBuilders.Contains(builder)) s_DeferredBuilders.Add(builder);
        }

        internal static void CancelDeferred(VMGAnimate builder)
        {
            if (builder == null) return;
            s_DeferredBuilders.Remove(builder);
        }

        // Timeline path. A timeline is its own Timer, so we just tick it and
        // it dispatches to its children. Same deferred-then-active pattern.
        internal static void RegisterDeferredTimeline(VMGTimeline tl)
        {
            if (tl == null) return;
            EnsureHost();
            if (!s_DeferredTimelines.Contains(tl)) s_DeferredTimelines.Add(tl);
        }

        internal static void CancelDeferredTimeline(VMGTimeline tl)
        {
            if (tl == null) return;
            s_DeferredTimelines.Remove(tl);
        }

        internal static void UnregisterTimeline(VMGTimeline tl)
        {
            if (tl == null) return;
            s_Timers.Remove(tl);
        }

        static void EnsureHost()
        {
            if (s_Host != null) return;
            var go = new GameObject("[VMGEngine]") { hideFlags = HideFlags.HideAndDontSave };
            s_Host = go.AddComponent<Host>();
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
        }

        // Drives every registered animation. LateUpdate matches VMGAnimator's
        // write timing — animation drivers (Unity Animator, user scripts)
        // update in Update, we write in LateUpdate so we win the same races
        // VMGAnimator wins today.
        class Host : MonoBehaviour
        {
            void LateUpdate()
            {
                if (!Application.isPlaying) return;

                // Promote deferred builders first so they start ticking
                // this same frame. EnsureFinalized() builds the tween list
                // from any pending .To/.FromTo calls, then returns the
                // VMGAnimation core for us to register.
                if (s_DeferredBuilders.Count > 0)
                {
                    for (int i = 0; i < s_DeferredBuilders.Count; i++)
                    {
                        var anim = s_DeferredBuilders[i].PromoteToEngine();
                        if (anim != null && !s_Animations.Contains(anim)) s_Animations.Add(anim);
                    }
                    s_DeferredBuilders.Clear();
                }

                if (s_DeferredTimelines.Count > 0)
                {
                    for (int i = 0; i < s_DeferredTimelines.Count; i++)
                    {
                        var tl = s_DeferredTimelines[i];
                        if (tl.PromoteToEngine() && !s_Timers.Contains(tl))
                            s_Timers.Add(tl);
                    }
                    s_DeferredTimelines.Clear();
                }

                float dt = Time.deltaTime;
                for (int i = s_Animations.Count - 1; i >= 0; i--)
                {
                    s_Animations[i].Tick(dt);
                }
                for (int i = s_Timers.Count - 1; i >= 0; i--)
                {
                    s_Timers[i].Tick(dt);
                }
            }
        }
    }
}
