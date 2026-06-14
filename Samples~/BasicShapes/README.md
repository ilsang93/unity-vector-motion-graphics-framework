# Basic Shapes Sample

Demonstrates the simplest VMG use cases.

## Quick start

1. **Create > UI > Vector Image** in the Hierarchy. This drops a 160×160 vector circle in your canvas.
2. Tweak `Shape Stack ▸ Slot 0 ▸ Shape ▸ Kind` to Circle / Rectangle / RoundedRectangle / Polygon.
3. Toggle `Stroke` and `Fill` independently. Set `Stroke ▸ Alignment` to Center/Inner/Outer.
4. Add the `VMGDemoSpinner` component on the same GameObject for a live trim-path sweep without an Animator.

## Animator integration

All numeric and color fields on `VectorImageGraphic` (and `VectorSpriteRenderer`)
are public serialized fields. They appear directly in the Animation window:

- `Stroke.color`, `Stroke.width`, `Stroke.alignment`
- `Fill.color`
- `ShapeStack.Slot0.shape.size`, `.cornerRadii`, `.sides`, `.center`
- `ShapeStack.Slot0.intensity` ... `Slot3.intensity` (blend control)
- `Trim.start`, `Trim.end`, `Trim.offset`
- `RoundCorners.radius`

Just record keyframes — VMG marks the graphic dirty in `LateUpdate` so the
mesh regenerates each frame the Animator writes.

## Multi-shape blending

To morph between two shapes (e.g., circle ⇄ rectangle), put each shape
in its own slot and animate the intensities. A simple recipe:

1. **Slot 0**: kind = Circle, intensity = 1.
2. **Slot 1**: kind = Rectangle, intensity = 0.
3. Keyframe `Slot1.intensity` from 0 → 1 — the circle morphs into the rectangle.
4. Optionally drive `Slot0.intensity` from 1 → 0 in parallel so the
   result is a clean rectangle at the end rather than a 50/50 blend.

Slots are weighted equally; there's no "base" slot. Three or four
active slots produce a smooth N-way blend.

### Live morph demo without an Animator

Add the `VMGDemoShapeMorph` component on the same Vector Image to
ping-pong Slot 0 ↔ Slot 1 intensities each frame:

- `Speed` controls cycles per second (one cycle = A → B → A).
- `Ease` smoothstep blends the linear ping-pong toward a hold-and-snap
  feel (0 = pure linear, 1 = full smoothstep).

The renderer's dirty-flag detects the per-frame intensity change via
its snapshot equality check, so the mesh updates cleanly without any
explicit `SetMeshDirty()` call.
