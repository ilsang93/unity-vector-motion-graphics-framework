using UnityEngine;
using VMG.Core;

namespace VMG.Animation.Core
{
    // Base for code-built shape descriptors. A descriptor is a lightweight
    // data record produced by VMGFx.Circle() / Rectangle() / etc. and consumed
    // by VMGScene.Add(name, descriptor), which materialises it into a
    // GameObject + VectorImageGraphic (or VectorSpriteRenderer) under the
    // scene root.
    //
    // The descriptor holds the *intent* — size, fill, stroke, rotation, etc.
    // — without knowing whether it will become a UGUI Graphic or a World
    // MeshRenderer. The Scene picks the renderer type from its root.
    //
    // All chain methods return the typed sub-descriptor (via the curiously
    // recurring template pattern) so derived chain calls keep their type
    // through the fluent chain.
    public abstract class VMGShapeDescriptor
    {
        // ----- Transform-level intent -----
        internal Vector2 m_Size = new Vector2(100f, 100f);
        internal bool m_HasSize;
        internal Vector2 m_Position;
        internal bool m_HasPosition;
        internal float m_RotationDeg;
        internal bool m_HasRotation;
        internal Vector2 m_Pivot = new Vector2(0.5f, 0.5f);
        internal bool m_HasPivot;
        internal Vector2 m_Anchor = new Vector2(0.5f, 0.5f);
        internal bool m_HasAnchor;
        internal bool m_FitToRect = true;
        internal bool m_HasFitToRect;

        // ----- Style intent -----
        internal FillStyle m_Fill = new FillStyle { enabled = false, color = Color.white };
        internal bool m_HasFill;
        internal StrokeStyle m_Stroke = StrokeStyle.Default;
        internal bool m_HasStroke;
        internal RoundCornerModifier m_RoundCorners = RoundCornerModifier.Default();
        internal bool m_HasRoundCorner;
        internal TrimPathModifier m_Trim = TrimPathModifier.Default();
        internal bool m_HasTrim;
        internal WiggleModifier m_Wiggle = WiggleModifier.Default();
        internal bool m_HasWiggle;

        // ----- Slot 0 shape (populated by derived ctors) -----
        // Slots 1..3 are reserved for future morph authoring via .Slot(i, ...).
        internal PrimitiveShapeSource m_Slot0Shape = PrimitiveShapeSource.Default();
        internal float m_Slot0Intensity = 1f;

        // Optional override of slots 1..3. Sparse — only set if .Slot() was
        // called for that index.
        internal PrimitiveShapeSource? m_Slot1Shape, m_Slot2Shape, m_Slot3Shape;
        internal float m_Slot1Intensity, m_Slot2Intensity, m_Slot3Intensity;

        protected VMGShapeDescriptor(ShapeKind kind)
        {
            m_Slot0Shape.kind = kind;
        }
    }

