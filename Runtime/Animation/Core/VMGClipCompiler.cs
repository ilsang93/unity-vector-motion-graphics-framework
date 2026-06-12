using UnityEngine;

namespace VMG.Animation.Core
{
    // Compiles an existing VMGAnimationClip asset into a runtime
    // VMGAnimation. This is the Z-option seam: data authoring stays in
    // the ScriptableObject world, runtime execution moves to the new
    // tween core, and a single function bridges them.
    public static class VMGClipCompiler
    {
        public static VMGAnimation Compile(VMGAnimationClip clip, Transform root)
        {
            var anim = new VMGAnimation();
            if (clip == null) return anim;

            anim.iterationDuration = Mathf.Max(0.0001f, clip.duration);
            anim.iterationCount = clip.loop ? float.PositiveInfinity : 1f;

            // Reuse the existing resolver — same writer, same type cache,
            // same error handling. This is the lever that makes
            // bit-identical parity with VMGAnimator possible.
            var resolver = new VMGBindingResolver(root);
            resolver.Resolve(clip);

            foreach (var resolved in resolver.Tracks)
            {
                anim.tweens.Add(new VMGClipTween
                {
                    startTime = 0f,
                    endTime = anim.iterationDuration,
                    resolved = resolved,
                });
            }

            return anim;
        }
    }
}
