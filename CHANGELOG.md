# Changelog

## [0.41.2] - 2026-06-17

### Fixed

- **Degenerate fill emits a visible band via the AA ring.** A
  polygon authored at zero area (e.g. a rectangle with size (W, 0))
  has no interior triangles, but `FillMeshBuilder` still emitted
  the outset AA ring around each edge — which on opposite sides of
  a zero-height rect produces a visible band several pixels wide
  (the ring width is 0.5% of the longer dimension). Surfaced via
  the mask-source workflow: a "zero-thickness line" used as a mask
  shape unexpectedly stamped a line-shaped region into the
  stencil. The fix skips the AA ring when the polygon's bounding
  box collapses below a sub-pixel threshold on either axis. The
  interior triangulation still runs (it's free and produces no
  pixels anyway) — only the ring is suppressed.

## [0.41.1] - 2026-06-17

### Fixed

- **`VMGMaskSource` stencil self-contradiction.** The source's ReadMask
  included its own bit, so the first pixel rendered for any source
  failed the `Comp=Equal` test (the bit it was about to write was not
  yet set in the buffer) — the source never stamped, the client
  never had a region to compare against, and the mask appeared
  inert. ReadMask now contains only the ancestor parent bits, which
  matches Unity's standard `Mask` pattern. Effect: VMG masks now
  work at the top level (parentBits=0 → ReadMask=0 → test always
  passes) and inside an ancestor standard Mask (ReadMask=parentBits
  clips to the ancestor region). Bit-isolation between simultaneous
  groups is unaffected because WriteMask is still the own bit.

## [0.41.0] - 2026-06-17

Stencil-based multi-source dynamic mask (W4). Several animated VMG
elements can collectively define a mask region whose union clips
other content. Each source preserves its own transform/animation, so
the visible region animates with them (the yarns-style wipe-reveal
use case). UI / Canvas only this round.

### Added

- **`VMGMaskGroup`** (`Runtime/UI/`) — marker on a GameObject that
  owns a stencil mask region. Allocates one bit from the upper
  nibble (128 / 64 / 32 / 16) at `OnEnable`, supporting up to 4
  simultaneously active groups. Caches the ambient standard-Mask
  depth so nesting under Unity's `Mask` clips the VMG region to the
  ancestor area; sufficiently deep nesting (parent depth ≥ 7) leaves
  no free bit and the group disables with an error.
- **`VMGMaskSource`** (`Runtime/UI/`) — `IMaterialModifier` on each
  child Graphic of a `VMGMaskGroup`. Stamps the group's bit with
  `StencilOp.Replace` + per-bit write mask so overlapping groups
  don't trample each other. Forces the host `VectorImageGraphic`'s
  `Fill` enabled so the mesh actually rasterises (the colour itself
  is suppressed via `ColorWriteMask=0` unless `ShowSource` is true,
  exposed for debugging the mask region).
- **`VMGMaskClient`** (`Runtime/UI/`) — `IMaterialModifier` on a
  Graphic that should render only where its parent `VMGMaskGroup`
  has been stamped AND every ancestor standard Mask region matches.
  `StencilOp.Keep` + `writeMask=0` so clients never modify the buffer.
- **VMGFx DSL `mask <name> { ... }`** — new top-level statement.
  Spawns a container GameObject + attaches `VMGMaskGroup`, and every
  `add` inside the block becomes a mask source (`VMGMaskSource`
  auto-attached).
- **VMGFx DSL `add ... in=<maskName>`** — when the named parent is a
  `VMGMaskGroup`, the added Graphic is auto-tagged with
  `VMGMaskClient`. The existing `in=` semantic for plain groups is
  unchanged.

### Notes

- The VMG mask uses upper-nibble stencil bits so Unity's standard
  `Mask` (depth IDs 1, 2, 4, 8) can coexist on the same canvas.
- World renderer (`VectorSpriteRenderer`) is not stencil-masked this
  round.
- Soft / alpha-gradient mask, inverse mask, and nested VMG mask groups
  are out of scope this round.

## [0.40.0] - 2026-06-16

HtmlCapture step 1 — reference render size. CSS animation demos
authored against a specific viewport (often 100vw responsive) now
preserve their authored pixel dimensions on the VMG side instead of
landing at whatever the capture default happened to be.

### Added

- **`Tools/HtmlCapture/capture.js --viewport WxH`** — external CLI
  flag selecting the reference viewport the demo should be captured
  at. Default `400x400` (unchanged). Required for responsive demos
  whose layout is driven by `vw/vh` percentages — capture them at the
  size the author designed for, otherwise child element positions
  scale unpredictably.
- **`Tools/HtmlCapture/<out>/reference.json`** — sidecar written
  alongside `frame_*.svg` / `frame_*.png` recording the captured
  viewport, totalMs, sample points, and source filename. Forms the
  base layer for future capture-pipeline metadata (keyframe stops,
  per-element transforms).
- **VMGFx DSL `add <name> svg asset=<id> referenceSize=auto`** — sizes
  the host RectTransform to the bound `VMGShapeAsset`'s `viewBoxSize`
  (which the SVG ScriptedImporter populates from the SVG's `viewBox`
  attribute). An explicit `size=W,H` on the same statement still
  wins. Recognized values: `auto`, `off`/`none`/`false` (no-op).

### Changed

- **`Samples~/HtmlCaptureWalkthrough/popup-ball.vmgfx`** —
  `svgBall` now uses `referenceSize=auto`. Comment explains how to
  recapture at the desired viewport to control imported size.

## [0.39.0] - 2026-06-16

`add svg asset=` DSL — VMGShapeAsset (already used by the
`SvgAsset` slot on `VectorImageGraphic` / `VectorSpriteRenderer`) now
spawns from VMGFx scripts the same way primitives do. Plus an
external HtmlCapture tool for mining CSS animation demos.

### Added

- **VMGFx DSL `add <name> svg asset=<id>`** — spawns a renderer with
  a `VMGShapeAsset` bound directly to its `SvgAsset` slot, bypassing
  the procedural ShapeStack pipeline. The `asset(name)` parens form
  is also accepted for consistency with `motionPath path=asset(...)`;
  a bare name is the shorthand.
- **`VMGFx.Svg()` factory** — code-API counterpart of the DSL form.
  Returns a `VMGSvgDescriptor` whose `.Asset(VMGShapeAsset)` chain
  method binds the asset.
- **`VMGSvgDescriptor`** (in `VMGShapeDescriptors.cs`) — base
  shape-descriptor extension whose `ApplyDescriptor` branch writes to
  `g.SvgAsset` / `r.SvgAsset` and skips ShapeStack entirely. Size /
  Position / Rotation from the descriptor still apply.
- **`Tools/HtmlCapture/`** — external Playwright snapshot tool
  (`capture.js`, ~210 LOC). Samples 7 progress points of any
  HTML+CSS animation by pausing animations and shifting
  `animation-delay`. Emits a PNG and a synthesized SVG (`<rect rx
  fill transform>`) at each point. Not part of the package — zero
  Node dependency in `com.ilsang.vmg`.
- **`Assets/Samples/HtmlCaptureWalkthrough/`** — walkthrough proving
  the end-to-end path on yui540's `popup-ball` demo (MIT). Includes
  the source HTML, the synthesized SVG, the generated
  `.vmgshape.asset`, the `.vmgfx` script driving both a primitive and
  the SVG-backed shape, and a reference PNG.

### Changed

- **`VMGAnimatorEditor`** — the `Assets` list on `VMGAnimator` is now
  drawn under the Script section (only when Script is set). It was
  previously hidden by the custom editor, which made the asset
  registry unusable. The list is also tooltipped to mention the
  `add svg asset=...` consumer alongside the existing
  `motionPath path=asset(...)` one.

## [0.38.0] - 2026-06-15

SVG render-path perf — natural follow-up to 0.36.0's renderer
dirty-flag. When a dirty rebuild does fire and the underlying
`VMGShapeAsset` is unchanged, the bezier-tessellated polyline is now
served from a cache on the asset itself instead of being re-built
from scratch. Visual output is bit-identical to 0.37.x.

### Added

- **`VMGShapeAsset.GetTessellation(subShapeIndex, bezSamples)`** —
  returns the cached bezier-tessellated polyline for one sub-shape
  at the given per-segment sample budget. Lazy-built on first call,
  shared across every `VectorImageGraphic` / `VectorSpriteRenderer`
  that references the asset. Caller must not mutate the returned
  `VectorPath` — `CopyFrom` it onto a renderer-owned path before
  applying transforms.
- **`VMGShapeAsset.ClearTessellationCache()`** — drops every cached
  polyline. Call after mutating `subShapes` in code so the next
  rebuild re-tessellates.

### Changed

- **`VectorImageGraphic.PopulateFromSvg` and
  `VectorSpriteRenderer.BuildFromSvg`** now go through
  `SvgAsset.GetTessellation(...)` instead of calling
  `BezierTessellator.Tessellate(...)` per rebuild. The cached path
  is copied onto the renderer's own `m_SvgPath` before the
  origin/scale transform is applied, so the cache stays untouched
  and re-usable across renderers and rebuilds.
- **`VectorImageGraphic.SetMeshDirty()` and
  `VectorSpriteRenderer.SetMeshDirty()`** now also call
  `SvgAsset.ClearTessellationCache()`, so any renderer sharing the
  asset re-tessellates on its next rebuild. This is the right
  behaviour because `SetMeshDirty` is documented as the hook for
  "mutated a VMGShapeAsset's internal data" — every viewer of that
  data needs to know.

### Notes

- The cache is `[NonSerialized]` — it goes away on domain reload
  and is never written to disk. SVG re-import via `SvgScripted
  Importer` also starts fresh because the importer creates a new
  `VMGShapeAsset` instance.
- Up to 4 distinct `bezSamples` values are cached in parallel via a
  small linear-probe array. In practice every renderer uses 16 (or
  the per-stack override), so only one slot is ever populated;
  the array exists so mixed-density scenes don't thrash.

## [0.37.1] - 2026-06-14

Patch follow-up to 0.37.0: `self` target alias the default new-script
template was already using, plus an advanced sample to balance out
AnimatorSample's two minimal scripts. No API changes.

### Fixed

- **`self` is accepted as a target / group alias in VMGFx scripts.**
  `VMGAnimatorEditor` seeds the default new-script template with
  `animate self localPosition.y -> 1`, but `VMGFxScript.ResolveTarget`
  only recognised `/`, the empty string, and `root` as the self-target.
  First-time users hit Play on the default template and silently saw
  nothing. Now `self` joins `root` / `/` / `""` everywhere, in both
  the standard target resolver and the `stagger group/*` group
  resolver. `pulse.vmgfx` reverts to `keyframes self`; AnimatorSample
  README lists `self` first with the others as aliases.

### Added

- **Showcase sample.** `Samples~/Showcase/showcase.vmgfx` is the
  advanced counterpart to AnimatorSample — a single 6-second looping
  composition that exercises the full DSL surface in one playable
  scene: `add` / `group`, `timeline` with labels and relative
  positions, `stagger` from `first` / `center` with multiple child
  statements in lockstep (`at=<<`), `spring` and `cubicBezier` eases,
  multi-stop `keyframes` on hero colour + satellite orbit, `random()`
  values, `Trim` and `RoundCorners` channels, and a `call` event
  hook. Registered as the "Showcase" package sample.

## [0.37.0] - 2026-06-14

Small Timeline-editor quality-of-life fix plus two sample
additions. No runtime API changes; safe drop-in over 0.36.0.

### Added

- **Ctrl+D / Cmd+D duplicates the selected keys in the VMG
  Timeline window.** Copies the current selection and pastes it
  immediately past the last selected key — start time is `tMax +
  oneSnapTick` (or `0.05s` when snap is off). Multi-track
  selections keep their relative layout via `VMGKeyClipboard`'s
  per-entry `relativeTrack` / `relativeTime`. The selection is
  replaced with the new copies so the user can keep tweaking
  without re-clicking. The right-click menu on a track also
  gains a matching "Duplicate N Key(s)" item. Routes through
  Unity's standard "Duplicate" command, so it inherits the OS
  modifier (Cmd on macOS) automatically.
