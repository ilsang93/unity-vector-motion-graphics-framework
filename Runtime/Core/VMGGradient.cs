using System;
using UnityEngine;

namespace VMG.Core
{
    public enum GradientType
    {
        Linear = 0,
        Radial = 1,
    }

    /// Two-stop gradient (colorA → colorB) baked into mesh vertex colors at
    /// build time. Kept to two stops as plain struct fields so every value is
    /// keyframable from the Animation window (Unity's built-in Gradient type
    /// is neither keyframable nor struct-flattenable, which is why we don't
    /// use it). Evaluated per-vertex against the renderer's union bounds:
    ///
    ///   Linear: t = position projected onto the angle axis, remapped to [0,1]
    ///           across the bounds. angle is in degrees, 0 = +X (left→right),
    ///           90 = +Y (bottom→top), matching CSS gradient angle semantics
    ///           rotated to math convention.
    ///   Radial: t = distance from bounds center / half-diagonal, [0,1].
    [Serializable]
    public struct VMGGradient
    {
        [Tooltip("Linear (directional) or Radial (center-out) gradient. Keyframable (enum).")]
        public GradientType type;
        [Tooltip("Start color (t=0). Keyframable.")]
        public Color colorA;
        [Tooltip("End color (t=1). Keyframable.")]
        public Color colorB;
        [Tooltip("Linear gradient direction in degrees (0 = +X / left→right, 90 = +Y / bottom→top). Ignored for Radial. Keyframable.")]
        public float angle;

        public static VMGGradient Default => new VMGGradient
        {
            type = GradientType.Linear,
            colorA = Color.white,
            colorB = Color.black,
            angle = 0f,
        };

        /// Evaluate the gradient color at a path-space position, given the
        /// bounds the gradient is mapped across. Returns the lerp of
        /// colorA→colorB; the caller multiplies by tint.
        public Color Evaluate(Vector2 pos, Rect bounds)
        {
            float t;
            if (type == GradientType.Radial)
            {
                Vector2 c = bounds.center;
                float halfDiag = 0.5f * new Vector2(bounds.width, bounds.height).magnitude;
                t = halfDiag > 1e-6f ? Vector2.Distance(pos, c) / halfDiag : 0f;
            }
            else
            {
                float rad = angle * Mathf.Deg2Rad;
                Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                // Project the four bounds corners onto the axis to find the
                // span, then remap pos's projection into [0,1] across it.
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                ProjectCorner(bounds.xMin, bounds.yMin, axis, ref min, ref max);
                ProjectCorner(bounds.xMax, bounds.yMin, axis, ref min, ref max);
                ProjectCorner(bounds.xMin, bounds.yMax, axis, ref min, ref max);
                ProjectCorner(bounds.xMax, bounds.yMax, axis, ref min, ref max);
                float span = max - min;
                float p = Vector2.Dot(pos, axis);
                t = span > 1e-6f ? (p - min) / span : 0f;
            }
            return Color.LerpUnclamped(colorA, colorB, Mathf.Clamp01(t));
        }

        private static void ProjectCorner(float x, float y, Vector2 axis, ref float min, ref float max)
        {
            float p = x * axis.x + y * axis.y;
            if (p < min) min = p;
            if (p > max) max = p;
        }
    }
}
