# Changelog

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