- **BasicShapes sample gains a `VMGDemoShapeMorph` component.**
  Ping-pongs `Slot 0` ↔ `Slot 1` intensities on a `ShapeStack`
  to give a live circle ⇄ rectangle morph without an Animator.
  `Speed` controls cycles per second; `Ease` smoothstep blends
  the linear ping-pong toward a hold-and-snap feel. README adds
  a short "Live morph demo without an Animator" section.
- **AnimatorSample — new sample package.** Two `.vmgfx`
  scripts that show how to drive `VMGAnimator` from a text
  asset (no `AnimationClip`):
  - `pulse.vmgfx` — minimal `keyframes` block on `root`, ping-
    pongs `localScale` + `Fill.color` over one second.
  - `intro-card.vmgfx` — a 2-second logo intro that exercises
    `add` / `group` / `timeline` / `stagger` together with
    labels, relative positions (`at=headlineIn+0.2`), spring
    easing, and a `call introDone at=2` event hook.
  Registered in `package.json` under `samples` so users can
  import it from the Package Manager UI alongside Basic Shapes,
  DOTween Integration, and SVG Import.

### Changed

- **BasicShapes sample description in `package.json`** now
  mentions the new ShapeStack morph demo alongside the original
  trim circle / rounded rectangle / progress ring set.

## [0.36.0] - 2026-06-14

Renderer dirty-flag — both `VectorImageGraphic` (UGUI) and
`VectorSpriteRenderer` (World) used to rebuild the mesh every
frame regardless of state changes. The per-frame
`OnPopulateMesh` / `Rebuild` is now gated on a value-equality
check against a snapshot from the last rebuild; idle frames pay
only a handful of struct field compares instead of full
triangulation + mesh upload.

### Changed

- **`VectorImageGraphic` LateUpdate gates `SetVerticesDirty`.**
  Compares `ShapeStack` / `StrokeStyle` / `FillStyle` /
  `RoundCornerModifier` / `TrimPathModifier` / `Graphic.color` /
  `SvgAsset` ref / `Texture` ref against a snapshot captured at
  the end of `OnPopulateMesh`. When nothing changed, the call
  to `SetVerticesDirty` is skipped — UGUI's CanvasUpdateRegistry
  never queues the rebuild and the OnPopulateMesh /
  triangulation path is bypassed entirely. Animator-driven
  channel writes are detected automatically because Unity's
  channel writers mutate the [SerializeField] fields the
  comparison reads. **The gate is bypassed when `FitToRect` is
  on** — parent Canvas resizes, anchor changes, scaler updates
  and layout-group fixups can shift `rectTransform.rect`
  without an `OnRectTransformDimensionsChange` callback in the
  same frame, so a value-equality compare would miss those
  cases and the visual would drift out of fit. FitToRect mode
  rebuilds every frame as in 0.36.0-pre; the dirty-flag savings
  only apply to FitToRect=false (user-driven sizing).
- **`VectorSpriteRenderer` Update gates `Rebuild`.** Same
  comparison surface plus `DepthStyle` / `Tint` /
  `SvgUnitsPerWorldUnit`. `EnsureRefs` still runs every frame so
  material / texture / sorting edits stay live (those are
  independent of mesh content).
- **`SetMeshDirty()` public method** added to both renderers.
  Call it after mutating an external resource the dirty-flag
  cannot detect by value — typically a `SvgAsset`, a
  `VMGShapeAsset`'s internal data, or a FreePath's legacy node
  list. Plain field writes (animator channels, DOTween tweens,
  direct `g.Fill.color = ...`, inspector edits) are covered
  automatically and need no explicit call.
- New `VMG.Core.VectorRendererEquality` static helper exposes
  per-struct `Same(in T, in T)` comparisons for the package's
  mesh-input structs (`ShapeStack`, `PrimitiveShapeSource`,
  `ShapeSlot`, `StrokeStyle`, `FillStyle`, `DepthStyle`,
  `RoundCornerModifier`, `TrimPathModifier`, `FlatNode`).
  Hand-unrolled field comparison avoids the
  reflection-based default `ValueType.Equals`. The FreePath
  node compare stops at `activeNodeCount` and is skipped for
  non-FreePath shapes — a typical primitive renderer pays for
  ~10 field compares per frame, not 64 × 4 nodes.

### Known limitation

External-asset internal mutations (a `VMGShapeAsset`'s nodes,
the legacy FreePath `freeNodesLegacy` list, an SVG asset's path
data) are tracked by reference identity only. Call
`SetMeshDirty()` after mutating one of those in place. Plain
animator / inspector / DOTween writes need no such call.

## [0.35.0] - 2026-06-14

DSL friction round 1 #2 — stagger blocks now accept multiple
statements (and `keyframes`). Authoring a multi-channel stagger
no longer requires duplicating headers across two or three
identical `stagger` blocks.

### Changed

- **Stagger bodies can contain multiple animate / motionPath /
  keyframes statements.** Each statement runs in lockstep within
  a child — they share the same per-child offset computed from
  `step` / `from` / `seed`. Previously only the *last* statement
  was staggered (with a warning) and `keyframes` was rejected
  outright. Implemented as a per-child mini-timeline behind a new
  `VMGTimeline.Stagger<T>(targets, Func<T,int,int,VMGTimeline>,
  ...)` overload; each statement sequences inside that mini by
  appearance order, or use `at=<<` to start a statement together
  with the previous one for lockstep channels. The `i` / `n`
  token substitution that worked inside `animate` and
  `motionPath` now works inside `keyframes` too (block attrs,
  frame channel values, per-frame ease overrides). One-statement
  stagger bodies behave identically to before. Closes DSL
  friction item #2.

## [0.34.0] - 2026-06-14

DSL friction round 1 — three paper cuts surfaced authoring the
ShowcaseDemo against the UI Canvas. Rotation targets now route to
the Transform without an explicit `.transform` workaround,
`motionPath` works on UI elements, and `keyframes loop=<n>` inside
a `timeline { ... }` actually expands.

### Changed

- **Transform-reserved paths route to the Transform automatically.**
  `localPosition`, `localScale`, `localRotation`, and
  `localEulerAngles` (with any trailing `.x` / `.y` / `.z` / `.w`)
  resolve on the Transform — or RectTransform on a UI element — even
  when the target name was the renderer. Authors no longer need to
  spell `name.transform` for rotation. Mirrors the existing fallback
  for paths that don't compile on the renderer; this just makes the
  routing deterministic for the local* family instead of relying on
  member-resolution heuristics. Stagger's `it` target uses the same
  rule.
- **`motionPath` writes the right channel for the target type.**
  Was always world `transform.position`; now `anchoredPosition` on
  RectTransform (UI / Canvas), `localPosition` on a plain Transform,
  or the GameObject's appropriate transform field on a Component.
  `AutoRotate` similarly switches from world `eulerAngles.z` to
  `localEulerAngles.z`. The path is now interpreted as offsets in
  the parent's local space — anime.js parity, and the only
  interpretation that makes sense under a moved Canvas or parented
  hierarchy. `VMGTimeline.Remove(component)` still finds these
  tweens even though the writer is bound to the Transform.

### Fixed

- **`keyframes loop=<n>` inside a `timeline { ... }` now expands
  inline** instead of being silently re-routed onto the parent
  timeline. The block produces N back-to-back copies of all
  segments anchored within the block's slot; `alternate` reverses
  every odd cycle. Bare `loop` (infinite) inside a timeline is now
  a hard-error — the repetition span would be undefined when the
  parent doesn't loop.

## [0.33.0] - 2026-06-14

SVG importer round + Billboard component. The SVG parser learns
`<defs>` / `<use>` / `<symbol>` inlining and `<style>` class
selectors — Figma exports that reuse shapes via `<use>` or
style elements via CSS classes now import correctly. A drag-and-drop
"sidecar" workflow on renderer slots lets `.svg` files coexist with
Unity 6's built-in SVG importer instead of fighting it for the
extension. `VMGBillboard` ships as the package's first utility
component: a GameObject keeps facing the active camera (or a
Transform) under three rotation modes, with edit-mode preview.

### Added

- **SVG `<defs>` + `<use>` inlining.** The parser now resolves
  `<use href="#id">` against `<defs>` and `<symbol>` definitions,
  inheriting the use site's transform, style, and `x`/`y` offset.
  Forward references work because the document is loaded into a
  DOM before walking.
- **SVG `<symbol>` support.** Treated as a definition container
  whose children are instanced by `<use>`.
- **Cycle detection** for pathological `<use>` chains — broken with
  a `Debug.LogWarning` rather than a stack overflow.
- **SVG `<style>` class-selector matching.** A tiny CSS parser
  reads `.class { ... }` blocks (including `.a, .b` selector lists)
  from inline `<style>` elements. Properties resolved: fill,
  fill-opacity, stroke, stroke-opacity, stroke-width,
  stroke-linecap, stroke-linejoin, opacity. No specificity, no
  `!important`, no compound/descendant/pseudo selectors.
- **Style precedence** matches the SVG spec: inherited → class
  rule → presentation attribute → inline `style="..."` (later
  overrides earlier).
- **SVG sidecar workflow.** Drop a `.svg` file directly onto the
  `SvgAsset` slot of `VectorImageGraphic` or `VectorSpriteRenderer`
  — a sibling `<name>.vmgshape.asset` is generated automatically
  and assigned. Editing the `.svg` keeps the sidecar in sync via
  an `AssetPostprocessor`. Coexists with Unity 6's built-in
  `SVGImporter` (which keeps the `.svg` extension for Sprite /
  VectorImage usage) instead of fighting it.
- **`VMGBillboard` component** (`VMG/Billboard` menu) in the new
  `Runtime/Utility/` folder. Single component, nullable slots
  determine mode: `TargetTransform` set → follow Transform; else
  `TargetCamera` → that camera; else auto (Camera.main →
  SceneView camera in edit mode → any active camera).
  - Three rotation modes: `Full` (screen-aligned, default),
    `YAxis` (signboard — Y-only rotation), `ZAxis` (2D marker —
    Z-only rotation).
  - `FaceAxis` enum (Z+/Z-/Y+/Y-/X+/X-) for meshes whose front
    isn't +Z.
  - `TiltOffset: Vector3` for keyframable wobble on top of
    alignment.
  - `[ExecuteAlways]` + `OnRenderObject` re-apply for live
    SceneView preview during edit mode.
  - One-shot `Debug.LogWarning` when the parent chain has
    non-uniform scale (alignment would shear).

### Changed

- **SVG parser internals rewritten** from `XmlReader` streaming to
  `XmlDocument` DOM. Required for forward `<use>` references and
  the `<style>` pre-pass. Public `SvgDocumentParser.Parse(string)
  → VMGShapeAsset` signature unchanged.
- **`SvgScriptedImporter` version 3 → 5** to invalidate any cached
  `.svg` imports from the old parser.
- `IgnoreWhitespace` flipped `true → false` on the XML reader
  settings so `<style>` text content survives the DOM load.

## [0.32.0] - 2026-06-14

Track Groups round. User-defined composition groups arrive in the
Timeline window so authors can bundle tracks from multiple
GameObjects into one logical block (e.g. "Hero entrance" containing
character + camera + UI tracks). Track area also gets vertical
scrolling so long track lists fit inside the window. JSON clip
format bumps to "2"; "1" still imports.

### Added

- **User-defined track groups.** `VMGAnimationClip.userGroups` —
  list of `VMGTrackGroup { id, name }` entries. Each track carries
  a `groupId` (0 = ungrouped).
- **Three-level Timeline tree.** UserGroup header → auto (GO +
  component) subgroup header → track row. Per-depth indent. Same
  GameObject/component under two different user groups shows as
  two independent auto subgroups with independent collapse states.
  Empty user groups render their header so authors can create the
  group first, then assign tracks.
- **`+ Add Group` button** next to `+ Add Track` (creates an empty
  named group via the new name-input popup).
- **Right-click menus** for groups:
  - Track row → `Assign to group >` submenu lists existing groups,
    `(No group)`, and `New group...`.
  - User group header → Rename / Delete group (keep tracks) /
    Delete group and tracks.
  - Auto subgroup header → `New group from these tracks...`
    (one-shot: creates a user group and assigns every matching
    `(GO, component)` track).
- **Row-drag reorder.** Drag a user group or auto subgroup header
  to a new slot; a yellow indicator shows the drop position.
  Auto subgroups can also move into / out of user groups (track
  `groupId` is re-stamped). Click without movement still toggles
  collapse. Track rows themselves are not draggable yet.
