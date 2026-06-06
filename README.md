# VMG — Vector Motion Graphics Framework

Procedural vector motion graphics runtime for Unity. Built for AE-style shape-layer
expressiveness with first-class Unity Animator / Timeline integration on both UGUI
and world-space renderers.

## Features (0.9.0)

- Path + Node data model with cubic Bezier (`inTangent` / `outTangent` per node), tessellated upstream of every modifier
- Procedural CPU mesh generation
- Stroke
  - Inner / Center / Outer alignment
  - Cap: Butt / Square / Round
  - Join: Miter (with limit) / Bevel / Round
- Fill with self-contained ear-clipping triangulator (concave-safe)
- Modifiers (fixed order: Morph → RoundCorner → Trim)
  - Path Morph — blend toward a second `PrimitiveShapeSource`
  - Round Corner — real path-level geometry rounding with adjacent-corner clamping
  - Trim Path — start / end / offset with closed-path wrap support
- Primitives: Circle, Ellipse, Rectangle, Rounded Rectangle, Polygon, Free Path
- UGUI renderer (`VectorImageGraphic`) — `MaskableGraphic`, works with `Mask` / `RectMask2D`
- World renderer (`VectorSpriteRenderer`) — `MeshFilter` + `MeshRenderer`
- SVG import: drop a `.svg` into the project and the ScriptedImporter
  produces a `VMGShapeAsset` that either renderer can reference. Supports
  the full path `d` grammar, basic shape elements, viewBox, transforms,
  and fill/stroke styling.
- SceneView handles: drag-edit FreePath nodes and bezier tangents
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
```

The integration adds no hard dependency to the core package — projects
without DOTween are unaffected. See `Samples~/TweenIntegration/README.md`
for the full surface.

## Animation support

VMG's design goal is "every parameter you can edit in the inspector you
can also keyframe from `AnimationClip` / Timeline". Both renderers mark
themselves dirty every frame (`LateUpdate` on UGUI, `Update` on world)
so Animator-driven writes always re-tessellate the mesh.

### Keyframable from a standard AnimationClip

Every inspector field is exposed as a struct member so the Animation
window's "Add Property" tree walks into it. The full surface:

- **Procedural shape** — `kind`, `center.x/y`, `size.x/y`, `sides`,
  `cornerRadius`, `circleSegments`, `bezierSamplesPerSegment`,
  `freeClosed`, `activeNodeCount`
- **FreePath nodes** — per-slot `m_Node00.position.x/y`,
  `m_Node00.inTangent.x/y`, `m_Node00.outTangent.x/y`, `m_Node00.type`
  ... up to `m_Node63`. Bind them in the Animation window or just drag
  handles in the SceneView while Record is on — each drag becomes a
  keyframe at the playhead.
- **Stroke** — `enabled`, `color.rgba`, `width`, `alignment`, `cap`,
  `join`, `miterLimit`
- **Fill** — `enabled`, `color.rgba`
- **Modifiers** — every `[SerializeField]` field on `PathMorphModifier`,
  `RoundCornerModifier`, `TrimPathModifier` (including their `enabled`
  flag, so a modifier can be toggled on/off mid-clip)
- **Morph target** — the morph modifier wraps another
  `PrimitiveShapeSource`, so `m_Morph.target.size.x`, `m_Morph.target.m_Node00.position.x`,
  etc. are individually keyframable
- **UGUI renderer** — `FitToRect`, `Graphic.color`
- **World renderer** — `Tint`, `SvgUnitsPerWorldUnit`, `SortingLayerID`,
  `SortingOrder`

### FreePath node animation

Edit nodes normally — the SceneView handles route every drag through
`SerializedProperty`, so Animation window's Record mode auto-captures
each drag as a keyframe at the playhead. No sync step, no parallel
surface.

When both the base shape and a `PathMorphModifier.target` are
FreePaths, an "Active Shape" overlay appears in the upper-left of the
SceneView. Pick **Base Shape** or **Morph Target** to decide which
set of nodes the handles operate on — base/target nodes would
otherwise visually overlap.

The only Unity AnimationClip limitation that bites:

- **Node count (`activeNodeCount`) is keyframable**, but new slots
  appearing mid-clip use whatever data is sitting in those previously
  unused slot fields — they don't blend in from the previous frame's
  visible nodes. For shape transitions where the visual node count
  needs to grow smoothly (triangle → pentagon), animate
  `PathMorphModifier.progress` against a second shape instead. The
  modifier arc-length-resamples both paths to a common vertex count
  before lerping.

### NOT keyframable from a standard AnimationClip

| Field | Why | Workaround |
|---|---|---|
| `Material`, `Texture`, `SvgAsset` (any Object reference) | AnimationClip's Object track is PPtr-only and not exposed for these slots | Swap via script (`AnimationEvent` callback, or Timeline `Signal`) |
| FreePath node reorder | The slot index is the keyframe channel; renaming/reordering would break bindings | Use end-only add/remove (the +/- buttons in the inspector) |

## Roadmap

Deferred work is tracked in [BACKLOG.md](BACKLOG.md). Next up: DOTween / UniTask
interop (`DOFade`, `DOColor`, `DOSize`, `DOTrim`, …) shipped as an optional
assembly that compiles only when DOTween is present.
