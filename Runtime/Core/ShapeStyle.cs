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
        [Tooltip("Miter length cap (as multiple of half-width) before falling back to bevel. Keyframable.")]
        public float miterLimit;

        public static StrokeStyle Default => new StrokeStyle
        {
            enabled = true,
            color = Color.white,
            width = 4f,
            alignment = StrokeAlignment.Center,
            cap = LineCap.Butt,
            join = LineJoin.Miter,
            miterLimit = 8f,
        };
    }

    [Serializable]
    public struct FillStyle
    {
        [Tooltip("Render the fill. Keyframable (bool).")]
        public bool enabled;
        [Tooltip("Fill color (multiplied by renderer tint). Keyframable.")]
        public Color color;

        public static FillStyle Default => new FillStyle
        {
            enabled = false,
            color = Color.white,
        };
    }
}