- **Vertical track scroll.** Long track lists now scroll inside the
  Timeline window. Ruler / events row / playhead / horizontal
  scrollbar / Add bar stay sticky. Plain wheel over the track area
  scrolls vertically; Alt / Ctrl / Cmd + wheel zooms (zoom is
  still the default behavior over the ruler).

### Changed

- **JSON clip format `formatVersion` "1" → "2"**. New `userGroups`
  array and per-track `groupId` field. Importing format "1" data
  still works (groupId defaults to 0, userGroups stays empty).
- Collapse-state keys for auto subgroups now include the parent
  user group id so the same `(GO, component)` under different
  user groups can be collapsed independently.

### Notes

- DSL (`.vmgfx`) deliberately gets no group syntax this round.
  Groups are a clip-editing concept; today's DSL builds
  `VMGFx.Timeline` / `VMGFx.Animate` runtime objects (no group
  notion). A DSL → `VMGAnimationClip` compile path is the
  prerequisite, not in scope here.
- Track row reorder is deferred. Cross-subgroup track moves would
  break binding invariants; an intra-subgroup drag is queued for
  a future polish round.

## [0.31.0] - 2026-06-14

VectorSpriteRenderer World-renderer defaults round. New world-space
renderers (AddComponent, inspector Reset, prefab-new) now land at a
1m / thin-stroke baseline that fits a standard camera's view, instead
of inheriting the UGUI pixel-scale defaults (100px shape, 4px stroke
that rendered as 100m / 4m in world space).

### Changed

- **`VectorSpriteRenderer` field initializers use new World defaults.**
  `ShapeStack.WorldDefault()` seeds every slot's primitive at 1m
  (still circles); `StrokeStyle.WorldDefault` uses 0.04 stroke width
  instead of 4. UGUI `VectorImageGraphic` is untouched — pixel domain
  keeps 100px defaults so 160×160 RectTransforms still read as
  expected.
- **`GameObject > 2D Object > Vector Sprite Renderer` menu** no longer
  manually overrides slot sizes / stroke width — the renderer's own
  defaults now match what the menu was hand-patching. Single source
  of truth.

### Why this matters

The 0.11.0 fix patched only the menu factory entry point. Every other
path — `Add Component`, prefab instantiation, code-driven
`AddComponent<VectorSpriteRenderer>()`, inspector Reset — still
inherited the pixel-friendly 100×100 default and rendered at 100m.
This round closes those entry points by moving the World-vs-UI
default split into the type-level field initializers, so all paths
behave consistently.

### Added

- `PrimitiveShapeSource.WorldDefault()` — factory returning a 1m
  primitive (Normalize + size override).
- `ShapeStack.WorldDefault()` — analog of `Default()` using
  `PrimitiveShapeSource.WorldDefault()` for every slot.
- `StrokeStyle.WorldDefault` — `Default` with width 0.04 instead of 4.

## [0.28.0] - 2026-06-13

Timeline editor UX polish round. Visual language aligned with
Unity's Animation window for transfer of muscle memory, autoFit
removed in favor of a visible-window model that always permits
extending past the current end, group rows folded over the GO +
component dimension, and a first-run empty-state affordance to
remove the "what now?" dead-end. Self-channels on the VMGAnimator
itself are now blocked at the channel builder so they can't be
recorded or keyed by accident.

### Added

