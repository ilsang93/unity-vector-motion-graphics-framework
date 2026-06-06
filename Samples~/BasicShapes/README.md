# Basic Shapes Sample

Demonstrates the simplest VMG use cases.

## Quick start

1. **Create > UI > Vector Image** in the Hierarchy. This drops a 160×160 vector circle in your canvas.
2. Tweak `Shape ▸ Kind` to Circle / Rectangle / RoundedRectangle / Polygon.
3. Toggle `Stroke` and `Fill` independently. Set `Stroke ▸ Alignment` to Center/Inner/Outer.
4. Add the `VMGDemoSpinner` component on the same GameObject for a live trim-path sweep without an Animator.

## Animator integration

All numeric and color fields on `VectorImageGraphic` (and `VectorSpriteRenderer`)
are `[SerializeField]`. They appear directly in the Animation window:

- `m_Stroke.color`, `m_Stroke.width`, `m_Stroke.alignment`
- `m_Fill.color`
- `m_Shape.size`, `m_Shape.cornerRadius`, `m_Shape.sides`, `m_Shape.center`
- `m_Trim.start`, `m_Trim.end`, `m_Trim.offset`
- `m_RoundCorners.radius`

Just record keyframes — VMG marks the graphic dirty in `LateUpdate` so the
mesh regenerates each frame the Animator writes.
