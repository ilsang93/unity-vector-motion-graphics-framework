# VMG — Vector Motion Graphics Framework

[English](README.md) · [한국어](README.ko.md)

Procedural vector motion graphics runtime for Unity. Built for AE-style shape-layer
expressiveness with first-class Unity Animator / Timeline integration on both UGUI
and world-space renderers.

## Features (0.26.0)

- Path + Node data model with cubic Bezier (`inTangent` / `outTangent` per node), tessellated upstream of every modifier
- Procedural CPU mesh generation
- **ShapeStack** — up to 4 primitive shapes blended with arc-length resampling
  and per-slot intensity weights. Replaces the old "single shape + Morph
  modifier" pair; every slot is symmetric.
- Stroke
  - Inner / Center / Outer alignment
  - Cap: Butt / Square / Round
  - Join: Miter (with limit) / Bevel / Round
- Fill with self-contained ear-clipping triangulator (concave-safe)
- **Depth (3D extrusion)** — `VectorSpriteRenderer` only. Extrudes the
  fill along Z with Front / Center / Back pivot alignment. Vertex
  normals are emitted so a lit material shades the sides. Requires a
  3D URP renderer (Forward / Forward+ / Deferred) and an **Opaque**
  surface material — the 2D Renderer and Transparent surfaces will
  not light or occlude correctly.
- Modifiers (fixed order: RoundCorner → Trim)
  - Round Corner — real path-level geometry rounding with adjacent-corner clamping
  - Trim Path — start / end / offset with closed-path wrap support and
    open-path safe clamp (no flicker when offset crosses the end)
- Primitives: Circle, Ellipse, Rectangle, Rounded Rectangle, Polygon, Free Path
- UGUI renderer (`VectorImageGraphic`) — `MaskableGraphic`, works with `Mask` / `RectMask2D`
- World renderer (`VectorSpriteRenderer`) — `MeshFilter` + `MeshRenderer`
- SVG import: drop a `.svg` into the project and the ScriptedImporter
  produces a `VMGShapeAsset` that either renderer can reference. Supports
  the full path `d` grammar, basic shape elements, viewBox, transforms,
  and fill/stroke styling.
- SceneView handles: drag-edit FreePath nodes and bezier tangents on the
  active stack slot. A small overlay in the upper-left of the SceneView
  picks which slot the handles target.
- Custom shader material and texture per renderer
  - UGUI: `Material` slot + `Texture` (bound via `Graphic.mainTexture`, no material instancing)
  - World: `Material` slot + `Texture` (bound via `MaterialPropertyBlock`, shared material preserved)
  - Mesh UVs are bounds-normalised to `[0,1]` over the renderer's footprint
- Sorting Layer / Order in Layer fields on `VectorSpriteRenderer` (mirrors `SpriteRenderer` ergonomics)
- All animatable parameters exposed as `[SerializeField]` for AnimationClip / Timeline keyframing
- Editor menu entries:
  - `GameObject ▸ UI ▸ Vector Image`
  - `GameObject ▸ 2D Object ▸ Vector Sprite Renderer`

## Install

Add to `Packages/manifest.json`:

```json
"com.ilsang.vmg": "https://github.com/ilsang93/unity-vector-motion-graphics-framework.git"
```

Or as a local/embedded package by copying into `Packages/com.ilsang.vmg`.

## Samples

Import via Package Manager ▸ VMG ▸ Samples ▸ Basic Shapes (or Tween
Integration if DOTween is set up).

## DOTween / UniTask interop

When DOTween is present in the project (and a `VMG_DOTWEEN` scripting
define symbol is set — auto-defined for UPM installs), an optional
assembly exposes shorthand extensions:

```csharp
using VMG.Tween;

vectorImage.DOFade(0f, 0.4f);
vectorImage.DOTrim(1f, 0.8f).SetEase(Ease.OutCubic);
vectorImage.DOStrokeColor(Color.red, 0.5f);
await vectorImage.DOSize(new Vector2(300, 300), 0.6f).AsyncWaitForCompletion();

// Cross-fade between two shapes:
vectorImage.DOSlotIntensity(1, 1f, 0.8f);    // bring slot 1 in
vectorImage.DOSlotIntensity(0, 0f, 0.8f);    // fade slot 0 out
```

The integration adds no hard dependency to the core package — projects
without DOTween are unaffected. See `Samples~/TweenIntegration/README.md`
for the full surface.

## Standalone animation (VMGAnimator)

Beyond Unity's AnimationClip / Timeline path below, VMG ships its own
self-contained animator that doesn't require `PlayableDirector` or
Unity Timeline. Three authoring surfaces, all driving the same engine:

- **`VMGAnimationClip` + VMGAnimator** — ScriptableObject clip
  asset, edited in a dedicated timeline window. Per-track keys with
  ease, multi-target, events, baseline restore.
- **Code API (anime.js-style fluent builders)** —
  `VMGFx.Animate(target).To(...).Duration(...).Ease(...).Play()`,
  `VMGFx.Timeline()` for sequencing with relative positions
  (`"+=0.2"`, `"<"`, `"-=F"`), `VMGFx.Stagger(targets, ...)` for
  per-target offset, spring / motion-path / function-value channels.