- **Diamond key glyph + Unity-style palette.** Keys render as
  diamonds with a 1px outline; white = idle, blue = selected, yellow
  = recording. White hover halo follows the cursor (matches Unity
  Animation's hover convention).
- **Banded track rows + row hover highlight.** Trivially improves
  readability when the clip has more than a handful of tracks.
- **Ruler frame / seconds toggle.** Default unit is frames, derived
  from `VMGAnimationClip.snapDivisor` (60 → 60fps). A small `f` / `s`
  button in the ruler gutter switches modes; choice persists per-
  window via EditorPrefs. snapDivisor = 0 forces seconds. Tick
  density follows zoom; minor ticks auto-hide when too dense.
- **Group rows.** Tracks fold under group headers keyed by
  `gameObjectPath + componentTypeName`. Headers carry the GO path
  and short component name, draw bold on a darker band, take a ▶/▼
  caret, and show half-sized summary diamonds for the hidden child
  keys while collapsed. Collapse state is per-clip and lives in
  memory only (re-opening the window starts everything expanded).
  `self` GameObject groups label as `<self>`.
- **Right-click menus regrouped.** Track context menu now reads
  Add → Clipboard (Copy / Paste / Delete) → Track, separators between
  sections.
- **Visible-window with headroom.** When zoomed out past Fit, the
  ruler/grid/snap-grid extend past `duration` into a dimmed
  "headroom" band. Dragging a key into the headroom extends
  `duration` automatically on MouseUp. Fit zoom still targets the
  exact clip duration. Single-key drag clamps at the visible-window
  end; multi-key drag is delta-based and unclamped (Unity Animation
  parity).
- **Empty-state Create buttons.** When both `script` and `clip`
  slots are empty, the inspector shows two buttons:
  `Create new VMGFx…` writes a starter `.vmgfx` (3-line comment + one
  valid `animate` statement) and assigns it; `Create new Clip…`
  creates an empty `VMGAnimationClip` asset and assigns it. Both
  use `SaveFilePanel` and validate the path lives under Assets/
  or Packages/.

### Changed

- **`VMGAnimationClip.duration` is always derived.** The
  `autoFitDuration` toggle is gone — duration tracks the latest key
  or event time exactly, with no minimum floor (sub-second clips are
  representable). Empty clips fall back to `EmptyClipDuration = 1f`
  so the timeline view doesn't collapse before the first key. The
  inspector shows Duration as a read-only field; extending the clip
  is done by dragging the last key further right (zoom out first if
  there's no headroom).
- **Inspector Timeline embed removed.** The full timeline now lives
  exclusively in `VMGTimelineWindow`. The animator inspector shows
  an "Open Timeline Window" / "Focus Window" button and the
  selected-key editor (`VMGTrackKeyEditor`), nothing else. Removing
  the embed eliminates the duplicated state path that tore down
  mid-interaction when inspector focus changed.
- **`VMGAnimationClip.MinAutoDuration` renamed to
  `EmptyClipDuration`** and only applies when no keys or events
  exist.
- **`VMGAnimationClip.RecalculateDurationIfAuto` renamed to
  `RecalculateDuration`** and is unconditional. All editor mutation
  paths already called the previous method, so callers update
  one-for-one.

### Removed

- **`VMGAnimationClip.autoFitDuration` field** (and its serialized
  DTO counterpart). 1.0 hasn't shipped yet, so the schema break is
  taken now rather than carrying a vestigial flag. Existing JSON
  containing the field is ignored by Unity's JsonUtility.

### Fixed

- **VMGAnimator's own serialized fields are no longer offered as
  animation channels.** `VMGChannelTreeBuilder` skips
  `VMGAnimator` MonoBehaviour instances, so the channel picker and
  the editor recorder both stop exposing `progress`, `speed`, mode
  flags, etc. Recording while moving the inspector's Progress slider
  no longer creates self-feedback keys.

### Internal

- New `Row` flatten structure inside `VMGTimelineView`; HitTest,
  hover, and rubber-band selection all route through
  flattened-row ↔ trackIdx mapping. Group headers are excluded from
  lasso selection by construction.
- `VMGTimelineWindow` sets `wantsMouseMove = true` and repaints on
  MouseMove so the key hover halo refreshes while the cursor is
  parked on a key.

## [0.27.0] - 2026-06-13

Code-API parity with the DSL's `keyframes` block. The last asymmetry
between `.vmgfx` scripts and the C# fluent builder closes with
`VMGAnimate.Keyframes(...)`, completing the anime.js port at the
code-API surface (Finding #2 from the 2026-06-13 usability review).

### Added

- **`VMGAnimate.Keyframes(path, ...)`.** CSS / anime.js-style multi-
  stop animations on a single channel. Times are normalized to
  `[0, 1]` across `.Duration()`; adjacent stops become FromTo
  segments. Seven typed `params (float, T)[]` overloads plus seven
  `VMGKeyframe<T>[]` overloads (the latter carry per-segment `Ease`
  overrides). Call multiple times with different paths to animate
  several channels in lock-step inside one animation.
- **`VMGKeyframe<T>` struct.** `(time, value)` or
  `(time, value, ease)` — `ease` follows the anime.js convention
  ("ease applies to the segment ENDING at this frame"). The
  animation-level `.Ease()` fills in any segment that doesn't carry
  its own override, so a later `.Ease()` call still reaches plain
  segments.

### Internal

- `VMGCodeTween` gains `hasExplicitSegment` and `hasExplicitEase`
  flags. `VMGAnimate.EnsureFinalized` scales normalized
  `[startTime, endTime]` into seconds for keyframe segments instead
  of clobbering them with the default `[0, dur]` window. Non-
  keyframe tweens are unchanged.

## [0.26.0] - 2026-06-13

CSS importer end-to-end integration round. The 0.25.0 translator now
has working editor entry points, a `.vmgfx` ScriptedImporter so the
output drops into `VMGAnimator.script` directly, and the script slot
plus auto-play / loop knobs that were missing from the animator
inspector.

### Added

- **`VmgFxScriptedImporter`.** `.vmgfx` files import as TextAsset and
  are droppable into `VMGAnimator.script`. Importer stays dumb —
  compilation still happens at `VMGAnimator.OnEnable` via
  `VMGFxScript.Compile`, so the runtime compiler remains the single
  source of grammar truth.
- **Editor menu entry points for the CSS importer.**
  - `Tools ▸ VMG ▸ Import CSS @keyframes…` — file dialog, writes
    `.vmgfx` next to the source.
  - `Tools ▸ VMG ▸ CSS → VMGFx Window` — paste-in window with
    Translate / Save / Copy / Clear and inline warnings.
- **`VMGAnimator.playOnEnable`** (bool, default false). At runtime
  OnEnable, if set + Internal mode, calls `Play()`. No effect in Edit
  mode or External play mode. Inspector control in the Playback
  section.
- **`VMGAnimator.loopScript`** (bool, default false). Wraps progress
  1→0 in Internal + script mode. Mirrors `VMGAnimationClip.loop` for
  clip mode. The animator drives a script via normalized progress and
  stops at 1, so a `loop` keyword inside a `.vmgfx` timeline was
  effectively inert for the playhead; `loopScript` is the working
  knob.
- **CSS importer: `var(--name, fallback)` resolution.** Transform
  function arguments now resolve a `var()` with a fallback to the
  fallback value before parsing — `scale(var(--s, 1.1))` becomes
  `scale(1.1)`. A fallback-less `var()` is left raw so the downstream
  parser warns honestly rather than silently dropping the function.

### Fixed

- **`VMGAnimator` Inspector did not expose the `script` field.** The
  field had existed since round-6 script-mode but the CustomEditor
  only drew Clip / Playback / Status / Timeline sections. Inspector
  now draws a Script section above Clip; the Clip section disables
  (read-only) when a script is assigned, matching the runtime
  priority.

### Scope decision documented

- **CSS importer is for self-contained `@keyframes`** from AE / Figma /
  Webflow-style exports. HTML companion input is permanently out of
  scope — building a mini-browser (cascade, selector engine,
  per-element scoped vars) is not the importer's purpose. Wild CodePen
  demos with HTML-tree-dependent selectors or per-element CSS-variable
  stagger should be trimmed to their `@keyframes` core; element-level
  effects re-expressed via `VMGFx.Stagger` and timeline states.
  `:root` global-token resolution is a possible future addition;
  per-element scoped vars are not.

### Known behaviour (not a bug)

- **Stroke does not scale with `transform.scale`.** Animating a vector
  shape's `localScale` to (0,0,1) shrinks the fill mesh to a point but
  leaves the stroke outline at its built-in width — stroke thickness
  is baked into the mesh, not a render-time multiplier. Workaround:
  turn the stroke off on shapes intended to scale to zero, or animate
  `Stroke.width` alongside scale.

## [0.25.0] - 2026-06-13

CSS `@keyframes` importer — translate browser-CSS keyframe animations
into `.vmgfx` script text the existing `VMGAnimator` / `VMGFxScript`
pipeline runs unchanged. Single-direction (CSS → VMG); the existing
internal-JSON round-trip remains the export path. The original AE
handoff motivation for an Authored-JSON track was superseded — AE and
CSS shape-keyframe animation are functionally equivalent at the
shape/keyframe level, so the CSS surface covers both.

### Added

- **`VMG.Animation.Serialization.VMGCssKeyframes.Translate(string css,
  out List<string> warnings)`** — parses a `@keyframes`/selector pair
  and returns `.vmgfx` text. The caller can paste the output into a
  `.txt` TextAsset on a `VMGAnimator`, save it as a `.vmgfx` for
  source control, or feed it back through `VMGFxScript.Compile`
  directly. Tokenizer + parser + emitter live in a single file.

### Supported CSS surface

- `@keyframes name { 0% / 50% / from / to / 100% { ... } }` with
  comma-separated frame selectors.
- `<selector> { animation: name dur timing delay iter direction; }`
  shorthand AND `animation-*` longhands. `,`-separated `animation`
  lists are honoured.
- Selectors: `.class` / `#id` / bare ident — all map to a GameObject
  of the same name under the VMGAnimator root. Compound selectors
  (`.a .b`, `div.box`) are warned + skipped.
- `transform`: `translate(x, y)` / `translateX/Y` / `scale(s)` /
  `scale(x, y)` / `scaleX/Y` / `rotate(Ndeg)` / `rotateZ`. Combinable
  pieces; aggregated into `localPosition` / `localScale` /
  `localEulerAngles.z` per-frame.
- `opacity` → `color.a` (Graphic tint alpha).
- `background-color` → `Fill.color`, `border-color` →
  `Stroke.color`, `border-width` → `Stroke.width`. `color` → tint.
- Colour literals: `#rgb` / `#rgba` / `#rrggbb` / `#rrggbbaa` / `rgb()` /
  `rgba()` / named CSS colours. HSL is warned (unsupported).
- Timing functions: `linear` / `ease` / `ease-in` / `ease-out` /
  `ease-in-out` / `step-start` / `step-end` / `cubic-bezier(a,b,c,d)` /
  `steps(N)`. CSS keywords map to the W3C-spec cubic-Bezier control
  points so visual fidelity matches a browser exactly. Per-keyframe
  `animation-timing-function` is honoured (CSS attaches it to the
  starting keyframe of a segment; the importer shifts to the
  end-of-segment convention VMGFx uses).
- Units: `px` / `deg` / `s` / `ms`. `rem`/`em` get a coarse 16-px
  conversion. `rad`/`turn`/`grad` for angles.
- `animation-direction`: `alternate` and `alternate-reverse` honoured;
  `reverse` warned (no native reverse-from-100% playback yet).

### Notes

- Per-channel keyframe blocks. Each animated property is emitted as
  its own `keyframes target { ... }` block so a per-keyframe timing
  function on one channel does not bleed across channels — channels
  redefined at different keyframes stay independent, matching CSS.
- One CSS frame can carry multiple declarations; the emitter splits
  by channel. The block-level `animation` shorthand fields
  (duration / delay / iter / direction / default timing) replicate
  identically on every per-channel block.
- Unsupported: HSL colour, CSS variables, `calc()`, `:hover`/`:active`,
  `transition`, `filter`, `transform-origin`, layout properties
  (`display`/`margin`/`padding`/`flex`/`grid`/`width`/`height`/…),
  fill-mode, play-state, 3D rotate (`rotateX`/`rotateY`), `matrix()`,
  `skew()`, `perspective()`. Most layout/typography properties are on
  a known-but-dropped allowlist (silently dropped without warnings);
  unknown identifiers produce a warning so typos surface.
- `Translate` returns the warning list out-param. The internal
  JSON serializer (`VMGAnimationClipSerializer`) is untouched —
  round-trip backup remains available.

## [0.24.0] - 2026-06-13

Code-API usability round B — single-tween handle parity with anime.js.
A bare `VMGFx.Animate(target).To(...).Duration(...)` now returns a
fully-featured handle: `Revert()` and the new `Cancel()` complete the
set already exposed for `Play / Pause / Stop / Seek / Restart / Reset
/ Complete / Reverse / Refresh / PlaybackRate / Completion`. No more
wrapping a single animate in a Timeline just to get a revertable
handle. Closes finding #1.

`Cancel()` is the new "abort current tween, keep current channel
value, start fresh from here" verb. Unlike `Revert()` (which snaps
channels back to their pre-animation baseline) and `Pause()` (which
leaves the handle resumable with the original from→to), `Cancel()`
ends the tween at its current value and clears tween flags so a
follow-up `.Animate(...)` recaptures from-side from where things
stopped. Use for toggle / re-press patterns where the old handle is
being discarded — `Revert` flashes the baseline during fast re-press,
`Pause` strands the old handle. Closes finding #3.

### Added

- **`VMGAnimate.Revert()` / `.Cancel()`.** Single-animate handle now
  exposes both stop-verbs. Mirrors the existing Timeline surface.
- **`VMGTimeline.Cancel()`.** Recurses into children in the same
  reverse-order pattern as `Revert`, but skips the baseline writes.
- **`VMGAnimation.Cancel()`.** Underlying engine method —
  `EndAndClear(restoreBaseline: false)`. `Revert()` is now the
  `restoreBaseline: true` counterpart over the same helper.

### Notes

- No API removals. `Stop()` semantics (Seek-to-start + pause) are
  unchanged — `Restart()` / `Reset()` still depend on it. `VMGAnimator`
  clip-mode is unaffected.
- DSL surface unchanged. The DSL runs through `VMGAnimator` which
  doesn't expose code-handles; the new verbs are for direct
  `VMGFx.Animate` / `VMGFx.Timeline` callers.
- No schema changes; no migration. Bump per the round's user-visible
  API addition.

## [0.23.0] - 2026-06-13

Code-API usability round A — schema break. Strips the `m_` SerializeField
prefix from every keyframable surface on `VectorImageGraphic` /
`VectorSpriteRenderer` / `ShapeStack` / `PrimitiveShapeSource`, removes the
redundant ref-returning property layer, and replaces the single-float
`cornerRadius` with a `Vector2 cornerRadii` so elliptical corners (CSS
`border-radius: Xpx / Ypx`) are reachable directly. AE-toggle-style
authoring code now reads as `.To("Fill.color", ...)` instead of
`.To("m_Fill.color", ...)`.

### BREAKING — channel paths and field names

- **SerializeField rename pass.** Every previously `m_*`-prefixed
  serialized field on the public surface drops the prefix and becomes a
  `public` field (Unity still serializes it). The same name is used
  everywhere — inspector label, channel path, and direct C# field access.
  AnimationClips, JSON clips, and code that referenced the old names
  break.

  | Renamer  | Before                       | After                     |
  | -------- | ---------------------------- | ------------------------- |
  | UGUI     | `m_Fill`, `m_Stroke`, `m_ShapeStack`, `m_RoundCorners`, `m_Trim`, `m_FitToRect`, `m_SvgAsset`, `m_Texture` | `Fill`, `Stroke`, `ShapeStack`, `RoundCorners`, `Trim`, `FitToRect`, `SvgAsset`, `Texture` |
  | World    | `m_Tint`, `m_Depth`, `m_Material`, `m_SortingLayerID`, `m_SortingOrder`, `m_SvgUnitsPerWorldUnit` (+ the UGUI set above) | `Tint`, `Depth`, `Material`, `SortingLayerID`, `SortingOrder`, `SvgUnitsPerWorldUnit` |
  | Stack    | `m_Slot0..m_Slot3`           | `Slot0..Slot3`            |
  | FreePath | `m_Node00..m_Node63`         | `Node00..Node63`          |

  No migration shim. Pre-1.0 per the package's stated migration policy
  — re-bind any clip that referenced the old paths, search-and-replace
  any C# / `.vmgfx` source for the new field names.

- **`ref`-returning properties removed.** `VectorImageGraphic.Fill`,
  `.Stroke`, `.ShapeStack`, `.RoundCornerModifier`, `.TrimModifier`,
  `.SvgAsset`, `.FitToRect`, `.Texture` (and the world-renderer
  equivalents) used to be `ref`-getters in front of the private
  SerializeField. They are now the field directly — `g.Trim.end = 0.5f`
  is unchanged at the call site but lands on the public field. Two
  property name changes for consistency: `RoundCornerModifier` →
  `RoundCorners`, `TrimModifier` → `Trim`. Setter side-effects
  (`SetVerticesDirty` / `Rebuild` / `ApplyTexture` / `ApplySorting`) are
  removed; the runtime path already runs them every frame in
  `LateUpdate` / `Update`, so external writes pick up on the next frame
  with no visible delay.

- **`cornerRadius : float` → `cornerRadii : Vector2`.** Per-axis radii
  let RoundedRectangle reproduce CSS `border-radius: Xpx / Ypx`
  elliptical corners. `BuildRoundedRect` now sweeps elliptical arcs
  (`AddEllipticalArc`) using `(rx, ry)`; the degenerate full-ellipse
  fallback now fires only when **both** radii reach the half-extent.
  Tween extensions: `DOCornerRadius(float)` is kept as a convenience
  (expands to `(r, r)`); `DOCornerRadii(Vector2)` is new. DSL: new
  `cornerRadii=X,Y` attribute on `add ... roundedRect`; the existing
  single-value `cornerRadius=` / `corner=` keys still work and expand
  to `(r, r)`.

### Added

- **`FitToRect` tooltip warns about silent slot-size overwrite.** New
  text on the field: "When true, ShapeStack slot center/size channels
  are overwritten every frame from the RectTransform — animate
  RectTransform.sizeDelta instead." Catches the footgun where users
  reach for `ShapeStack.Slot0.shape.size` first and find it has no
  visible effect. (Findings doc #5.)

### Compatibility

- AnimationClip `.asset` files and any exported VMG JSON clips
  referencing the old `m_*` paths will fail to bind. Re-bind manually.
- DSL scripts (`.txt` / TextAsset / inline strings) with literal
  `m_Fill.color` etc. need a one-time search-and-replace to the new
  names.
- C# code using `g.TrimModifier.X` or `g.RoundCornerModifier.X` needs to
  switch to `g.Trim.X` / `g.RoundCorners.X` (the field replaces the
  property; both `enabled` and the inner fields are reachable the same
  way).

## [0.22.0] - 2026-06-13

DSL parity round 2 — Stagger. Closes the group-3 "big four" for the
script-mode DSL: the code-API `tl.Stagger(targets, build, step, from,
seed)` now has a block statement counterpart. Anime.js parity for
"repeat N children with index variation" authoring inside `.vmgfx` /
TextAsset scripts.

### Added

- **`stagger` block statement.** Repeats its body once per direct child
  of a named group, distributing the children across time via
  `VMGTimeline.Stagger`. Legal both at top level (wraps itself in an
  implicit timeline) and inside a `timeline { ... }` block.

  ```
  stagger dots/* step=0.1 from=center seed=42 {
      animate it m_Fill.color -> red duration=0.5 ease=outQuad
      animate it.transform localScale.x -> random(0.6, 1.6, i)
          duration=0.4 ease=outBack
  }
  ```

- **Wildcard target `<group>/*`.** Resolves the named group via
  `root.Find(...)` (so nested `a/b/*` paths work) and collects each
  direct child's renderer Component (`VectorImageGraphic` /
  `VectorSpriteRenderer`), falling back to the Transform when the
  child has no renderer. Scene order is preserved.

- **Implicit `it` / `i` / `n` bindings inside a stagger block.**
  - `it` at the target position substitutes the current child
    component; `it.transform` substitutes its Transform.
  - `i` (current index, 0..n-1) and `n` (total count) at value
    positions substitute the numeric literal. Word-boundary aware,
    so `inOutQuad` / `linear` are not mangled.
  - Outside a stagger block, `it` / `i` / `n` remain ordinary
    identifiers (forward-compatible).

- **Stagger header attributes.** `step` (float, seconds — default
  0.1), `from` (`first` / `center` / `last` / `random` — maps to
  `VMGStaggerFrom`), `seed` (int — drives `from=random` ordering
  only), `at` (timeline position string — only meaningful inside an
  enclosing `timeline { ... }`).

- **Tokenizer `*` allowed in identifier body.** Required for the
  `dots/*` wildcard to tokenise as a single ident. `*` remains
  illegal at start, so existing punctuation behaviour is unchanged.

### Notes

- Only `animate` and `motionPath` statements are allowed inside the
  stagger body. `set` / `call` / `label` are not (per-child semantics
  are ambiguous).
- `seed` on the stagger header controls only `from=random` shuffle
  ordering. Per-child variation inside `random()` / `rangeInt()`
  generators is the body's responsibility — pass `i` (or `seed+i`,
  built lazily by the body) for varying per-child seeds.
- Multiple animate/motionPath statements in a single stagger block
  currently keep only the last one's tween (a warning is logged).
  Wrap each repeated tween in its own stagger block for now.

## [0.21.0] - 2026-06-13

DSL parity round 2 — FunctionValue. Third of the group-3 "big four"
closes: the script-mode DSL now accepts lazy generators at value
positions, mirroring the code-API `VMGAnimate.To(Func<T>) /
FromTo(Func<T>, Func<T>)` + `RefreshOnLoop` surface. Anime.js parity
for `random()` / `randomInt()` value expressions.

### Added

- **`random(min, max [, seed])` generator at value position.**
  Returns a continuous float in `[min, max]`. Recognised by the parser
  anywhere a numeric value is expected (`-> random(...)`,
  `from=random(...)`, `keyframes` `path=random(...)`). On Int channels
  the result is rounded to the nearest integer for convenience.

  ```
  animate dot.transform localPosition.x -> random(-200, 200)
    duration=1 ease=inOutQuad loop alternate refreshOnLoop
  ```

- **`rangeInt(min, max [, seed])` generator at value position.**
  Returns an integer in `[min, max]` inclusive on both ends (anime.js
  convention). Usable on Float channels too (returned as a float).

- **Optional `seed` argument (int) on both generators.** When supplied,
  a `System.Random(seed)` is captured per generator instance so the
  sequence is deterministic across runs — same seed produces the same
  values. Omitting the seed falls back to `UnityEngine.Random`
  (global, non-deterministic), matching anime.js's default behaviour.

- **`refreshOnLoop` animate attribute.** Re-evaluates every Func<T>
  tween value at each iteration boundary, so a `random(...)` value
  draws a fresh number every loop instead of freezing on its first
  roll. Bare `refreshOnLoop` (no value) means on; `=true/false`
  controls explicitly. Maps 1:1 onto
  `VMGAnimate.RefreshOnLoop(bool)`. Default is off (anime.js parity).

- **Tokenizer paren-aware whitespace.** Whitespace inside balanced
  `(...)` in a value position is now preserved/dropped without
  terminating the value. This means
  `random(-100, 100, 42)` and `cubicBezier(0.25, 0.1, 0.25, 1)` parse
  with reader-friendly spacing — previously you had to write them
  compact (`random(-100,100,42)`). Compact form still works
  unchanged. Newlines still terminate the value (no multi-line
  generator calls).

### Fixed

- **Bare-key flag attributes now parse.** `animate ... loop alternate`
  (and friends — `refreshOnLoop`, `autoRotate`, `closed`, `fitToRect`,
  `reversed`, `paused`) previously raised `expected '=' after
  attribute key 'loop'` despite the doc-comments and CHANGELOG
  examples showing the bare form. `ParseAttributes` now treats a key
  with no trailing `=` as an empty-valued flag, and the affected
  handlers go through a `ParseFlag` helper that maps empty → on,
  preserving `=true/false` overrides.

### Notes

- **Numeric channels only this round.** Float and Int leaf types use
  the new generators. Vector2/3/4 channels would need a tuple-shaped
  generator (`vec2(random(-1,1), random(-1,1))`) which expands the
  grammar — deferred to a later round if asked. Color channels likewise
  would need a `randomColor()` / per-channel form.
- **Seeded RNG is per generator instance**, not per script. Each
  `random(..., 42)` in the DSL creates its own `System.Random(42)`. If
  two channels share the same seed they advance independently.
  Sequential calls inside one generator (loop / refreshOnLoop) walk the
  same series.
- `VMGAnimator` unchanged — round rule preserved.
- Round 1 and round 2 (Spring + MotionPath) syntax 100% compatible.
  `random` / `rangeInt` / `refreshOnLoop` were previously unparseable;
  this is a strict superset.

## [0.20.0] - 2026-06-13

DSL parity round 2 — MotionPath. Second of the group-3 "big four"
closes: a new `motionPath` statement drives a target's
`transform.position` along an inline polyline, with optional
auto-rotation tangent tracking. Mirrors the code-API
`VMGAnimate.AlongPath(points, closed) + .AutoRotate(offsetDeg)` pair.

### Added

- **`motionPath <target> points=x1,y1,x2,y2,... [closed=true]
  [autoRotate[=offsetDeg]] [duration= ease= delay= endDelay= loop=
  alternate at=]` statement.** Top-level or inside a timeline.
  Reuses the existing comma-pair `points=` format from `add … path`,
  so AE-exported coordinates can be pasted between shape descriptors
  and motion paths without massaging.

  ```
  scene {
    add dot circle size=20 fill=#fff
  }
  timeline duration=2 {
    motionPath dot points=0,0,100,50,200,0 autoRotate=-90
  }
  ```

- **`autoRotate` accepts `true` / `false` / a numeric offset in
  degrees.** Bare `autoRotate` (no value) means "on, offset 0", same
  short form as the existing `loop` / `alternate` attrs. Anime.js
  parity: `createMotionPath({autoRotate: -90})`.

### Notes

- **Inline `points=` only this round.** Asset-mode binding
  (`asset=heart subShape=0` referencing a `VMGShapeAsset`) is
  deferred to a future round that defines DSL-wide asset
  registration. The code-API `.AlongPath(VMGShapeAsset, int)` is
  unchanged and still usable from C#.
- **Why a new statement instead of `animate dot transform.position
  -> 0,0 alongPath=...`?** `animate` grammar requires `<target>
  <fieldPath> -> <toValue>`; the `to` value has no meaning when a
  motion path is driving position. A dedicated `motionPath`
  statement keeps both grammars clean.
- `VMGAnimator` unchanged — round rule preserved.
- Round 1 syntax 100% compatible; `motionPath` is a new top-level
  keyword that previously errored as unknown, so this is a strict
  superset.

## [0.19.0] - 2026-06-13

DSL parity round 2 — Spring. First of the group-3 "big four" closes:
function-form `spring(...)` is now a recognised ease in the script-mode
DSL, matching `VMGEase.Spring` on the code API one-to-one.

### Added

- **`ease=spring(stiffness, damping, mass, velocity)` in DSL.** Slots
  into the existing `ResolveEase` function-form switch alongside
  `cubicBezier(...)` and `steps(N)`. Argument order matches the C# API
  (`VMGEase.Spring(stiffness=100, damping=10, mass=1, velocity=0)`) so
  DSL ↔ code translation is mechanical. 0..4 positional args, missing
  trailing args take the C# default — `spring(200)` is a stiffer
  spring with otherwise-default damping/mass/velocity.

  ```
  timeline duration=1 {
    animate dot scale -> 1.5 ease=spring(200, 12)
    animate dot opacity -> 1 ease=spring
  }
  ```

  Bare `spring` (no parens) continues to work via `VMGEase.From` and
  produces the all-defaults spring — unchanged from prior rounds.

### Notes

- Unparseable args fail the whole resolve to `Linear` with a warning,
  same pattern as `cubicBezier`. Whitespace inside parens
  (`spring( )`) collapses to the 0-arg form.
- `VMGAnimator` unchanged — round rule preserved.
- Round 1 syntax 100% compatible: function-form `spring(...)` was
  previously a `Linear` fallback, so this is a strict superset.

## [0.18.0] - 2026-06-13

DSL parity round 1 + CSS-compatible keyframes. The script-mode `.txt`
DSL has been frozen since round 6 while the code-API grew. This release
brings the most useful bits of that gap back to script authors, and
adds a native CSS-style `keyframes` block so AE-exported / CSS-authored
animation data can be hand-written or machine-converted with almost no
syntactic massaging.

### Added

- **Timeline header attrs.** `timeline duration=2 ease=outQuad
  playbackEase=inOutQuad rate=1.5 loop=3 alternate { ... }`. Maps to
  `VMGTimeline.Duration / Defaults(ease) / PlaybackEase / PlaybackRate /
  Loop / Alternate` 1:1. Replaces the previous timeline header which
  only allowed the bare `timeline { ... }` form.

- **`on <event> -> <eventName>` statement** inside a timeline.
  Subscribes the given script event name to a timeline lifecycle
  callback. Events: `begin / beforeUpdate / update / render / loop /
  complete / pause`. Dispatched through the same channel as `call`, so
  `VMGAnimator.scriptEvent` listeners hear both.

  ```
  timeline duration=2 {
    animate dot opacity -> 1 duration=1
    on complete -> doneAnimating
  }
  ```

- **`keyframes <target> { <pct>%: ... }` block.** CSS-style multi-
  keyframe animation in DSL. Compiles into a series of segment tweens
  added to the enclosing timeline (or an auto-wrapped one-off timeline
  if used at top level). Per-frame `ease=` override is supported,
  applying to the segment ending at that frame (CSS semantics). Accepts
  `from` / `to` aliases for `0%` / `100%`.

  ```
  keyframes box {
    0%:   pos=0,0    opacity=0
    50%:  pos=50,0   opacity=1   ease=inOutQuad
    100%: pos=100,0  opacity=0
  } duration=2 ease=outQuad loop=2 alternate
  ```

  Channels not redefined at an intermediate frame hold their last
  defined value (CSS semantics: no segment, no write).

- **Function-form ease in DSL.** `ease=cubicBezier(0.25,0.1,0.25,1)`
  for CSS-compatible curve specification, and `ease=steps(N)` (mapped to
  `Hold` for now — staircase ease is engine-level work). Applies
  everywhere DSL accepts an ease: `animate`, `timeline`, `keyframes`
  block, per-frame override.

- **`VMGTimeline.Defaults(VMGEase ease, ...)` overload.** Lets a
  constructed `VMGEase` (not just a preset enum) become the child
  default. Used by the DSL header to route any of preset name /
  cubicBezier / steps / spring uniformly into `Defaults(ease:)`.

### Notes

- **Combined position selectors already worked.** `at=*=2`, `at=<+=0.2`,
  `at=<<+=0.2`, `at=label+=0.5` all parse correctly through the
  existing value-mode tokenizer + `VMGAt.Parse`. No changes required;
  the DSL was carrying these for free already.

- **CSS/AE handoff.** The `keyframes` block is the explicit
  conversion-friendly surface for AE-exported keyframe data and
  CSS-authored `@keyframes`. Direct hand-conversion is mostly one-to-one
  except for:
  - CSS percent units → VMG accepts bare `<pct>%` or `from`/`to`.
  - CSS unit suffixes (`px`, `deg`) → strip; VMG values are raw numbers.
  - CSS `transform: translateX(N)` → split into per-channel
    `transform.position`, `transform.rotation`, etc.

- **Tokenizer additions.** `%` and `:` are now single-character symbol
  tokens (previously dropped with a warning). They appear nowhere in
  pre-0.18 DSL syntax, so no existing scripts are affected.

- **`steps(N)` is a stub.** Currently collapses to `Hold` regardless of
  `N`. A future round will extend `VMGEase` with a native staircase
  kind. The argument is accepted for forward compatibility — scripts
  written today will keep working when the real implementation lands.

- **VMGAnimator unchanged.** The clip-mode runtime path is untouched.
  Script-mode benefits from every new feature automatically because
  the DSL compiles down to the same `VMGFx.Animate` / `VMGFx.Timeline`
  call sites the code API uses.

## [0.17.0] - 2026-06-13

anime.js port group 2 #5: **Revert** — undo every channel an animation
or timeline has written to and restore the pre-animation values. Closes
the anime.js port at the code-API level. DSL parity round is the
natural next session (the script-mode surface has been frozen since
round 6 and now lags behind 12 code-API features across groups 1–3).

### Added

- **`VMGAnimation.Revert()`.** Snap-back (not animated) every channel
  the animation has written to since play started, then stop and reset
  the playhead. Distinct from `Reverse()` (which flips direction and
  keeps playing).

  ```csharp
  var anim = VMGFx.Animate(target).To("transform.position", new Vector3(5, 0, 0)).Duration(2f);
  // ... user clicks Cancel mid-animation ...
  anim.Revert(); // target.position is back to its pre-play value
  ```

- **`VMGTimeline.Revert()`.** Same semantics across every child in
  reverse order, so the channels reached the state they had before the
  *first* writer fired — even when later children overwrote earlier
  ones.

### Notes

- **Baseline is per-channel, captured lazily.** The first time each
  tween writes to a channel, the reader-side value is snapshotted into
  a per-animation dict keyed by `(target instance ID, field path)`.
  Subsequent tweens hitting the same channel don't re-capture
  (per-channel single-slot rule), so the stored value is always "before
  THIS animation began."
- **FunctionValue interaction.** `Refresh()` clears `hasFrom` so the
  next Evaluate re-resolves the lazy from-side, but the revert baseline
  is captured through the reader and stored in a slot
  `Refresh()` doesn't touch. A `Refresh()` mid-play doesn't move the
  Revert origin.
- **MotionPath interaction.** The position channel and the optional
  AutoRotate channel are each captured as full Vector3/Vector2 + float
  baselines. Separate from the runtime Z-preservation baseline
  `VMGMotionPathTween` already keeps for sampling.
- **Revert→Restart cycle.** After Revert, the next play cycle
  re-captures both `from` and the revert baseline from the (now
  restored) target state, so repeated Revert+Restart cycles behave
  intuitively.
- **Clip-driven tweens are not subject to Revert.** Clip data IS the
  authored baseline; the host GameObject's pre-Play state is recovered
  by stopping the animator, which the engine already supports.

### Changed

- `VMGChannelWriter` now takes an optional `fieldPath` string in its
  constructor and exposes `TargetInstanceID` / `FieldPath` getters.
  Used to key the revert-baseline dict. All existing call sites pass
  the path string they already had; default-null fallback keeps the
  ctor source-compatible for any out-of-tree user (none exist today
  but the writer is internal anyway).
- `VMGTweenBase` gained an `owner` back-pointer so code-driven tweens
  can register baselines on first Evaluate. Wired automatically when
  `VMGAnimate.EnsureFinalized` appends tweens to the animation.

## [0.16.0] - 2026-06-13

anime.js port group 3 #4: **MotionPath** — drive a target's
`transform.position` along an arc-length parametrized curve, optionally
auto-rotating to face the tangent. Closes the anime.js port at the
code-API level (only Revert / group 2 #5 remains; the DSL is one
parity round behind across groups 1–3).

### Added

- **`.AlongPath(VMGShapeAsset asset, int subShapeIndex = 0)`.** Follow
  a sub-shape from a vector asset. The asset's sub-shape nodes are
  tessellated through the existing Bezier flattener and stored as a
  flat polyline with cumulative arc length, so a normalized `t` walks
  the curve at uniform speed regardless of where the control points
  cluster.

  ```csharp
  VMGFx.Animate(target)
      .AlongPath(curveAsset)
      .Duration(2f)
      .Ease(VMGEasingPreset.InOut)
      .Loop();
  ```

- **`.AlongPath(IList<Vector2> points, bool closed = false)`.** Inline
  polyline variant for code-built paths. Same sampler, no asset
  required. `closed` adds a wrap-around segment when at least 3 points.

- **`.AutoRotate(float offsetDeg = 0)`.** Writes the curve's tangent
  angle (in degrees, `Atan2(dy, dx) + offset`) into
  `transform.eulerAngles.z`, so the target keeps its local +X facing
  along the curve. Supply `-90` for sprites whose forward is +Y.

### Notes

- The motion-path tween is a `VMGTweenBase` peer to `VMGCodeTween`,
  not a subclass. Position is computed by sampling the path at the
  eased time, not interpolated between two endpoints — the easing
  curve shapes *speed along the curve*, not the curve itself.
- Vector3 channels preserve the current Z on first evaluate (lazy
  capture mirroring code-tween's "from" capture). Vector2 channels
  write XY directly.
- Calling `.AlongPath` twice on the same animate replaces the pending
  path; one animate carries one curve, anime.js parity.
- The same animate can also have `.To(...)` tweens running in parallel
  on other channels; composition follows the engine's
  "last-registered wins per channel" rule.

## [0.15.0] - 2026-06-12

anime.js port group 3 #2 + #3: **FunctionValue + Refresh** (lazy `.To`
values resolved at render time, re-evaluable per loop or on demand) and
**Spring easing** (physical mass-spring-damper solver as a `VMGEase`
variant). Shipped together because they're independent and each is
small.

### Added — FunctionValue

- **`.To(path, () => value)` / `.FromTo(path, () => from, () => to)` —
  lazy values.** The function is invoked when the tween first renders
  (and again on `Refresh`), so callbacks can produce "current state"
  values without recomputing them at authoring time:

  ```csharp
  // New random target on every restart / refresh.
  VMGFx.Animate(box)
      .To("localPosition.x", () => Random.Range(-200f, 200f))
      .Loop().RefreshOnLoop();
  ```

  Overloads exist for every channel type: `float`, `int`, `bool`,
  `Color`, `Vector2/3/4`. `from`-side function values override the
  reader-based "current value" baseline; `to`-side functions stand in
  for the literal value at the same call site.

- **`VMGAnimate.Refresh()` / `VMGTimeline.Refresh()`.** Re-runs every
  lazy slot on the next sample. Idempotent and a no-op when no tween
  has a function slot. On a timeline, descends into nested timelines
  recursively.

- **`VMGAnimate.RefreshOnLoop(bool)`.** When set, the start of every
  new iteration auto-calls `Refresh`, so each loop sees freshly
  resolved values. Off by default (anime.js parity — resolve-once is
  the standard behavior; this is a convenience for the common
  "new random per loop" case).

### Added — Spring easing

- **`VMGEase.Spring(stiffness, damping, mass, velocity)`.** Physical
  spring solver, returned as a `VMGEase` so it drops into the existing
  `.Ease(...)` slot:

  ```csharp
  VMGFx.Animate(box)
      .To("localPosition.x", 200f)
      .Ease(VMGEase.Spring(stiffness: 80f, damping: 8f));
  ```

  Defaults match anime.js: `stiffness=100`, `damping=10`, `mass=1`,
  `velocity=0`. Closed-form solver — no LUT, no per-frame ODE step.
  Handles under-, critically, and over-damped regimes.

- **`.RecommendedDuration`.** Returns the spring's settle time
  (visually at rest within ~0.5% of target). Pair with
  `.Duration(spring.RecommendedDuration)` when the natural settle
  feels right; otherwise the spring is time-compressed to fit whatever
  duration you set, anime.js style.

- **`VMGEase.From("spring")`.** Resolves to a default-parameter
  spring, for DSL / string-driven paths (round 6 DSL doesn't surface
  spring directly yet — bundled with the deferred DSL parity round).

### Notes

- **VMGAnimator unchanged** (still the case through groups 1+2+3 #1+#2+#3 —
  clip-mode runtime path is stable; new code-API features benefit
  script-mode for free).
- **Round 6 DSL doesn't surface FunctionValue or Spring directly.** Both
  remain code-API only this round; bundled into the deferred "DSL
  parity round" along with Stagger / Reverse / explicit Duration /
  callbacks / PlaybackEase.

## [0.14.0] - 2026-06-12

anime.js port group 3 #1: `Stagger`. Build N children on a timeline
with auto-distributed offsets, no per-index `.Add(..., "+=0.1")` boilerplate.

### Added

- **`VMGTimeline.Stagger(targets, build, step)` — per-index time
  distribution.** Emits one child per target, placed at staggered offsets
  on the timeline:

  ```csharp
  var boxes = new[] { box1, box2, box3, box4 };
  VMGFx.Timeline()
      .Stagger(boxes, t => VMGFx.Animate(t).To("localPosition.y", 100f), step: 0.1f);
  ```

  Two overloads: `Func<T, VMGAnimate>` for "same effect per target"
  and `Func<T, int, int, VMGAnimate>` for "vary by index" (the lambda
  receives target/index/total — use the index to fan out values too).

- **`VMGStaggerFrom` — distribution origin.** `First` (default), `Center`,
  `Last`, `Random`. Mirrors anime.js's `from` parameter for 1-D
  distributions; `from: 'first' | 'center' | 'last' | 'random'` semantics
  preserved. (Grid / axis / `autoGrid` are not ported — anime.js's grid
  mode is rarely used in practice and the `Random` mode is seedable via
  the optional `seed:` parameter for reproducible output.)

- **`at:` anchor on Stagger.** Defaults to timeline end (next free slot,
  consistent with `Add`). Pass `VMGAt.Time(0.5f)` etc. to root the
  stagger somewhere specific. The anchor resolves *once* before the
  fan-out, so all N children stagger from the same base.

- **`VMGStagger.Lerp(i, n, from, to)` — index-to-value helper.** Available
  for the "vary by index" lambda when you want a per-index value
  (anime.js's `x: stagger([0, 500])`). Lives as a static helper until
  group 3 #2 (`FunctionValue`) lands a smoother surface.

### Notes

- Timeline `Defaults(duration, ease, delay)` propagate to every emitted
  child via the same path as `Add(...)`.
- DSL not extended this round; Stagger is a runtime-control API like
  group 2's runtime sugar. A DSL parity round will batch the group
  2 + 3 additions later.
- `VMGAnimator` not touched. Clip-mode runtime stable.

## [0.13.0] - 2026-06-12

The anime.js port reaches the user-facing level. A single text file is
now enough to author + run a VMG motion graphic. Three chunks landed
together: the Scene builder (code-driven hierarchy), VMGAnimator
script-mode (DSL → Scene + animates + timelines), and Timeline parity
sugar against anime.js v4.

### Added

- **`VMGFx.Scene` — code-driven scene composition.** New facade for
  building VMG hierarchies from code:

  ```csharp
  var scene = VMGFx.Scene(uiRoot)
      .Add("ring", VMGFx.Circle().Size(200).Stroke(Color.white, 4f).Trim(0f, 0f))
      .Add("dot",  VMGFx.Circle().Size(40).Position(120, 0).Fill(Color.cyan))
      .Group("orbit", g => g
          .Add("comet", VMGFx.Circle().Size(20).Fill(Color.yellow)));
  ```

  Six shape descriptors: `Circle`, `Ellipse`, `Rectangle`,
  `RoundedRectangle` (with `.CornerRadius`), `Polygon` (with `.Sides`),
  `Path` (with `.Points`, `.Closed`). Root-type dispatch — RectTransform
  uses `VectorImageGraphic`, plain Transform uses `VectorSpriteRenderer`.
  `Add(name, ...)` is idempotent: re-runs reuse children by name, so
  Edit-mode domain reloads and script-mode re-entries are safe.
  Descriptor state is always-written to the renderer — what you set is
  what you get; a shape that doesn't call `.Fill()` comes up with fill
  disabled.

- **VMGAnimator script-mode — flat-statement DSL.** Drop a `.txt`
  TextAsset into `VMGAnimator.script` and the script runs in place of
  (or alongside) an authored clip:

  ```
  add ring  circle  size=200  stroke=#ffffff,4  trim=0,0
  add dot   circle  size=40   pos=120,0         fill=cyan

  group orbit {
    add comet circle size=20 fill=yellow
  }

  animate ring m_Trim.end -> 1 duration=1 ease=outQuad

  timeline {
    set    dot m_Fill.color = magenta at=0
    animate dot localScale -> 1.5,1.5,1 duration=0.5 ease=outCubic
    label  midway
    animate dot localScale -> 1,1,1 duration=0.5 ease=inCubic at=midway
    call   pulseDone at=+=0
  }
  ```

  Statements: `add` / `group` / `animate` / `timeline` / `set` / `call`
  / `label`. `//` line comments. `<name>` target resolution falls back
  to the child Transform when the field path doesn't compile against
  the renderer, so `animate dot localScale -> ...` works without
  `.transform` suffix. VMGAnimator drives all Seeks every LateUpdate;
  the engine never ticks the script's animations independently. When
  both `script` and `clip` are assigned, script wins (with a one-time
  warning).

- **Timeline parity sugar against anime.js v4.** All landed on
  `VMGTimeline` / `VMGAnimate` (`VMGAnimator`'s clip-mode is unchanged
  but script-mode + code-driven `VMGFx.Timeline()` benefit):
  - **Position parser additions** (`VMGAt`): `'*=F'` multiplies the
    previous child's duration; `'<+=N'` / `'<<+=N'` (and `-=`) layer
    relative offsets on the `<` / `<<` anchors. Invalid `'label*=F'`
    falls back to End() rather than guess.
  - **Lifecycle sugar**: `Restart()`, `Resume()`, `Reset()`,
    `Complete()`, `PlaybackRate` (get/set, clamped ≥ 0). Symmetric on
    `VMGTimeline` and `VMGAnimate` so call sites use the same shape
    regardless of which they're holding. `Complete()` warns + no-ops
    on infinite loops.
  - **`Sync(...)` alias** on `VMGTimeline` — anime.js parity name for
    `Add(...)`. Both shapes (`VMGAnimate`, `VMGTimeline`) supported.
  - **`Reverse()`** — toggle direction at the current playhead and
    keep playing. Mirrors `CurrentTime` around the midpoint so
    iterationTime stays continuous across the flip. Auto-resumes
    from completed (anime.js parity). Distinct from `Reversed(bool)`,
    which is the construction-time setter. Available on both
    `VMGTimeline` and `VMGAnimate`.
  - **Explicit `Duration(seconds)` on `VMGTimeline`** — lock the
    timeline's total length to a fixed value. Pads with a held end
    (children clamp via the existing `OnAfterRender` clamp) when
    longer than the children's natural span; truncates with a
    warning when shorter. Distinct from `Stretch(...)`, which scales
    children proportionally. Pass a negative value to release the
    lock. anime.js parity for `duration: N` in `createTimeline()`.
  - **Extra callbacks**: `OnBeforeUpdate(...)` fires inside `Render`
    before value writes (`fireCallbacks`-gated, same as `OnUpdate`);
    `OnRender(...)` is an anime.js-name alias for `OnUpdate(...)`;
    `OnPause(...)` fires on the `Pause()` transition only (idempotent
    Pause is a no-op; completion-driven pause routes through
    `onComplete` instead). All three available on `VMGTimeline` and
    `VMGAnimate`.
  - **`PlaybackEase(...)` on `VMGTimeline`** — ease curve applied to
    `iterationTime` before child dispatch. Composes with each
    child's own ease (children see a re-mapped time but still run
    their own curve). Same overload shape as `VMGAnimate.Ease`:
    preset, `VMGEase`, 4-param bezier, or anime.js-style name. Opt-in
    — without a call the no-remap path stays zero-overhead.

### Changed

- **VMGEngine no longer ticks script-driven animations.**
  `VMGFxCompiled.DetachFromEngine()` runs immediately after script
  compile so VMGAnimator is the only Seek driver. This is what makes
  External-mode work uniformly between clip-mode and script-mode.

- **`VMGTimeline.OnAfterRender` now sweeps the previous→current
  iterationTime window** and fires 0-duration child timers found in
  between via `RenderForCallbacks()`. Previously plain `Seek()`
  suppressed callbacks (`fireCallbacks=false`), so under
  VMGAnimator-driven playback `timeline.Call(...)` never fired. This
  was a latent Stage-2 bug; script-mode smoke surfaced it.

- **`VectorImageGraphic.FitToRect` is now public.** VMGScene needs to
  write it from descriptors; setter calls `SetVerticesDirty()`.

- **`VMG.Runtime.Animation` asmdef now references `VMG.Runtime.UI` +
  `VMG.Runtime.World`.** VMGScene dispatches on root type (RectTransform
  → UI, plain Transform → world), so it must see both renderer types.

### Fixed

- **`Defaults(ease)` on a Timeline now reaches the baked tweens.**
  Previously `VMGAnimate` finalized before `Add` could apply the
  default, so only `duration` / `delay` landed via post-finalize
  mutation and `ease` was silently dropped. Now
  `VMGTimeline.Add(builder)` calls `ApplyDefaultsToBuilder(builder)`
  before `ClaimByTimeline()` triggers finalize; defaults land on the
  actual tweens. `VMGAnimate` tracks `HasDurationUserSet` /
  `HasDelayUserSet` so user-set values aren't overwritten.

### Hard constraints preserved

- No Unity Timeline / PlayableDirector dependency anywhere.
- `ExecuteAlways` + `Application.isPlaying` split on VMGAnimator —
  Edit-mode preview keeps working.
- Scene `Add` by name stays idempotent (script-mode safety relies on
  this).

## [0.12.0] - 2026-06-09

### Changed

- **Fill triangulation rewrite.** `FillTessellator` now picks a strategy
  per path: simple polylines go through the existing ear-clipper (one
  vertex per node, no change vs. 0.11.x); self-intersecting polylines
  go through a new trapezoidal scanline decomposition. The scanline path
  cuts the plane at every vertex Y and every edge-edge crossing Y, then
  fills the even-odd regions in each horizontal strip as trapezoids.
  Independent of CW/CCW, frame-stable on morph intermediates, and matches
  the user-facing definition "the region geometrically enclosed by the
  stroke." A star traced as one continuous polyline correctly hollows
  out the inner pentagon; a star traced as its outer silhouette fills
  through.
- **Bezier tessellation is now adaptive.** `BezierTessellator` replaced
  uniform t-stepping with recursive de Casteljau subdivision plus a
  flatness test (0.5% of chord length). `bezierSamplesPerSegment` is
  reinterpreted as a depth cap (`ceil(log2(samples))`) so the worst case
  matches the old budget while near-straight curves use far fewer points
  and high-curvature segments get the budget they need.
- **Miter join correctness.** The miter-length check now divides by the
  full stroke width instead of the outer half-width, so Inner alignment
  no longer lets one bend direction produce unbounded spikes (the outer
  half-width collapsed to 0 there). Near-collinear joins (|sin θ| < 1e-3)
  skip the spike calculation entirely. `miterLimit` is now interpreted
  in the SVG convention (multiple of full stroke width).
- **ShapeStack slot alignment is configurable.** New
  `ShapeStack.alignment` enum (`Auto` / `Preserve`, keyframable). `Auto`
  (default) reorders each closed slot path so blends between *different*
  shapes don't shrink at the midpoint. `Preserve` keeps node order so
  blends between *the same shape at different rotations* visibly rotate.
- **`GameObject > UI (Canvas) > Vector Image` menu placement.** Unity 6's
  UGUI renamed the category from `"UI"` to `"UI (Canvas)"`. The package
  now matches so Vector Image appears alongside Image / Raw Image /
  Panel instead of in a separate `"UI"` category.

### Added

- **ShapeStack inspector polish.**
  - Slots with `intensity == 0` get a dimmed header label and a `·`
    indicator so live vs. inactive slots are scannable at a glance.
  - Header `⋯` menu: "Reset intensities" (slot 0 = 1, others = 0) and
    "Swap Slot a ↔ Slot b" for all 6 pairs. Swap moves the whole slot
    (intensity + shape) via `SerializedProperty.boxedValue`.
- **FreePath new-node placement follows tangent.** Appending a node now
  continues in the previous node's outTangent direction, or the previous
  segment's direction if no tangent, with distance matching the previous
  segment. Old behaviour (`prev + (20, 0)`) is the final fallback.
- **SceneView inactive-slot guides.** Slots other than the active one
  draw their path as a faint polyline so multi-slot blends are easier
  to line up while editing.

### Fixed

- Ear-clipper output is now deterministic on the same polygon shape
  regardless of which node the caller listed first (rotates the index
  list to start at the leftmost-lowest vertex before the ear loop).
  Stabilizes fill triangle sets across morph frames.

## [0.11.0] - 2026-06-08

### Added

- **Depth (3D fill extrusion) on `VectorSpriteRenderer`.** New
  `DepthStyle` (enabled / thickness / alignment) extrudes the fill
  polygon along the Z axis. Pivot at Z=0, +Z is camera-facing;
  alignment is Front / Center / Back (memory:
  `project_depth_feature.md`). Fill is extruded; stroke is duplicated
  onto both faces with epsilon Z bias to read from any angle. When
  depth is on, stroke alignment is forced to Inner so the ribbon stays
  inside the silhouette.
- **Vertex normals on extruded fill.** +Z front, −Z back, per-edge
  outward on side walls — a Lit material in a Forward / Forward+ /
  Deferred 3D renderer shades the sides correctly.
- **`MeshBuffer` helpers**: normal-aware `AddVertex`,
  `PromoteToZWithFrontNormal`, `CopyFrom`, `FlipForBackFace`. 2D-only
  paths stay unchanged (`ApplyTo` only pushes normals when the buffer
  carries one per vertex).
- **Korean README** (`README.ko.md`) linked from the English README.

### Changed

- `GameObject > VMG > Vector Sprite Renderer` factory now defaults to
  size 1m × 1m and stroke width 0.04 so a freshly created world
  renderer doesn't overwhelm the default scene camera (previous
  defaults were UGUI-friendly 100 units / 4 width).

