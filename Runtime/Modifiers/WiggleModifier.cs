using System;
using System.Collections.Generic;
using UnityEngine;

namespace VMG.Core
{
    /// After Effects "Wiggle" for a vector path. Adds a smooth, irregular,
    /// time-varying offset along the outline so the line ripples/shakes.
    ///
    /// Two design goals over a naive per-node jitter:
    ///
    /// 1. **Line wiggle, not shape wiggle.** A naive implementation offsets
    ///    each authored node, so a rectangle (4 nodes) just sloshes around as
    ///    a whole — there's no per-line ripple because there are no nodes
    ///    along the edges. We first RESAMPLE the path to a dense, roughly
    ///    even arc-length spacing (controlled by `spacing`) so even a
    ///    low-node shape gets many points to ripple. `spacing` is the line
    ///    wiggle's resolution.
    ///
    /// 2. **No spikes.** A naive implementation gives each node INDEPENDENT
    ///    noise, so two neighbours can lurch in opposite directions and the
    ///    segment between them kinks into a sharp spike. We instead sample
    ///    the noise field along the path's CUMULATIVE ARC LENGTH, so adjacent
    ///    samples read nearby noise coordinates and move together — the
    ///    displacement is spatially continuous and the outline stays smooth.
    ///    `spatialScale` is the wavelength: large = broad lazy undulation,
    ///    small = fine chop (too small reintroduces spikes, so it's clamped
    ///    to a sane floor).
    ///
    /// For closed paths the noise is sampled around a circle parameterised by
    /// the arc-length fraction so the field wraps seamlessly — no visible
    /// discontinuity at the start node.
    ///
    /// Struct (not class) so its fields surface to the Animation window as
    /// keyframable channels, like the other modifiers.
    ///
    /// The renderer rebuilds every frame while this is enabled (time
    /// advances); its dirty-flag is bypassed when Enabled. Disabled or
    /// intensity == 0 returns to the gated fast path.
    [Serializable]
    public struct WiggleModifier
    {
        [Tooltip("Whether wiggle is applied. Keyframable (bool).")]
        public bool enabled;
        [Min(0f)]
        [Tooltip("Displacement amplitude in path-space units. 0 = no wiggle. Keyframable.")]
        public float intensity;
        [Min(0f)]
        [Tooltip("Wiggles per second (Hz). Higher = faster shake. Keyframable.")]
        public float frequency;
        [Min(0f)]
        [Tooltip("Spacing between resampled points along the line, in path-space units. Smaller = finer ripple (more points). 0 = use the authored nodes without resampling (shape wiggle). Keyframable.")]
        public float spacing;
        [Min(0f)]
        [Tooltip("Noise wavelength along the line, in path-space units. Large = broad lazy undulation; small = fine chop. Controls how spiky vs smooth the ripple is. Keyframable.")]
        public float spatialScale;
        [Tooltip("Random seed so multiple shapes in a scene wiggle differently. Keyframable (int).")]
        public int seed;

        public bool Enabled => enabled && intensity > 0f;

        public static WiggleModifier Default()
        {
            return new WiggleModifier
            {
                enabled = false,
                intensity = 5f,
                frequency = 2f,
                spacing = 8f,
                spatialScale = 40f,
                seed = 0,
            };
        }

        // Reused across calls (builds are sequential on the main thread) so
        // resampling doesn't allocate every frame.
        private static readonly List<Vector2> s_resampled = new List<Vector2>(256);
        private static readonly List<float> s_cumLen = new List<float>(256);

        /// Displace the path along its arc length by a smooth, spatially
        /// continuous, time-varying noise field. May replace the path's node
        /// set entirely (resampling), so call it as a modifier stage like
        /// Trim / RoundCorner.
        public void Apply(VectorPath path, float time)
        {
            if (!Enabled) return;
            int n = path.Count;
            if (n < 2) return;

            bool closed = path.closed;

            // ---- 1. Resample to dense, even spacing (line wiggle). ----
            // spacing <= 0 means "keep authored nodes" — degrades to the old
            // shape-wiggle behaviour on purpose for users who want it.
            float total = ArcLength(path, closed);
            if (total < 1e-5f) return;

            if (spacing > 1e-4f)
            {
                // Target one sample every `spacing` units. Clamp count so a
                // huge shape with tiny spacing can't blow up the vertex
                // budget, and a tiny shape still gets enough points to
                // ripple rather than slosh.
                int count = Mathf.Clamp(Mathf.RoundToInt(total / spacing), 8, 2048);
                ArcLengthResample.Resample(path, count, s_resampled);
                path.nodes.Clear();
                for (int i = 0; i < s_resampled.Count; i++)
                    path.nodes.Add(VectorNode.Corner(s_resampled[i]));
                // Resample already honoured open/closed sampling; keep the
                // closed flag so the stroke/fill builders re-close it.
                path.closed = closed;
                n = path.Count;
                if (n < 2) return;
            }

            // ---- 2. Displace using arc-length-keyed noise (smooth). ----
            float ft = time * frequency;
            // Wavelength floor: below ~2x the sample spacing the noise
            // changes faster than the points can follow and spikes return.
            float effSpacing = spacing > 1e-4f ? spacing : (total / Mathf.Max(1, n));
            float wavelength = Mathf.Max(spatialScale, effSpacing * 2f);
            float s = seed * 0.6180339887f; // golden-ratio scramble per shape

            // Cumulative arc length over the CURRENT (pre-displacement) node
            // set. Captured up-front in its own buffer so the noise
            // coordinate is the clean original geometry — displacing in place
            // while also measuring would feed the offsets back into the
            // coordinate and corrupt the field.
            s_cumLen.Clear();
            float running = 0f;
            s_cumLen.Add(0f);
            for (int i = 0; i < n - 1; i++)
            {
                running += Vector2.Distance(path.GetPoint(i), path.GetPoint(i + 1));
                s_cumLen.Add(running);
            }
            // `total` already accounts for the closing segment on closed paths.

            float radius = total / (Mathf.PI * 2f * wavelength); // closed-wrap radius

            for (int i = 0; i < n; i++)
            {
                float arc = s_cumLen[i];
                float nx, ny;
                if (closed)
                {
                    // Wrap seamlessly: map the arc-length fraction onto a
                    // circle so the field at fraction 0 and 1 coincide.
                    float theta = (arc / total) * Mathf.PI * 2f;
                    float cx = Mathf.Cos(theta) * radius;
                    float cy = Mathf.Sin(theta) * radius;
                    nx = Mathf.PerlinNoise(cx + s, cy + ft) * 2f - 1f;
                    ny = Mathf.PerlinNoise(cx + s + 100f, cy + ft + 50f) * 2f - 1f;
                }
                else
                {
                    float u = arc / wavelength;
                    nx = Mathf.PerlinNoise(u + s, ft) * 2f - 1f;
                    ny = Mathf.PerlinNoise(u + s + 100f, ft + 50f) * 2f - 1f;
                }
                var node = path.nodes[i];
                node.position += new Vector2(nx, ny) * intensity;
                path.nodes[i] = node;
            }
        }

        private static float ArcLength(VectorPath path, bool closed)
        {
            int n = path.Count;
            int segCount = closed ? n : n - 1;
            float total = 0f;
            for (int i = 0; i < segCount; i++)
                total += Vector2.Distance(path.GetPoint(i), path.GetPoint((i + 1) % n));
            return total;
        }
    }
}
