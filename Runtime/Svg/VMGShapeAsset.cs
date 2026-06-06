using System;
using System.Collections.Generic;
using UnityEngine;
using VMG.Core;

namespace VMG.Svg
{
    /// One imported SVG = one asset. SVG has many <path>/<rect>/... siblings
    /// so this holds a flat list of sub-shapes that the renderer draws in
    /// order. Coordinates are already normalized to the SVG's viewBox at
    /// import time so the asset is unit-agnostic.
    [Serializable]
    public sealed class VMGSubShape
    {
        public string id;
        public List<VectorNode> nodes = new List<VectorNode>();
        public bool closed = true;
        public FillStyle fill = new FillStyle { enabled = false, color = Color.white };
        public StrokeStyle stroke = new StrokeStyle
        {
            enabled = false,
            color = Color.black,
            width = 1f,
            alignment = StrokeAlignment.Center,
            cap = LineCap.Butt,
            join = LineJoin.Miter,
            miterLimit = 4f,
        };
    }

    [CreateAssetMenu(fileName = "VectorShape", menuName = "VMG/Vector Shape Asset", order = 100)]
    public sealed class VMGShapeAsset : ScriptableObject
    {
        public List<VMGSubShape> subShapes = new List<VMGSubShape>();
        /// SVG viewBox size in user units. Used by renderers to size/scale
        /// the asset into the target rect or world-space dimensions.
        public Vector2 viewBoxSize = new Vector2(100f, 100f);
    }
}