### Fixed

- **FreePath SceneView handle visibility against white fills/strokes.**
  Yellow node handle with a dark rectangle outline cap drawn behind it
  so the handle stays readable on any color.

### Known limitation

- URP 2D Renderer does NOT light Lit shaders by 3D Directional Light —
  it handles only `Light2D`. Visible depth shading requires a Forward /
  Forward+ / Deferred 3D renderer. The mesh normals are correct in
  either case; this is purely about the renderer pipeline.

## [0.10.0] - 2026-06-07

### Changed (breaking)
- **The renderer's "shape" surface is now a `ShapeStack` of 4
  PrimitiveShapeSource slots with per-slot intensities.** Slots are
  weighted symmetrically — there is no special base slot. The Build
  pipeline arc-length-resamples each contributing slot to
  `resampleCount` points and produces a per-index weighted average.
  When only one slot has intensity > 0 the blend is skipped and that
  slot's raw vertex count is preserved (no quality loss). If all
  intensities are 0, slot 0 is shown as a fallback.
- `m_Shape` and `m_Morph` fields are gone. Both renderers expose
  `m_ShapeStack` instead. `Shape` / `MorphModifier` properties are
  replaced by `ShapeStack`.
- `PathMorphModifier` is **deleted**. Cross-shape transitions are
  expressed as intensity tweens between stack slots.
