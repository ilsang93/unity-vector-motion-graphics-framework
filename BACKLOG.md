# VMG Backlog

Tasks deferred from the current phase. Captured to avoid loss of context.

---

## [Animation] Object-reference fields (Material / Texture / SvgAsset) keyframing

### Symptom
`Material`, `Texture`, and `VMGShapeAsset` fields on both renderers are
Object references. A standard `AnimationClip` cannot keyframe these —
the Object track is PPtr-only and unavailable for user-defined slots.

### Workarounds available today
- `AnimationEvent` on the clip calls a script method that assigns the
  new reference at the right frame.
- Timeline `SignalReceiver` does the same thing with a more declarative
  workflow.

### Design (deferred)
Build a custom `PlayableAsset` / `PlayableBehaviour` pair per
swappable slot (texture / material / SVG). Each track on Timeline
holds a sequence of (time, reference) pairs and writes the active one
into the renderer each frame. Big-ish surface — one track type per
slot, plus editor track drawers — so deferred until at least one user
specifically needs it.

### Acceptance
- A Timeline can swap a renderer's texture or SVG asset at specific
  frames without a script callback.

---

## [Phase 3] User-reorderable Modifier Stack

### Requirement
Today the renderer hardcodes the modifier order (RoundCorner → Trim)
in fixed serialized slots. AE shape layers let users add / reorder / disable
arbitrary operators (Offset Paths, Repeater, Wiggle Transform, Pucker & Bloat…).
Promote VMG's pipeline to that model.

Note: cross-shape morphing is no longer a modifier — that's the ShapeStack's
job. Modifiers operate on the path that comes out of the stack's blend.

### Design
- Serialize a `[SerializeReference]` `List<IPathModifier>` on the renderers
  alongside (or replacing) the fixed slots.
- Custom inspector with `ReorderableList` for drag-to-reorder and
  add-via-popup ("Add Modifier ▸ Round Corner / Trim Path / Offset Paths / …").
- Animator integration: each modifier inside the array still has stable
  field paths (`m_Modifiers.Array.data[0].radius`), but reordering invalidates
  bindings — so document that reordering should happen at authoring time, not
  via Animation.
- New modifiers to add at this point: `OffsetPathsModifier` (uniform
  inward/outward offset), `RepeaterModifier` (N copies with per-iteration
  transform delta), `WiggleTransformModifier`.

### Acceptance
- An empty renderer with zero modifiers behaves like a renderer with all
  three current modifiers disabled.
- Reordering modifiers in the inspector reorders execution.
- A scene with the previous fixed-slot setup migrates without losing data
  (write an OnAfterDeserialize upgrade path).

---

## [Known issue] Occasional miter spike on bezier curves

### Symptom
Rare visual artifact where a miter join shoots a thin spike outward on
otherwise-smooth bezier-tessellated strokes. Not severe — does not block
shipping — but visible at certain tangent configurations.

### Suspected causes
1. `EmitMiter` rejects the join only when `|denom| < 1e-6` (lines nearly
   parallel). Just above that threshold the line intersection is finite but
   astronomically far from the corner, so the miter-limit test fires AFTER
   we've already added a spike vertex very far out. Mitigation: clamp the
   computed `t` and re-run the limit check, or use an angle-based denom
   threshold (e.g. fall back when `dot(dirA, dirB) > 0.9999`).
2. Bezier tessellation occasionally produces adjacent micro-segments whose
   normals oscillate; each micro-corner emits a normal-sized miter spike.
   Mitigation: a "weld nearby colinear samples" post-pass in
   `BezierTessellator`, or auto-bevel when the corner angle is below a
   user-configurable threshold (~5°) regardless of miter limit.

### Acceptance
Spike should not appear on default `circleSegments`/`bezierSamplesPerSegment`
settings, with any combination of tangent directions, when miter join is
selected. Bevel/Round joins are already immune.

---

## [Known issue] Bezier tessellation leaves visible chord gaps on high-curvature segments

### Symptom
On strongly curved Bezier segments, the rendered polyline noticeably
"flat-spots" between samples. Raising `bezierSamplesPerSegment` to its
maximum (64) hides it on shallow curves but cannot eliminate it on tight
ones — the curve still reads as a sequence of straight chord segments.

### Root cause
`BezierTessellator.Tessellate` walks each cubic in uniform `t`-steps
(`t = i / N`). Curvature is not uniform in `t`, so chord length spikes
where the curve bends hardest — exactly where chord error is most visible.
More uniform `t`-samples do not converge fast in those regions.

### Investigation directions (pick one)

1. **Adaptive subdivision (recommended starting point).** Recursively
   subdivide a segment whenever the chord-to-curve flatness exceeds a
   pixel-space tolerance. Standard algorithm: split at `t=0.5`, measure
   distance from control points to the chord, recurse if above tolerance.
   Convergence is curvature-driven, output sample count auto-scales.
   Replace `bezierSamplesPerSegment` (or keep it as an upper bound).

2. **Uniform arc-length sampling.** Compute the arc-length parameterisation
   of each cubic (numeric integration), then place N samples at equal
   arc-length intervals. Cheaper than adaptive subdivision but still leaves
   chord error visible on tight curves — only fixes the *spacing* problem,
   not the *flatness* problem. Worth pairing with #1.

3. **GPU stroke (longer term).** Render strokes as analytic distance-field
   primitives in a fragment shader instead of tessellating to a polyline.
   Eliminates the chord-gap class of artifacts entirely but breaks the
   "single Core path drives both UI and World renderer" symmetry. Big
   architectural shift — defer unless #1 proves insufficient.

### Acceptance
A path with high-curvature bezier segments (e.g. a small circle made of
4 cubic-bezier quadrants) renders without visible flat spots at default
tolerance, without the user having to raise sample counts manually.
