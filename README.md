# VMG — Vector Motion Graphics Framework

[English](README.md) · [한국어](README.ko.md)

> Procedural **vector motion graphics** for Unity — After Effects-style shape
> layers (path / stroke / fill / trim / round-corner), SVG and TextMeshPro
> vectorization, stencil masking, and a self-contained animator — all rendered
> on **both UGUI and world-space** with zoom-independent anti-aliasing.

---

## ✨ Highlights

| | |
|---|---|
| 🟦 **Procedural shapes** | Circle, Rectangle, Polygon, Free Path… blended in a 4-slot **ShapeStack** |
| ✒️ **Stroke & Fill** | Alignment / cap / join, concave-safe fill, **two-stop gradients** |
| 🔠 **Vector Text (TMP)** | Render TextMeshPro as true glyph **outlines** + **WordArt warp** |
| 🖼️ **SVG import** | Drop a `.svg` → renderable `VMGShapeAsset` (paths, `defs`/`use`, styles) |
| ✂️ **Stencil masking** | Multi-source dynamic masks via `VMGMaskGroup` / `Source` / `Client` |
| 🌊 **Modifiers** | Round Corner, Trim Path, **AE-style Wiggle** |
| 🎞️ **Animator** | Keyframe everything from AnimationClip/Timeline, **or** the built-in `VMGAnimator` |
| 🧊 **World extras** | 3D **Depth** extrusion, **Billboard**, Sorting Layer/Order |
| 🪶 **Crisp at any zoom** | SDF-based edge anti-aliasing on both renderers |

---

## 📦 Install

Add to `Packages/manifest.json`:

```json
"com.ilsang.vmg": "https://github.com/ilsang93/unity-vector-motion-graphics-framework.git"
```

Or copy the package into `Packages/com.ilsang.vmg` for a local/embedded install.

**Requirements:** Unity **6000.3+**, uGUI (`com.unity.ugui`). TextMeshPro ships
inside uGUI on Unity 6, so no extra dependency is needed for Vector Text.

---

## 🚀 Quick start

Two renderers, same shape model:

| Renderer | Component | Create via |
|---|---|---|
| **UI (Canvas)** | `VectorImageGraphic` | `GameObject ▸ UI (Canvas) ▸ Vector Image` |
| **World (3D/2D)** | `VectorSpriteRenderer` | `GameObject ▸ 2D Object ▸ Vector Sprite Renderer` |

1. Create one of the above — you get a 160×160 vector circle.
2. In the inspector, set **ShapeStack ▸ Slot 0 ▸ Shape ▸ Kind** (Circle / Rectangle /
   Rounded Rectangle / Polygon / Free Path).
3. Toggle **Fill** and **Stroke** independently; try a `linear-gradient` fill.
4. Add a modifier (Trim / Round Corner / Wiggle) or keyframe any field — it just works.

---

## 🧩 Core features

### Shapes — ShapeStack

Up to **4 primitive shapes** blended together with arc-length resampling and
per-slot **intensity** weights (no separate "morph" modifier — every slot is
symmetric). Animate `SlotN.intensity` to morph between shapes.

- **Primitives:** Circle, Ellipse, Rectangle, Rounded Rectangle, Polygon, Free Path
- **Free Path:** cubic Bezier per node (`inTangent` / `outTangent`), drag-editable
  in the Scene view (handles target the active slot via a small overlay)

### Stroke & Fill

- **Stroke** — Inner / Center / Outer alignment · cap (Butt / Square / Round) ·
  join (Miter + limit / Bevel / Round)
- **Fill** — self-contained ear-clipping triangulator (concave-safe), multi-contour
  hole carving (even-odd)
- **Gradients** — two-stop **Linear / Radial** on fill *and* stroke, baked
  per-vertex on the CPU (fully keyframable), mapped across the shared fill+stroke bounds

### Modifiers  *(fixed order: Round Corner → Trim → Wiggle)*

- **Round Corner** — real path-level rounding with adjacent-corner clamping
- **Trim Path** — start / end / offset, closed-path wrap, flicker-free open-path clamp
- **Wiggle** — After Effects-style ripple *along the line* (arc-length resampled,
  spike-free), with intensity / frequency / spacing / seed

### Vector Text (TMP)

Render **TextMeshPro** text as true VMG vector **outlines** — fill, stroke
(테두리), thickness (두께), Wiggle, and WordArt warp. TMP is used purely as the
**layout engine** (`DontRender`); each glyph's shape comes from parsing the
font's TrueType (`.ttf`) outlines.

- `VMG ▸ Rendering ▸ Vector Text (UI, TMP)` — pairs with `TextMeshProUGUI`
- `VMG ▸ Rendering ▸ Vector Text World (TMP)` — pairs with world `TextMeshPro`
- **Warp (WordArt):** Arc · Circle · Trapezoid · Wave · **Grid** (drag control-point
  handles in the Scene view; every point is keyframable)
- **Build bake:** font bytes are embedded on the component and auto-baked at build
  time, so text renders in a player even when the TMP font has no source-file
  reference. *(TrueType only; CFF/`.otf` is unsupported.)*

### SVG import

Drop a `.svg` into the project — a ScriptedImporter produces a `VMGShapeAsset`
either renderer can reference. Supports the full path `d` grammar, basic shape
elements, `viewBox`, transforms, fill/stroke styling, **`<defs>`/`<use>`/`<symbol>`
inlining**, and **`<style>` class selectors**.

### Stencil masking