    // Generic chain-returning base. Derived descriptors inherit
    //   VMGShapeDescriptor<VMGCircleDescriptor>
    // so .Size(...).Fill(...) keeps the Circle-specific type through the
    // chain (which matters for shape-specific chain methods like
    // VMGPolygonDescriptor.Sides()).
    public abstract class VMGShapeDescriptor<TSelf> : VMGShapeDescriptor
        where TSelf : VMGShapeDescriptor<TSelf>
    {
        protected VMGShapeDescriptor(ShapeKind kind) : base(kind) { }

        TSelf Self => (TSelf)this;

        public TSelf Size(float square)
        {
            m_Size = new Vector2(square, square);
            m_Slot0Shape.size = m_Size;
            m_HasSize = true;
            return Self;
        }

        public TSelf Size(float width, float height)
        {
            m_Size = new Vector2(width, height);
            m_Slot0Shape.size = m_Size;
            m_HasSize = true;
            return Self;
        }

        public TSelf Size(Vector2 size)
        {
            m_Size = size;
            m_Slot0Shape.size = m_Size;
            m_HasSize = true;
            return Self;
        }

        public TSelf Position(float x, float y)
        {
            m_Position = new Vector2(x, y);
            m_HasPosition = true;
            return Self;
        }

        public TSelf Position(Vector2 p)
        {
            m_Position = p;
            m_HasPosition = true;
            return Self;
        }

        public TSelf Rotation(float degrees)
        {
            m_RotationDeg = degrees;
            m_HasRotation = true;
            return Self;
        }

        // RectTransform pivot in 0..1 space. (0,0) is bottom-left, (1,1) is
        // top-right, (0.5, 0.5) is center (the default). Mirrors CSS
        // transform-origin when used to set the rotation anchor of a shape.
        public TSelf Pivot(float x, float y)
        {
            m_Pivot = new Vector2(x, y);
            m_HasPivot = true;
            return Self;
        }

        public TSelf Pivot(Vector2 p)
        {
            m_Pivot = p;
            m_HasPivot = true;
            return Self;
        }

        // RectTransform anchor — same point for anchorMin and anchorMax, so
        // the rect occupies a zero-width/zero-height slot at (x, y) inside
        // its parent's 0..1 frame. Authoring two separate anchors is left
        // out: this single-point form is what makes parent-edge attachment
        // (CSS-style stacking) ergonomic from a flat .vmgfx.
        public TSelf Anchor(float x, float y)
        {
            m_Anchor = new Vector2(x, y);
            m_HasAnchor = true;
            return Self;
        }

        public TSelf Anchor(Vector2 a)
        {
            m_Anchor = a;
            m_HasAnchor = true;
            return Self;
        }

        // Fill: passing a color implies enabled=true. Use NoFill() to disable.
        public TSelf Fill(Color color)
        {
            m_Fill.enabled = true;
            m_Fill.color = color;
            m_HasFill = true;
            return Self;
        }

        public TSelf NoFill()
        {
            m_Fill.enabled = false;
            m_HasFill = true;
            return Self;
        }

        // Common case is (color, width). Advanced fields take named arguments
        // so the call site stays short for the 80% case.
        public TSelf Stroke(
            Color color,
            float width,
            StrokeAlignment alignment = StrokeAlignment.Center,
            LineCap cap = LineCap.Butt,
            LineJoin join = LineJoin.Miter,
            float miterLimit = 8f)
        {
            m_Stroke = new StrokeStyle
            {
                enabled = true,
                color = color,
                width = width,
                alignment = alignment,
                cap = cap,
                join = join,
                miterLimit = miterLimit,
            };
            m_HasStroke = true;
            return Self;
        }

        public TSelf NoStroke()
        {
            m_Stroke.enabled = false;
            m_HasStroke = true;
            return Self;
        }

        // Trim is enabled implicitly: explicit start/end imply you want the
        // trim modifier active.
        public TSelf Trim(float start, float end)
        {
            m_Trim.start = start;
            m_Trim.end = end;
            m_Trim.enabled = true;
            m_HasTrim = true;
            return Self;
        }

        public TSelf RoundCorner(float radius)
        {
            m_RoundCorners.radius = radius;
            m_RoundCorners.enabled = true;
            m_HasRoundCorner = true;
            return Self;
        }

        public TSelf FitToRect(bool fit)
        {
            m_FitToRect = fit;
            m_HasFitToRect = true;
            return Self;
        }

        // Advanced: configure morph slots 1..3 directly. Slot 0 is owned by
        // the descriptor's primary kind (set in the ctor). Indices outside
        // 1..3 are clamped to a no-op.
        public TSelf Slot(int index, ShapeKind kind, float intensity)
        {
            var sub = PrimitiveShapeSource.Default();
            sub.kind = kind;
            switch (index)
            {
                case 1: m_Slot1Shape = sub; m_Slot1Intensity = intensity; break;
                case 2: m_Slot2Shape = sub; m_Slot2Intensity = intensity; break;
                case 3: m_Slot3Shape = sub; m_Slot3Intensity = intensity; break;
            }
            return Self;
        }
    }
}
