using UnityEngine;

namespace VMG.Core
{
    // Value-equality helpers used by the renderer dirty-flag gate. All
    // mesh-input types in the package are plain structs of POD fields,
    // so a hand-written field-by-field check is allocation-free and
    // ~100x faster than the default reflection-based ValueType.Equals.
    //
    // Comparison is scoped to fields that actually influence the built
    // mesh — Normalize() defaults and animation channels both write
    // these fields, so a stable equality across two consecutive frames
    // proves no rebuild is needed. Reference fields (SvgAsset etc.) are
    // checked at the call site; their internal mutations need an
    // explicit SetMeshDirty().
    public static class VectorRendererEquality
    {
        public static bool Same(in FlatNode a, in FlatNode b)
        {
            return a.position == b.position
                && a.inTangent == b.inTangent
                && a.outTangent == b.outTangent
                && a.type == b.type;
        }

        // PrimitiveShapeSource carries 64 FlatNode slots, but only the
        // first activeNodeCount of them feed BuildFree, and only when
        // kind == FreePath. Skipping inactive slots keeps the per-frame
        // gate cheap on primitives that don't touch FreePath at all.
        public static bool Same(in PrimitiveShapeSource a, in PrimitiveShapeSource b)
        {
            if (a.kind != b.kind) return false;
            if (a.center != b.center) return false;
            if (a.size != b.size) return false;
            if (a.sides != b.sides) return false;
            if (a.cornerRadii != b.cornerRadii) return false;
            if (a.circleSegments != b.circleSegments) return false;
            if (a.freeClosed != b.freeClosed) return false;
            if (a.bezierSamplesPerSegment != b.bezierSamplesPerSegment) return false;
            if (a.activeNodeCount != b.activeNodeCount) return false;

            // Only walk node slots when this kind actually reads them.
            // freeNodesLegacy is wiped to null on first Normalize() so a
            // reference compare is enough for the steady state.
            if (a.kind == ShapeKind.FreePath)
            {
                if (!ReferenceEquals(a.freeNodesLegacy, b.freeNodesLegacy)) return false;
                int n = Mathf.Clamp(a.activeNodeCount, 0, PrimitiveShapeSource.MaxFreeNodes);
                if (n > 0 && !SameNodes(a, b, n)) return false;
            }
            return true;
        }

        // Hand-unrolled switch matches PrimitiveShapeSource's own
        // GetSlot pattern — keeps the comparison branch-predictable and
        // avoids a virtual call per node. Stops at activeNodeCount so a
        // FreePath using 5 of 64 slots only pays for 5 comparisons.
        static bool SameNodes(in PrimitiveShapeSource a, in PrimitiveShapeSource b, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (!Same(a.GetSlot(i), b.GetSlot(i))) return false;
            }
            return true;
        }

        public static bool Same(in ShapeSlot a, in ShapeSlot b)
        {
            if (a.intensity != b.intensity) return false;
            // Slots at intensity 0 are skipped by ShapeStack.Build, so
            // their shape data doesn't affect the mesh and we don't
            // pay to compare it.
            if (a.intensity <= 0f && b.intensity <= 0f) return true;
            return Same(a.shape, b.shape);
        }

        public static bool Same(in ShapeStack a, in ShapeStack b)
        {
            if (a.resampleCount != b.resampleCount) return false;
            if (a.alignment != b.alignment) return false;
            if (!Same(a.Slot0, b.Slot0)) return false;
            if (!Same(a.Slot1, b.Slot1)) return false;
            if (!Same(a.Slot2, b.Slot2)) return false;
            if (!Same(a.Slot3, b.Slot3)) return false;
            return true;
        }

        public static bool Same(in StrokeStyle a, in StrokeStyle b)
        {
            // Both disabled → the mesh skips stroke entirely, so the
            // remaining fields don't matter for rebuild correctness.
            if (!a.enabled && !b.enabled) return true;
            return a.enabled == b.enabled
                && a.color == b.color
                && a.width == b.width
                && a.alignment == b.alignment
                && a.cap == b.cap
                && a.join == b.join
                && a.miterLimit == b.miterLimit
                && a.useGradient == b.useGradient
                && (!a.useGradient || Same(a.gradient, b.gradient));
        }

        public static bool Same(in FillStyle a, in FillStyle b)
        {
            if (!a.enabled && !b.enabled) return true;
            return a.enabled == b.enabled
                && a.color == b.color
                && a.useGradient == b.useGradient
                && (!a.useGradient || Same(a.gradient, b.gradient));
        }

        public static bool Same(in VMGGradient a, in VMGGradient b)
        {
            return a.type == b.type
                && a.colorA == b.colorA
                && a.colorB == b.colorB
                && a.angle == b.angle;
        }

        public static bool Same(in WiggleModifier a, in WiggleModifier b)
        {
            // Both inactive (disabled or zero amplitude) → no mesh effect.
            if (!a.Enabled && !b.Enabled) return true;
            return a.enabled == b.enabled
                && a.intensity == b.intensity
                && a.frequency == b.frequency
                && a.spacing == b.spacing
                && a.spatialScale == b.spatialScale
                && a.seed == b.seed;
        }

        public static bool Same(in DepthStyle a, in DepthStyle b)
        {
            if (!a.enabled && !b.enabled) return true;
            return a.enabled == b.enabled
                && a.thickness == b.thickness
                && a.alignment == b.alignment;
        }

        public static bool Same(in RoundCornerModifier a, in RoundCornerModifier b)
        {
            if (!a.enabled && !b.enabled) return true;
            return a.enabled == b.enabled
                && a.radius == b.radius
                && a.segmentsPerCorner == b.segmentsPerCorner;
        }

        public static bool Same(in TrimPathModifier a, in TrimPathModifier b)
        {
            if (!a.enabled && !b.enabled) return true;
            return a.enabled == b.enabled
                && a.start == b.start
                && a.end == b.end
                && a.offset == b.offset;
        }
    }
}
