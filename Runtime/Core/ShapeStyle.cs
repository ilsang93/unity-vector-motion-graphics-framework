using System;
using UnityEngine;

namespace VMG.Core
{
    public enum StrokeAlignment
    {
        Center = 0,
        Inner = 1,
        Outer = 2,
    }

    public enum LineCap
    {
        Butt = 0,
        Square = 1,
        Round = 2,
    }

    public enum LineJoin
    {
        Miter = 0,
        Bevel = 1,
        Round = 2,
    }

    [Serializable]
    public struct StrokeStyle
    {
        [Tooltip("Render the stroke. Keyframable (bool).")]
        public bool enabled;
        [Tooltip("Stroke color (multiplied by renderer tint). Keyframable.")]
        public Color color;
        [Min(0f)]
        [Tooltip("Stroke width in path-space units. Keyframable.")]
        public float width;
        [Tooltip("Inner / Center / Outer stroke alignment relative to the path. Keyframable (enum).")]
        public StrokeAlignment alignment;
        [Tooltip("Line cap style at open ends. Keyframable (enum).")]
        public LineCap cap;
        [Tooltip("Join style between segments. Keyframable (enum).")]
        public LineJoin join;
        [Min(1f)]
        [Tooltip("Miter length cap as multiple of full stroke width (matches SVG stroke-miterlimit). Corners sharper than this fall back to bevel. Keyframable.")]
        public float miterLimit;
        [Tooltip("Color the stroke with a two-stop gradient instead of the solid color. Keyframable (bool).")]
        public bool useGradient;
        [Tooltip("Two-stop gradient used when Use Gradient is on. Mapped across the renderer's bounds, multiplied by tint. Keyframable.")]
        public VMGGradient gradient;

        public static StrokeStyle Default => new StrokeStyle
        {
            enabled = true,
            color = Color.white,
            width = 4f,
            alignment = StrokeAlignment.Center,
            cap = LineCap.Butt,
            join = LineJoin.Miter,
            miterLimit = 8f,
            gradient = VMGGradient.Default,
        };

        /// World-renderer default: stroke width in meters (0.04) instead
        /// of pixels (4). Otherwise identical to Default.
        public static StrokeStyle WorldDefault => new StrokeStyle
        {
            enabled = true,
            color = Color.white,
            width = 0.04f,
            alignment = StrokeAlignment.Center,
            cap = LineCap.Butt,
            join = LineJoin.Miter,
            miterLimit = 8f,
            gradient = VMGGradient.Default,
        };
    }

    [Serializable]
    public struct FillStyle
    {
        [Tooltip("Render the fill. Keyframable (bool).")]
        public bool enabled;
        [Tooltip("Fill color (multiplied by renderer tint). Keyframable.")]
        public Color color;
        [Tooltip("Color the fill with a two-stop gradient instead of the solid color. Keyframable (bool).")]
        public bool useGradient;
        [Tooltip("Two-stop gradient used when Use Gradient is on. Mapped across the renderer's bounds, multiplied by tint. Keyframable.")]
        public VMGGradient gradient;

        public static FillStyle Default => new FillStyle
        {
            enabled = false,
            color = Color.white,
            gradient = VMGGradient.Default,
        };
    }

    /// Pivot location for fill extrusion along +Z (camera-facing).
    /// Front: pivot is the front face, shape grows away from the camera (back face at -thickness)
    /// Center: pivot is mid-plane, faces at ±thickness/2
    /// Back: pivot is the back face, shape grows toward the camera (front face at +thickness)
    public enum DepthAlignment
    {
        Front = 0,
        Center = 1,
        Back = 2,
    }

    /// Extrudes the fill polygon into a 3D solid along the Z axis. Stroke
    /// is not extruded — it stays as a thin ribbon co-planar with the
    /// front face (World renderer offsets it slightly to avoid z-fighting).
    [Serializable]
    public struct DepthStyle
    {
        [Tooltip("Extrude the fill into 3D depth. Keyframable (bool).")]
        public bool enabled;
        [Min(0f)]
        [Tooltip("Depth thickness along the Z axis (world units). Keyframable.")]
        public float thickness;
        [Tooltip("Where the pivot sits relative to the extrusion: Front face, Center, or Back face. Keyframable (enum).")]
        public DepthAlignment alignment;

        public static DepthStyle Default => new DepthStyle
        {
            enabled = false,
            thickness = 0.1f,
            alignment = DepthAlignment.Center,
        };

        /// Computes the front/back face Z positions for this style.
        /// Front Z is always >= Back Z (i.e. front face is closer to +Z / camera).
        public void GetFaceZ(out float frontZ, out float backZ)
        {
            float t = Mathf.Max(0f, thickness);
            switch (alignment)
            {
                case DepthAlignment.Front: frontZ = 0f; backZ = -t; break;
                case DepthAlignment.Back: frontZ = t; backZ = 0f; break;
                default: frontZ = t * 0.5f; backZ = -t * 0.5f; break;
            }
        }
    }
}