- `DOMorph` DOTween extension deleted. New `DOSlotIntensity(slotIndex,
  value, duration)` is the replacement.
- `DOSize` and `DOCornerRadius` now target slot 0's shape (the
  conventional "primary" slot). Other slots are reached via
  `g.ShapeStack.m_SlotN.shape.*` directly inside a DOTween.To setter,
  or by changing slot 0 to point at the shape you want to drive.
- SceneView overlay shows a 4-button slot selector ("S0 (1.00) | S1
  (0.00) | …") instead of the old "Base / Morph Target" toggle.

### Added
- `ShapeStack` and `ShapeSlot` structs ([Runtime/Primitives](Runtime/Primitives/)).
- `ArcLengthResample` utility ([Runtime/Core](Runtime/Core/)) extracted
  from the deleted PathMorphModifier so other modifiers / tools can
  reuse uniform arc-length sampling.
- `ShapeStackDrawer` custom inspector with per-slot foldouts and a
  prominent intensity slider on each slot's header.
- `UI VectorImageGraphic` now applies fit-to-rect to all 4 slots so
  blending stays in sync with the RectTransform.

### Migration
- None. The release is the first public 0.x version that ships the
  ShapeStack model; pre-0.10 scenes are not auto-migrated and are
  expected to be re-authored if any existed.

## [0.9.0] - 2026-06-07

### Changed
- **All authoring data is now struct-typed so the Animation window can
  keyframe every inspector field.** Unity's `AnimationClip` binding only
  walks into struct-typed serialized members; class-typed members were
  opaque object references and their inner fields didn't surface.
  - `PrimitiveShapeSource` → struct.
  - `PathMorphModifier`, `RoundCornerModifier`, `TrimPathModifier` → struct.
  - Renderer getters now return `ref` so external code (sample
    scripts, DOTween extensions) keeps reference semantics:
    `ref var trim = ref m_Target.TrimModifier`.
  - Field initializers were dropped (structs don't allow them); each
    type gained a `Default()` factory and a `Normalize()` fixup that
    applies sane defaults on first `Build`/`Apply`.

- **FreePath nodes are now stored as 64 individual `FlatNode` fields
  (`m_Node00`..`m_Node63`) plus `activeNodeCount`.** Unity exposes
  named struct fields to the Animation window's Add Property tree but
  NOT `List<T>` or `T[]` elements, so the flat layout is the only
  way to make per-node values keyframable.
  - Add / Remove Last buttons in the inspector adjust
    `activeNodeCount`; new slots seed near the previous last node so
    paths don't jump back to (0,0).
  - Reorder is unsupported by design — the slot index is the keyframe
    channel and renaming it would break bindings.
  - Legacy `freeNodes` list data is migrated into the flat slots on
    first `Normalize()` and the list is cleared afterwards.

- **SceneView "Active Shape" overlay** — a small toolbar in the upper
  left of the SceneView lets the user pick whether handles operate on
  the base shape or the morph target. Without it, base/target nodes
  would visually overlap when both are FreePath.

- **SceneView handles for FreePath now route every drag through
  `SerializedProperty`** so Unity's Record mode captures each drag as
  an automatic keyframe at the playhead. No sync step, no parallel
  surface.

- **Node `type` is the single source of truth for corner-vs-curve.**
  Switching a slot back to `Corner` in the inspector clears its
  tangents, so leftover non-zero values can't keep the segment curved.

### Fixed
- **Open-path Trim no longer blanks the whole path when offset crosses
  the end.** Open paths now clamp the `[start, end]` window after
  applying the offset (clip whatever falls outside `[0,1]`) instead of
  taking the closed-path wrap path, which used to enter an
  "inverted window → nothing to draw" branch. Closed-path wrap
  behaviour is unchanged.

### Removed
- `ShapePipeline.Evaluate(IShapeSource, IList<IPathModifier>)` — the
  UGUI path used to build a temporary list of `IPathModifier`s each
  frame; with struct modifiers that would box on every frame. Renderers
  now call each modifier's `Apply` directly. `ShapePipeline` keeps
  just the two scratch buffers it provided.
- `VectorImageGraphic.m_Modifiers` / `m_FillMods` and
  `BuildFillModifierList()` — same reason.

### Compat / migration
- `PathSnapshot` and the "Sync To Snapshot" / "Sync From Snapshot"
  buttons that briefly shipped in 0.6.0/0.7.0 are gone — the
  motivation (per-node keyframing) is now solved by the flat slot
  layout.
- Old scenes serialized against the `List<VectorNode> freeNodes`
  field migrate transparently at first `Build()` (the legacy list is
  read once and cleared).
- External code using `renderer.TrimModifier.enabled = true;` style
  mutations now needs `ref var t = ref renderer.TrimModifier;` to land
  on the real field. Bundled DOTween extensions are already updated.

## [0.8.0] - 2026-06-06

### Changed
- **FreePath node editing now records keyframes directly on
  `freeNodes`.** SceneView handles write per-node `position`,
  `inTangent`, and `outTangent` through SerializedProperty, so when
  the Animation window is in Record mode each drag becomes an
  automatic keyframe at the playhead. No sync step, no parallel
  surface — the same data drives both authoring and animation.

### Removed
- `PathSnapshot` struct and all related machinery
  (`SyncFreeNodesToSnapshot` / `SyncSnapshotToFreeNodes`, the
  snapshot inspector section, "Sync To Snapshot" / "Sync From
  Snapshot" buttons, and the `AnimationModeBridge` editor helper).
  The mirror surface added in 0.6.0 was conceptually heavy and
  required the user to think about authoring vs. animation order;
  routing `freeNodes` itself through SerializedProperty achieves the
  same outcome with zero extra concepts.
- Recording HelpBox and freeNodes lock that landed in 0.7.0 — they
  depended on `AnimationMode.InAnimationMode()`, which does not
  reliably mirror the Animation window's record state.

### Known limitation
- The list LENGTH of `freeNodes` (node add / remove) is still NOT
  keyframable; Unity AnimationClip cannot keyframe `List<T>` size.
  Keep the node count stable across a clip, or use
  `PathMorphModifier` against a second shape to animate
  pose-to-pose transitions including node-count changes.

## [0.7.0] - 2026-06-06

### Added
- **Animation Record mode integration for FreePath.**
  When the Unity Animation window is in Record (or Preview) mode and
  the user drags a FreePath node or tangent in the SceneView, the edit
  is routed through SerializedProperty writes on the keyframable
  PathSnapshot — so each drag registers as an automatic keyframe at
  the playhead, matching the AE / SpriteEditor record workflow.
  - First edit during recording auto-syncs `freeNodes` into the
    snapshot slots and turns `snapshot.enabled` on. This sync itself
    is a SerializedProperty write so the activation registers as a
    base keyframe.
  - The `freeNodes` list and the Sync buttons are disabled in the
    inspector while recording, with a HelpBox explaining the routing.
    Node add/remove cannot be keyframed; either stop recording or use
    PathMorphModifier for shape-count changes.
  - `AnimationModeBridge` helper centralises the Record/Preview check
    and SerializedProperty plumbing for future editors.
- `FreePathSceneHandles.Draw` gained an overload that takes
  `SerializedObject` + the shape's property path so the handles can
  route through SerializedProperty during recording. The legacy
  overload still works (direct freeNodes edit, no auto-keying).

## [0.6.0] - 2026-06-06

### Added
- `[Tooltip]` on every inspector-facing serialized field across the
  package, including keyframable-vs-not annotations so authors see
  Animator support at a glance.
- `PathSnapshot` — AnimationClip-friendly mirror of FreePath data.
  - Fixed-size arrays (32 slots) for `positions`, `inTangents`,
    `outTangents`, plus `activeNodeCount` and an `enabled` toggle, all
    keyframable as individual `m_Array.data[i]` bindings.
  - `PrimitiveShapeSource.BuildFree` uses the snapshot when enabled,
    so Animator can drive every per-node value the inspector can edit.
  - Custom inspector adds **Sync To Snapshot** / **Sync From Snapshot**
    buttons for round-tripping authoring data and animated state.
  - `OnAfterDeserialize` resizes arrays so existing scenes inherit
    32-slot capacity without per-asset migration.
- README "Animation support" section documenting the keyframable
  surface and the two known workarounds (`PathMorphModifier` for shape
  pose blending, `AnimationEvent` / Timeline `Signal` for Object-ref
  swaps).

### Deferred
- Object-reference keyframing (`Material` / `Texture` / `SvgAsset`)
  via custom Timeline tracks.

## [0.5.0] - 2026-06-06

### Added
- Custom material slot on both renderers.
  - `VectorImageGraphic` uses the inherited `Graphic.material` slot, now
    exposed in the inspector under a "Rendering" section.
  - `VectorSpriteRenderer` adds a `[SerializeField] Material m_Material`
    field; if null, falls back to the previous auto-`Sprites/Default`
    behaviour.
- Texture slot on both renderers.
  - `VectorImageGraphic` overrides `Graphic.mainTexture` so a `Texture`
    assigned in the inspector is bound on the CanvasRenderer with zero
    material instantiation.
  - `VectorSpriteRenderer` applies the texture through a
    `MaterialPropertyBlock` so the shared material stays shared across
    instances (no per-renderer material instantiation).
  - Mesh UVs are now bounds-normalised to `[0,1]`, so any texture in the
    bound slot lays continuously across the renderer:
    - UGUI procedural: `rectTransform.rect` (or vertex bounds when
      `FitToRect` is off).
    - UGUI SVG: the fit-to-rect viewBox area.
    - World procedural: the vertex bounds of the combined mesh.
    - World SVG: the scaled viewBox.
- `VectorSpriteRenderer` now exposes `Sorting Layer` and `Order in Layer`
  in its inspector and writes them onto the underlying `MeshRenderer`
  (`MeshRenderer.sortingLayerID` / `sortingOrder`) on enable / validate.

### Changed
- `MeshBuffer` gains `NormalizeUVsToRect(Rect)` and
  `NormalizeUVsToVertexBounds()` post-processing helpers. Builders still
  emit raw-position UVs; renderers call the appropriate normalisation
  once the mesh is fully assembled.

## [0.4.0] - 2026-06-06

### Added
- SVG import (path-first MVP).
  - New `VMG.Runtime.Svg` assembly with `VMGShapeAsset` ScriptableObject:
    one asset per imported .svg, holding a flat list of sub-shapes each
    with its own path + fill + stroke.
  - `SvgPathParser` covers the full `d` grammar: M/m L/l H/h V/v C/c S/s
    Q/q T/t A/a Z/z. Quadratic Beziers are elevated to cubic; arcs are
    converted to ≤90° cubic segments.
  - `SvgDocumentParser` handles `<path>`, `<rect>` (incl. rounded
    corners), `<circle>`, `<ellipse>`, `<line>`, `<polyline>`,
    `<polygon>`, `<g>`, viewBox normalisation, transforms
    (matrix/translate/scale/rotate/skew), and fill/stroke presentation
    attributes plus inline `style="..."`.
  - `SvgScriptedImporter` auto-converts any `.svg` dropped in the project
    into a `VMGShapeAsset`. Optional Y-flip so SVG coordinates align
    with UGUI.
  - `VectorImageGraphic` and `VectorSpriteRenderer` both accept a
    `VMGShapeAsset`. When set, procedural shape + modifiers are bypassed
    and each sub-shape draws with its own SVG-derived fill/stroke,
    fit-to-rect for UGUI / centered+scaled for world-space.
- Sample `SvgImport` with two demo `.svg` files (heart, star).

### Known limitations
- No gradients, filters, text, masks/clip paths, CSS classes,
  `<use>`/`<symbol>`, or animation.
- Path `A` command flag arguments must be whitespace-separated
  (uncommon `01-3,0`-style flag packing is not yet parsed).

## [0.3.0] - 2026-06-06

### Added
- Optional `VMG.Runtime.Tween` assembly with DOTween extension methods on
  both `VectorImageGraphic` and `VectorSpriteRenderer`:
  `DOFade`, `DOColor`, `DOStrokeColor`, `DOStrokeWidth`, `DOSize`,
  `DOCornerRadius`, `DOTrim`, `DOTrimStart`, `DOTrimOffset`, `DORoundness`,
  `DOMorph`.
- Assembly is gated behind `defineConstraints: ["VMG_DOTWEEN"]` and a
  `versionDefines` rule on `com.demigiant.dotween`, so projects without
  DOTween see zero overhead and zero compile errors.
- UniTask interop comes for free via DOTween's `AsyncWaitForCompletion()` /
  `WithCancellation()`.
- New sample `TweenIntegration` showing a draw-on + stroke pulse + color
  shift sequence.

## [0.2.0] - 2026-06-06

### Added
- Cubic Bezier support on FreePath via per-node `inTangent` / `outTangent`,
  tessellated through `BezierTessellator` before the modifier stack runs.
- SceneView handles for FreePath: drag-edit each node's position plus its
  in/out cubic tangents (only drawn when present so straight paths stay
  clean). Shared helper used by both UGUI and world renderers.
- Stroke `LineCap` styles wired into the mesh builder: Butt (no cap),
  Square (extend by `width/2`), Round (half-disc fan).
- Stroke `LineJoin` styles: Miter (with miter-limit + automatic bevel
  fallback), Bevel (single bridge triangle), Round (fan on outer side
  of bend).
- `PathMorphModifier` — arc-length-uniform resampling + lerp between the
  current shape's polyline and a second `PrimitiveShapeSource`. Sits at
  the head of the modifier order (Morph → RoundCorner → Trim).

### Notes
- Modifier execution order is still fixed (Morph → RoundCorner → Trim) on
  both renderers. AE-style user-reorderable stack is deferred to a later
  phase.

## [0.1.0] - 2026-06-06

### Added
- Initial Phase 1 MVP scaffolding.
- Core path/node data model and modifier-stack evaluation pipeline.
- Stroke mesh builder with Inner/Center/Outer alignment.
- Fill mesh builder with self-contained ear-clipping triangulator.
- Round Corner and Trim Path modifiers.
- Primitives: Circle, Ellipse, Rectangle, RoundedRectangle, Polygon, FreePath.
- UGUI renderer (`VectorImageGraphic`) and world renderer (`VectorSpriteRenderer`).
- Editor `GameObject` menu entries and basic inspectors.
- Sample scene: animated trim circle.