Dynamic, multi-source masks beyond Unity's single-graphic `Mask`:

- **`VMGMaskGroup`** — defines a mask region on a subtree
- **`VMGMaskSource`** — any graphic that *writes* the mask shape
- **`VMGMaskClient`** — graphics *revealed* through it

Multiple sources combine into one stencil channel (bit-slot pooled), and it
nests inside a standard `Mask`. Authorable from the DSL: `mask <name> { … }` +
`add … in=<maskName>`.

### Crisp edges (SDF anti-aliasing)

Both renderers emit a signed-distance channel so the `VMG/UI/VectorSDF` and
`VMG/World/VectorSDF` shaders fade a ~1px edge **independent of zoom** — vectors
stay clean whether scaled up or down.

---

## 🌍 World-renderer extras

- **Depth (3D extrusion)** — extrude the fill along Z (Front / Center / Back pivot)
  with real vertex normals for lit shading. *Needs a 3D URP renderer +
  **Opaque** material; the 2D Renderer / Transparent won't light or occlude correctly.*
- **Billboard** (`VMG ▸ Utility ▸ Billboard`) — face the camera or a target, with
  optional axis constraints and tilt offset.
- **Sorting** — `Sorting Layer` / `Order in Layer` fields, mirroring `SpriteRenderer`.

---

## 🎞️ Animation

### Keyframe from AnimationClip / Timeline

Design goal: **every inspector field is keyframable.** Both renderers mark
themselves dirty each frame (UGUI `LateUpdate`, World `Update`) so Animator
writes always re-tessellate.

Exposed channels include **ShapeStack** (`resampleCount`, per-slot `intensity`
and full shape surface), **FreePath nodes** (`Node00…Node63` position/tangents —
drag a handle while Recording to keyframe it), **Stroke / Fill** (incl. gradient),
**Modifiers** (incl. each `enabled` flag), and **Depth**.

**Morph between shapes:** put each shape in its own slot and keyframe the
intensities (0 ↔ 1). All four slots are weighted equally — no "base" slot.

> Not keyframable: `Material` / `Texture` / `SvgAsset` object references
> (swap via `AnimationEvent` or Timeline `Signal`), and FreePath node reorder
> (use the inspector +/- buttons).

### VMGAnimator — built-in, no Timeline dependency

A self-contained animator that does **not** require `PlayableDirector` / Unity
Timeline. Three authoring surfaces, one engine:

- **`VMGAnimationClip`** — ScriptableObject clip, edited in a dedicated timeline
  window: per-track keys + ease, multi-target, events, baseline restore,
  **track groups** spanning multiple GameObjects.
- **Code API (anime.js-style):**
  ```csharp
  VMGFx.Animate(target).To(...).Duration(0.4f).Ease(Ease.OutCubic).Play();
  VMGFx.Timeline().Add(a, "+=0.2").Add(b, "<");   // relative positions
  VMGFx.Stagger(targets, ...);                    // per-target offsets
  ```
  Plus spring, motion-path, and function-value channels.
- **`.vmgfx` DSL** — plain-text script (`add`, `animate`, `timeline`, `keyframes`,
  `stagger`, `mask`, …). Assign the asset to `VMGAnimator.script`; it builds on
  enable. `playOnEnable` / `loopScript` toggles included.

### CSS `@keyframes` importer

`VMGCssKeyframes.Translate(css, out warnings)` turns a self-contained CSS
keyframe animation into `.vmgfx` text — built for AE / Figma / Bodymovin exports
(`transform`, `opacity`, color/border, W3C cubic-bezier easing).

- `Tools ▸ VMG ▸ Import CSS @keyframes…` (file dialog)
- `Tools ▸ VMG ▸ CSS → VMGFx Window` (paste-in)

*Out of scope: HTML companion input, CSS cascade, pseudo-class state, per-element
custom-property stagger — trim to the `@keyframes` core, then re-express
element effects with `VMGFx.Stagger`.*

---

## 🔌 DOTween interop *(optional)*

When DOTween is present and `VMG_DOTWEEN` is defined (auto-set for UPM installs),
an optional assembly adds fluent shorthands — no hard dependency on the core:

```csharp
using VMG.Tween;

vectorImage.DOFade(0f, 0.4f);
vectorImage.DOTrim(1f, 0.8f).SetEase(Ease.OutCubic);
vectorImage.DOStrokeColor(Color.red, 0.5f);
vectorImage.DOSlotIntensity(1, 1f, 0.8f);   // cross-fade between shapes
```

---

## 📚 Samples

Import via **Package Manager ▸ VMG ▸ Samples**:

| Sample | Shows |
|---|---|
| **Basic Shapes** | Trim sweep, rounded rect, circle ⇄ rectangle morph |
| **Vector Text (TMP)** | TMP → vector outlines, Canvas + World, live warp demo |
| **SVG Import** | `.svg` icons through the ScriptedImporter |
| **Animator** | `.vmgfx` scripts driving `VMGAnimator` (no AnimationClip) |
| **Showcase** | Full DSL — stagger, spring/cubic-bezier ease, keyframes, events |
| **DOTween Integration** | `DOFade` / `DOTrim` / `DOSize` extensions (needs DOTween) |

---

## 📄 License & links

- **Repo:** <https://github.com/ilsang93/unity-vector-motion-graphics-framework>
- **Package id:** `com.ilsang.vmg` · **Namespaces:** `VMG.Core`, `VMG.UI`,
  `VMG.World`, `VMG.Svg`, `VMG.Text`, `VMG.Tween`