- **`.vmgfx` DSL** — plain-text script (`add`, `animate`, `timeline`,
  `keyframes`, `stagger`, …) that compiles to the same engine.
  Assign a `.vmgfx` (or any TextAsset) to `VMGAnimator.script` and
  the hierarchy builds on enable. Optional `playOnEnable` /
  `loopScript` toggles for one-shot vs. infinite playback.

### CSS `@keyframes` importer

`VMG.Animation.Serialization.VMGCssKeyframes.Translate(css, out warnings)`
turns a self-contained CSS keyframe animation into `.vmgfx` text.
Designed for AE / Figma / Bodymovin CSS exports — `transform`,
`opacity`, color / border channels with W3C-spec cubic-bezier easing
mapping. Editor entry points:

- `Tools ▸ VMG ▸ Import CSS @keyframes…` — file dialog
- `Tools ▸ VMG ▸ CSS → VMGFx Window` — paste-in window

Out of scope by design: HTML companion input, CSS cascade, pseudo-class
state, per-element custom-property stagger. Trim wild demos to the
`@keyframes` core before importing; re-express element-level effects
via `VMGFx.Stagger` and timeline states.

## Animation support

VMG's design goal is "every parameter you can edit in the inspector you
can also keyframe from `AnimationClip` / Timeline". Both renderers mark
themselves dirty every frame (`LateUpdate` on UGUI, `Update` on world)
so Animator-driven writes always re-tessellate the mesh.

### Keyframable from a standard AnimationClip

Every inspector field is exposed as a struct member so the Animation
window's "Add Property" tree walks into it. The full surface:

- **ShapeStack** — `resampleCount`, plus four slots:
  - `Slot0..Slot3.intensity` — weight in the blend (0 = inactive)
  - `Slot0..Slot3.shape.*` — full PrimitiveShapeSource surface
- **Procedural shape (per slot)** — `kind`, `center.x/y`, `size.x/y`,
  `sides`, `cornerRadii.x/y`, `circleSegments`, `bezierSamplesPerSegment`,
  `freeClosed`, `activeNodeCount`
- **FreePath nodes (per slot)** — per-flat-slot `Node00.position.x/y`,
  `Node00.inTangent.x/y`, `Node00.outTangent.x/y`, `Node00.type`
  ... up to `Node63`. Bind them in the Animation window or just drag
  handles in the SceneView while Record is on — each drag becomes a
  keyframe at the playhead.
- **Stroke** — `enabled`, `color.rgba`, `width`, `alignment`, `cap`,
  `join`, `miterLimit`
- **Fill** — `enabled`, `color.rgba`
- **Modifiers** — every serialized field on `RoundCornerModifier`
  and `TrimPathModifier` (including their `enabled` flag, so a modifier
  can be toggled on/off mid-clip)
- **UGUI renderer** — `FitToRect`, `Graphic.color`
- **World renderer** — `Tint`, `SvgUnitsPerWorldUnit`, `SortingLayerID`,
  `SortingOrder`
- **Depth (world renderer only)** — `Depth.enabled`,
  `Depth.thickness`, `Depth.alignment`

### Multi-shape blending

The ShapeStack replaces the old PathMorphModifier:

1. Put your "from" shape in slot 0 (intensity 1).
2. Put your "to" shape in slot 1 (intensity 0).
3. Keyframe `Slot1.intensity` from 0 → 1 — the renderer
   arc-length-resamples both paths and lerps index-by-index.
4. Optionally fade slot 0 out in parallel so the result is the pure
   destination at the end of the clip.

All four slots are weighted equally; there's no "base" slot. Three or
four active slots produce a smooth N-way blend.

### FreePath node animation

Edit nodes normally — the SceneView handles route every drag through
`SerializedProperty`, so Animation window's Record mode auto-captures
each drag as a keyframe at the playhead. No sync step, no parallel
surface.

The overlay in the upper-left of the SceneView picks which stack slot's
nodes the handles operate on. Slots that aren't FreePaths show no
handles when selected (their `kind` field is what decides — Circle,
Rectangle, etc. don't have node handles).

The only Unity AnimationClip limitation that bites:

- **Node count (`activeNodeCount`) is keyframable**, but new slots
  appearing mid-clip use whatever data is sitting in those previously
  unused slot fields — they don't blend in from the previous frame's
  visible nodes. For shape transitions where the visual node count
  needs to grow smoothly (triangle → pentagon), put each shape in its
  own ShapeStack slot and animate the intensities instead.

### NOT keyframable from a standard AnimationClip

| Field | Why | Workaround |
|---|---|---|
| `Material`, `Texture`, `SvgAsset` (any Object reference) | AnimationClip's Object track is PPtr-only and not exposed for these slots | Swap via script (`AnimationEvent` callback, or Timeline `Signal`) |
| FreePath node reorder | The slot index is the keyframe channel; renaming/reordering would break bindings | Use end-only add/remove (the +/- buttons in the inspector) |
