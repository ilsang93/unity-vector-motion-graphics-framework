# Vector Text (TMP) Sample

Renders **TextMeshPro** text as true VMG **vector outlines** — fill, stroke
(테두리), variable thickness (두께), Wiggle and PowerPoint-style Warp — by using
TMP purely as the layout engine and re-drawing each glyph's font contours
through the existing VMG mesh pipeline.

TMP's own mesh is a row of textured quads, not letterforms, so the glyph
*shape* is parsed directly from the font's TrueType (`.ttf`) outlines. TMP says
**where** each glyph sits; the parser says **what** each glyph looks like.

## Quick start — Canvas (UI)

1. Create a **UI ▸ Text - TextMeshPro** (or any `TextMeshProUGUI`).
2. **Add Component ▸ VMG ▸ Rendering ▸ Vector Text (UI, TMP)**.
3. The TMP text stops drawing its SDF glyphs and a hidden child
   `__VMGVectorTextMesh` draws the vector version in its place.
4. Set `Fill` (color / gradient), `Stroke` (테두리: width 두께, alignment,
   join), and toggle `Wiggle` for an animated edge shimmer.

## Quick start — World (3D)

1. Create a **3D Object ▸ Text - TextMeshPro** (a world-space `TextMeshPro`).
2. **Add Component ▸ VMG ▸ Rendering ▸ Vector Text World (TMP)**.
3. Same Fill / Stroke / Wiggle controls; the mesh is drawn by an
   auto-managed child `MeshFilter` / `MeshRenderer` with the
   `VMG/World/VectorSDF` material so the edges stay crisp at any zoom.

## Warp (WordArt)

The `Warp` field bends the whole text block per glyph vertex:

- **Arc / Wave** — bend or ripple the baseline.
- **Circle** — wrap the line into a ring (`secondary` = sweep degrees).
- **Trapezoid** — taper top vs bottom width.
- **Grid** — free-form envelope: select the component and **drag the control
  point handles in the Scene view**. The grid points are flat, named fields,
  so each one is keyframable in the Animation window.

### Live demo without an Animator

Add **`VMGDemoTextWarp`** on the same GameObject to breathe the warp between
flat and fully-warped each frame:

- `Mode` — which warp to animate (Wave / Arc / Circle / Trapezoid).
- `Max Amount` — peak distortion the sweep reaches.
- `Speed` — oscillations per second.

The component's two-level dirty gate notices the warp struct change and
re-meshes automatically.

## Animator integration

Public fields appear directly in the Animation window:

- `Warp.amount`, `Warp.secondary`, and the 36 grid points `Warp.p00 … p35`.
- `Stroke.width`, `Stroke.color`; `Fill.color`; `Tint`.
- `curveQuality` (bezier samples per glyph segment).

## Builds — font bytes & Bake

The renderer parses the font's `.ttf` outlines. A built player has no
`AssetDatabase`, and TMP font assets frequently report `sourceFontFile == null`
at runtime — so the source bytes are **embedded on the component** and re-parsed
at load.

- The editor **auto-caches** the bytes onto the component whenever the text
  rebuilds, so anything you've seen render in the editor ships correctly.
- Use the inspector's **Bake Font Bytes** button to embed them explicitly.
- A **build pre-process** step auto-bakes any vector-text component still
  missing bytes, so a build "just works" even if you never clicked Bake.

> Only TrueType (`.ttf`) outlines are supported in this version. A font asset
> backed only by CFF/OpenType-CFF (`.otf`) renders nothing and logs a warning.
