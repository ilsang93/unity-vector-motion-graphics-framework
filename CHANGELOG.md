# Changelog

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
