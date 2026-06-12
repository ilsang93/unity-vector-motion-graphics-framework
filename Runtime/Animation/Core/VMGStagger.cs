using System;
using UnityEngine;

namespace VMG.Animation.Core
{
    // anime.js's `stagger(value, { from })` distributes a value across N
    // targets so each target sees a different offset. VMG uses the same idea
    // but the surface is a Timeline helper: tl.Stagger(targets, builder, step)
    // emits N children and places them at staggered offsets.
    //
    // VMGStagger itself is just the offset-distribution maths + the from-mode
    // enum. VMGTimeline.Stagger(...) (this file's companion) is the actual
    // public API.
    public enum VMGStaggerFrom
    {
        // 0 -> N-1 in index order (anime.js 'first' / 0 / unset default).
        First,
        // Distance from the geometric center; center index gets 0.
        Center,
        // N-1 -> 0 (anime.js 'last').
        Last,
        // Shuffle the linear sequence (anime.js 'random'). Reproducible per
        // call when seed is provided; otherwise UnityEngine.Random state used.
        Random,
    }

    // Public helpers for the "stagger a value across indices" pattern. Use
    // inside the Stagger lambda when you want a per-index value (anime.js's
    // x: stagger([0, 500])) — group 3 #2 FunctionValue will give this a
    // smoother surface; for now the math is here:
    //
    //   tl.Stagger(boxes, (t, i, n) =>
    //       VMGFx.Animate(t).To("x", VMGStagger.Lerp(i, n, 0f, 500f)), 0.1f);
    //
    // No range/from-mode here on purpose: the Timeline.Stagger call already
    // owns the time distribution. These are just value helpers for the
    // lambda's inside.
    public static class VMGStagger
    {
        // Evenly spaces [from..to] across N indices. i=0 -> from, i=n-1 -> to.
        // n <= 1 returns from (parity with anime.js single-target).
        public static float Lerp(int i, int n, float from, float to)
        {
            if (n <= 1) return from;
            float t = i / (float)(n - 1);
            return from + (to - from) * t;
        }
    }

    // Internal: pre-computes the per-index multipliers. Multipliers are in
    // [0, max] where max is N-1 (linear/random), (N-1)/2 (center), N-1 (last).
    // The caller multiplies them by `step` to get actual time offsets.
    //
    // Kept as a small struct rather than a static helper so the values array
    // can be sized once and reused if we ever batch multiple Stagger calls.
    internal struct VMGStaggerCalculator
    {
        public float[] values;
        public float maxValue;

        public static VMGStaggerCalculator Build(int count, VMGStaggerFrom from, int? seed)
        {
            if (count <= 0) return new VMGStaggerCalculator { values = Array.Empty<float>(), maxValue = 0f };
            var values = new float[count];
            float fromIndex;
            switch (from)
            {
                case VMGStaggerFrom.Center: fromIndex = (count - 1) / 2f; break;
                case VMGStaggerFrom.Last: fromIndex = count - 1; break;
                default: fromIndex = 0f; break; // First, Random (random shuffles after)
            }
            float max = 0f;
            for (int i = 0; i < count; i++)
            {
                float v = Mathf.Abs(fromIndex - i);
                values[i] = v;
                if (v > max) max = v;
            }
            if (from == VMGStaggerFrom.Random)
            {
                // Fisher-Yates over the linear values. anime.js does the same
                // (shuffle on the values array, not the index sequence).
                System.Random rng = seed.HasValue ? new System.Random(seed.Value) : null;
                for (int i = count - 1; i > 0; i--)
                {
                    int j = rng != null ? rng.Next(0, i + 1) : UnityEngine.Random.Range(0, i + 1);
                    var tmp = values[i];
                    values[i] = values[j];
                    values[j] = tmp;
                }
            }
            return new VMGStaggerCalculator { values = values, maxValue = max };
        }
    }
}
