using UnityEngine;
using VMG.Core;
using VMG.Svg;

namespace VMG.Animation.Core
{
    // Concrete shape descriptors emitted by VMGFx.Circle() etc. Each
    // pre-seeds slot 0 with its ShapeKind in the base ctor; the only
    // additional surface is per-kind chain methods (sides, points, etc.).

    public sealed class VMGCircleDescriptor : VMGShapeDescriptor<VMGCircleDescriptor>
    {
        internal VMGCircleDescriptor() : base(ShapeKind.Circle) { }
    }

    public sealed class VMGEllipseDescriptor : VMGShapeDescriptor<VMGEllipseDescriptor>
    {
        internal VMGEllipseDescriptor() : base(ShapeKind.Ellipse) { }
    }

    public sealed class VMGRectangleDescriptor : VMGShapeDescriptor<VMGRectangleDescriptor>
    {
        internal VMGRectangleDescriptor() : base(ShapeKind.Rectangle) { }
    }

    public sealed class VMGRoundedRectangleDescriptor : VMGShapeDescriptor<VMGRoundedRectangleDescriptor>
    {
        internal VMGRoundedRectangleDescriptor() : base(ShapeKind.RoundedRectangle) { }

        // Per-shape sugar that also flips the shared modifier so the rounded
        // rect actually looks rounded with one call. RoundCorner() from the
        // base sets the corner modifier; CornerRadius()/CornerRadii() sets
        // the shape's own cornerRadii field (the one Polygon doesn't use).
        public VMGRoundedRectangleDescriptor CornerRadius(float radius)
        {
            m_Slot0Shape.cornerRadii = new Vector2(radius, radius);
            return this;
        }

        // X/Y elliptical corner radii (CSS `border-radius: Xpx / Ypx`).
        public VMGRoundedRectangleDescriptor CornerRadii(float rx, float ry)
        {
            m_Slot0Shape.cornerRadii = new Vector2(rx, ry);
            return this;
        }

        public VMGRoundedRectangleDescriptor CornerRadii(Vector2 radii)
        {
            m_Slot0Shape.cornerRadii = radii;
            return this;
        }
    }

    public sealed class VMGPolygonDescriptor : VMGShapeDescriptor<VMGPolygonDescriptor>
    {
        internal VMGPolygonDescriptor() : base(ShapeKind.Polygon) { }

        public VMGPolygonDescriptor Sides(int n)
        {
            m_Slot0Shape.sides = Mathf.Max(3, n);
            return this;
        }
    }

    public sealed class VMGPathDescriptor : VMGShapeDescriptor<VMGPathDescriptor>
    {
        internal VMGPathDescriptor() : base(ShapeKind.FreePath)
        {
            m_Slot0Shape.freeClosed = false;
        }

        // Points are written directly into the flat node slots so the
        // resulting PrimitiveShapeSource is identical to one authored in the
        // inspector. Cap at MaxFreeNodes (64); extras are dropped.
        public VMGPathDescriptor Points(params Vector2[] pts)
        {
            int n = Mathf.Min(pts.Length, PrimitiveShapeSource.MaxFreeNodes);
            for (int i = 0; i < n; i++)
            {
                var node = new FlatNode { position = pts[i] };
                m_Slot0Shape.SetSlot(i, node);
            }
            m_Slot0Shape.activeNodeCount = n;
            return this;
        }

        public VMGPathDescriptor Closed(bool closed)
        {
            m_Slot0Shape.freeClosed = closed;
            return this;
        }
    }

    // SVG-backed shape. Bypasses the procedural ShapeStack pipeline and
    // assigns a VMGShapeAsset directly to the renderer's SvgAsset slot.
    // Size/Position/Rotation/Fill/Stroke from the base still apply where
    // the renderer respects them — but slot 0 shape configuration is
    // ignored (the SVG asset's own geometry wins).
    public sealed class VMGSvgDescriptor : VMGShapeDescriptor<VMGSvgDescriptor>
    {
        internal VMGShapeAsset m_SvgAsset;

        internal VMGSvgDescriptor() : base(ShapeKind.Rectangle) { }

        public VMGSvgDescriptor Asset(VMGShapeAsset asset)
        {
            m_SvgAsset = asset;
            return this;
        }
    }
}
