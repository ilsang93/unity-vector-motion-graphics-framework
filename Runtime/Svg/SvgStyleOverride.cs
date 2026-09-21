using System;
using UnityEngine;
using VMG.Core;

namespace VMG.Svg
{
    /// Replaces the colors an imported SVG carries with the renderer's
    /// own. `Graphic.color` / `Tint` only MULTIPLY the asset's colors, so
    /// a black fill stays black no matter what tint is applied — this is
    /// the escape hatch for "ignore whatever the SVG said and use mine".
    ///
    /// Struct (not class) so every field surfaces to the Animation window
    /// as a keyframable channel, matching the other VMG style types.
    ///
    /// Width override is stroke-only and opt-in separately, because the
    /// common case ("recolor this icon") should not also resize strokes
    /// the artist deliberately varied.
    [Serializable]
    public struct SvgStyleOverride
    {
        [Tooltip("Replace the SVG's own colors with the values below. When off, the asset's authored colors are used. Keyframable (bool).")]
        public bool enabled;
        [Tooltip("Color applied to every sub-shape when Enabled. Still multiplied by the renderer's tint / Graphic.color. Keyframable.")]
        public Color color;
        [Tooltip("Use a two-stop gradient instead of the solid color. Mapped across the whole SVG's bounds, so the gradient spans the icon rather than each sub-shape. Keyframable (bool).")]
        public bool useGradient;
        [Tooltip("Two-stop gradient used when Use Gradient is on. Keyframable.")]
        public VMGGradient gradient;

        [Tooltip("Stroke only: also replace the SVG's stroke widths with Width. Off by default so recoloring does not disturb authored line weights. Keyframable (bool).")]
        public bool overrideWidth;
        [Min(0f)]
        [Tooltip("Stroke width in the SVG's own viewBox units (scaled with the asset like the authored widths). Used only when Override Width is on. Keyframable.")]
        public float width;

        public bool Enabled => enabled;

        public static SvgStyleOverride Default()
        {
            return new SvgStyleOverride
            {
                enabled = false,
                color = Color.white,
                useGradient = false,
                gradient = VMGGradient.Default,
                overrideWidth = false,
                width = 1f,
            };
        }

        /// Applies this override to an authored fill. Returns the style
        /// unchanged when disabled, so callers can apply unconditionally.
        public FillStyle ApplyTo(FillStyle authored)
        {
            if (!enabled) return authored;
            authored.color = color;
            authored.useGradient = useGradient;
            authored.gradient = gradient;
            return authored;
        }

        /// Applies this override to an authored stroke. Width is replaced
        /// only when overrideWidth is set.
        public StrokeStyle ApplyTo(StrokeStyle authored)
        {
            if (!enabled) return authored;
            authored.color = color;
            authored.useGradient = useGradient;
            authored.gradient = gradient;
            if (overrideWidth) authored.width = width;
            return authored;
        }

        /// Value equality for the renderer dirty-flag gate. Lives here
        /// rather than on VectorRendererEquality because VMG.Runtime
        /// (Core) does not reference VMG.Runtime.Svg — the dependency
        /// only runs the other way.
        public static bool Same(in SvgStyleOverride a, in SvgStyleOverride b)
        {
            if (a.enabled != b.enabled) return false;
            // A disabled override contributes nothing to the mesh, so
            // the remaining fields cannot dirty it.
            if (!a.enabled) return true;
            if (a.color != b.color) return false;
            if (a.useGradient != b.useGradient) return false;
            if (a.overrideWidth != b.overrideWidth) return false;
            if (a.overrideWidth && a.width != b.width) return false;
            if (a.useGradient && !VectorRendererEquality.Same(a.gradient, b.gradient)) return false;
            return true;
        }
    }
}
