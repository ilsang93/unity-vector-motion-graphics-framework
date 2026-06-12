# VMG Animator

A per-component animation system that lives next to VMG renderers
without depending on Unity's `Animator` / Timeline / PlayableDirector.
Designed for procedural motion graphics where you want short, scripted
sequences (intro stings, UI feedback, state transitions) instead of
authoring full skeletal animation.

The animator coexists with Unity's `Animator`. You can keep using
Unity Animation to drive `progress` on a `VMGAnimator` and let the
VMG animator fan that out to many child fields — see *Driving from
Unity Animation* below.

## At a glance

| | |
|---|---|
| Component | `VMG.Animation.VMGAnimator` |
| Authoring surfaces | (1) `VMGAnimationClip` asset, (2) code (`VMGFx.Animate` / `Timeline` / `Stagger`), (3) `.vmgfx` text DSL on `script` field |
| Clip asset | `VMG.Animation.VMGAnimationClip` (`Assets/Create/VMG/Animation Clip`) |
| Editor windows | Inspector (built-in) + dockable VMG Timeline (`Open in Window` button) |
| Tools menu | `Tools ▸ VMG ▸ Import CSS @keyframes…` / `Tools ▸ VMG ▸ CSS → VMGFx Window` |
| Runtime API | `Play()` / `Pause()` / `Stop()` / `PlayAsync(token)` / `Sample(t)` |
| Awaitable | `Task` (core); `UniTask` via optional `VMG_UNITASK` define |

When both `script` (TextAsset / `.vmgfx`) and `clip` (VMGAnimationClip)
are assigned, **script wins** — the inspector shows the Clip section
disabled. To switch back, clear the script slot.

## Your first animation

1. Add a `VMGAnimator` to a GameObject. Treat that GameObject as
   the *animation root* — every track addresses a child of it
   (or itself).
2. Create a clip: `Assets > Create > VMG > Animation Clip`. Assign
   it to the animator's `Clip` field.
3. Click `+ Add Track`. A channel picker appears showing every
   keyframable field on the animator and its children (Transform's
   position/rotation/scale plus every VMG renderer's color, trim,
   stroke, fill, slot intensities, etc.).
4. Pick a channel. Right-click the track row and `Add Key Here`
   at the playhead's current time. Drag the playhead, change the
   value in the inspector, and add another key.
5. Press `Play` in the inspector. Use the timeline scrubber to
   preview specific times.

That's the loop. Add more tracks and keys until the motion looks
right.

### Editor controls cheat-sheet

| Action | How |
|---|---|
| Add track | `+ Add Track` opens the channel picker (search + drilldown). |
| Re-bind track | Track row left-click → inspector `Re-bind…`. |
| Add key | Right-click on a track row → `Add Key Here`. Or use **Record mode**. |
| Move key | Drag. Snaps to 1/N seconds (clip's `Snap (per second)`, default 60). Hold Shift to disable snap temporarily. |
| Multi-select | Shift+click toggles a key in/out. Empty-area drag = rubber-band. |
| Multi-drag | All selected keys shift by the same delta from anchor. |
| Multi-edit | When all selected keys share a type, the inspector shows value/tangent edits that apply to all. |
| Copy/Paste | `Ctrl+C` / `Ctrl+V`. Paste targets the right-clicked track strictly; falls back to original binding only when no track was specified. |
| Delete | `Delete` / `Backspace` on the selected keys or event. |
| Add event | Right-click the events row → `Add Event Here`. |
| Detach window | Inspector → `Open in Window`. The inspector hides its timeline; the floating window takes over. |
| Track selection | Left-click the track label area — visible orange highlight. |
| Zoom | Mouse wheel anywhere over the timeline. Anchor = mouse cursor's time. |
| Fit view | The `Fit` button at the bottom-left of the scrollbar area. |

## Internal vs External mode

A `VMGAnimator` has a `mode` enum:

- **Internal** (default): the animator advances `progress` itself in
  `Update` (only at runtime, not in Edit mode) and samples in
  `LateUpdate`. Use `Play()` / `Pause()` / `Stop()` to control it.
- **External**: `progress` is set from outside (Unity Animator
  keyframing, your own script, Timeline, etc.). The animator does
  not advance time on its own but still samples in `LateUpdate`.

This is the indirect-control path for Unity Animation: keyframe
`VMGAnimator.progress` between 0 and 1 in a Unity AnimationClip,
set the VMGAnimator to External, and the VMG animator fans the
sample out to every bound child channel.

## Driving from Unity Animation

```csharp
// On a parent Animator (Unity's), keyframe a single float:
//   property: progress
//   curve:    0 → 1 over your desired duration
// On the child VMGAnimator, set mode = External.
```

In Edit mode this still works: VMGAnimator is marked
`[ExecuteAlways]` and `LateUpdate` runs while Unity Animation's
preview is scrubbing.

## Code tweens — `VMGFx.Animate` / `Timeline` / `Stagger`

For code-first motion (no clip asset), VMG ships an anime.js-style
fluent surface. These live on the same engine the clip system uses,
so behaviour stays consistent across authoring modes.

```csharp
using VMG.Animation.Core;
using static VMG.Animation.Core.VMGFx;

// Single tween — auto-plays on the next frame.
Animate(targetTransform)
    .To("localScale", new Vector3(1.2f, 1.2f, 1f))
    .Duration(0.4f)
    .Ease(VMGEase.OutBack)
    .Play();

// Timeline — sequenced moves with relative positions.
var tl = Timeline().Duration(1.2f);
tl.Animate(ring).To("Trim.end", 1f).Duration(0.7f);
tl.Animate(dot,  "<+=0.2").To("localScale", Vector3.one).Spring();
tl.Animate(burst, "-=0.1").To("color.a", 1f).Duration(0.3f);
tl.OnComplete(() => Debug.Log("done")).Play();

// Stagger — same builder applied per target with an offset.
Stagger(dots, (a, i) => a.To("localScale", Vector3.one).Duration(0.3f),
        step: 0.05f, from: VMGStaggerFrom.Center).Play();
```

The handle returned by `Animate(...)` / `Timeline(...)` exposes
`Play / Pause / Stop / Seek / Restart / Reset / Complete / Reverse /
Refresh / Revert / Cancel / PlaybackRate / Completion`. `Cancel`
ends the tween at its current value (use for toggle re-press);
`Revert` snaps channels back to the pre-animation baseline.

`Spring()` uses an analytic stiffness/damping/mass/velocity solver
(see `VMGEase.Spring`). `Refresh()` re-resolves `Func<T>`-driven
values; `RefreshOnLoop()` does it automatically per loop.
`AlongPath(asset|points)` + `AutoRotate(offsetDeg)` drive motion
paths with an arc-length LUT.

## Script DSL — `.vmgfx` text + `VMGAnimator.script`

The same engine is also drivable from a text file. Assign a `.vmgfx`
(or any TextAsset) to `VMGAnimator.script` and the hierarchy + tweens
build on `OnEnable`. The DSL maps 1:1 to the code API.

```vmgfx
add ring   circle    size=200,200 stroke=white,4 trim=true
add dot    circle    size=40,40   fill=cyan      pos=0,0   scale=0
add burst  polygon   sides=5      fill=yellow    pos=0,0   scale=0.5

timeline duration=1.2 {
  animate ring  -> Trim.end=1            duration=0.7 ease=outCubic
  animate dot   -> localScale=1,1,1      duration=0.4 at=<+=0.2 ease=spring(180,12)
  animate burst -> color.a=1             duration=0.3 at=-=0.1
  call BurstReady at=1.0
}

keyframes burst duration=0.6 ease=cubicBezier(0.4,0,0.2,1) {
  0%:   localScale=0.5,0.5,1
  50%:  localScale=1.3,1.3,1
  100%: localScale=1,1,1
}
```

Supported statements: `add`, `group { … }`, `animate`, `timeline { … }`,
`set`, `call`, `label`, `keyframes`, `motionPath`, `stagger`. Numeric
channels also accept generator helpers like `random(min,max[,seed])` and
`rangeInt(min,max[,seed])`.

### Playback toggles (script mode)

| Field | Effect |
|---|---|
| `script` (TextAsset) | The `.vmgfx` source. Compilation happens at OnEnable; the file's hash drives recompile so resaving the script picks up automatically. |
| `playOnEnable` | Runtime-only. Calls `Play()` once compilation finishes. No effect in Edit mode (ExecuteAlways guard) or External mode. |
| `loopScript` | Wraps `progress` 1→0 in Internal + script mode. The DSL's `loop` keyword inside a timeline / keyframes block sets the timeline's own iteration count to ∞, but the animator drives the timeline via normalized progress and would otherwise stop at 1 — so this animator-level toggle is the working knob. |

The `script` slot is an opaque `TextAsset`: any extension Unity
recognizes works (`.txt`, `.vmgfx`, etc.). `.vmgfx` is the
recommended convention — the package ships a `ScriptedImporter` that
imports `.vmgfx` files as plain TextAssets so they drop into the
slot directly.

## CSS `@keyframes` importer

`VMG.Animation.Serialization.VMGCssKeyframes.Translate(css, out warnings)`
takes self-contained CSS keyframe animations and emits `.vmgfx` text
ready for the script slot above. Designed for AE / Figma / Bodymovin
CSS exports — `transform`, `opacity`, color/border channels, with
W3C-spec cubic-bezier easing mapping.

Editor entry points:

- `Tools ▸ VMG ▸ Import CSS @keyframes…` — pick a `.css` on disk;
  the importer writes `<source>.vmgfx` next to it.
- `Tools ▸ VMG ▸ CSS → VMGFx Window` — paste CSS, Translate, Save
  the result. Warnings show inline.

```css
/* input */
@keyframes pop {
  0%, 100% { transform: scale(0); }
  50%      { transform: scale(var(--s, 1.1)); }
}
pop { animation: pop 0.6s ease infinite; }
```

```vmgfx
/* output (single channel example) */
keyframes pop duration=0.6 ease=cubicBezier(0.25,0.1,0.25,1) {
  0%:   localScale=0,0,1
  50%:  localScale=1.1,1.1,1
  100%: localScale=0,0,1
}
```

Selectors are simple-name only — `.dot` / `#dot` / `dot` all map to
a GameObject named `dot` under the animator root. Compound selectors
(`a:hover svg`, `.box .dot`) are warned and skipped — CSS selector
resolution against a GameObject tree is out of scope.

What it covers:

- `@keyframes` with percent, `from` / `to`, comma-grouped selectors.
- `animation` shorthand and all `animation-*` longhands.
- `transform`: `translate / translateX / translateY / scale / scaleX /
  scaleY / rotate / rotateZ`, combinable.
- `opacity`, `background-color`, `color`, `border-color`,
  `border-width`.
- Easing: keywords + `cubic-bezier(a,b,c,d)` + `steps(N)`.
- Units: `px / deg / s / ms / rad / turn / grad`; `rem / em` get a
  coarse 16px conversion.
- `var(--name, fallback)` resolved to the fallback when present;
  fallback-less `var()` is left raw so the parser surfaces a clear
  warning rather than silently dropping the function.

What's out of scope (by design, not pending work):

- HTML companion input. The importer is CSS-only — no cascade,
  no selector engine, no pseudo-class state, no per-element scoped
  CSS variables. Wild CodePen demos that rely on `:hover` /
  `nth-of-type` / per-element `--var` stagger should be trimmed to
  their `@keyframes` core; element-level effects are re-expressed
  via `VMGFx.Stagger` and timeline states.
- HSL colour, `calc()`, `transition`, `filter`, `transform-origin`,
  3D rotate (`rotateX` / `rotateY`), `matrix()`, `skew()`,
  `perspective()`. Layout properties (`display` / `margin` /
  `padding` / `flex` / `grid` / `width` / `height` / …) are on a
  known-but-dropped allowlist so they don't generate noise.

## Coding against the animator

```csharp
public class IntroSting : MonoBehaviour
{
    [SerializeField] VMGAnimator m_Animator;

    void Start() => m_Animator.Play();

    public async Task PlayOnceAsync()
    {
        await m_Animator.PlayAsync();
        // resumes after the clip's first 0 → 1 cycle
    }
}
```

UniTask equivalent (requires the optional `VMG.Runtime.Animation.UniTask`
assembly, gated by `VMG_UNITASK` which auto-activates when the
`com.cysharp.unitask` package is present):

```csharp
await m_Animator.PlayAsUniTask(token);
```

Wire animation events from code if you don't want them serialized:

```csharp
m_Animator.clip.events[0].invoke.AddListener(() => Debug.Log("hit"));
```

Or drive a specific time directly (External mode):

```csharp
m_Animator.mode = VMGPlayMode.External;
m_Animator.progress = 0.5f;       // 0..1
m_Animator.Sample(0.5f);          // forces an immediate write
```

## Record mode

Record turns the animator into a "sketch keys from the inspector"
tool, like Unity Animation's record button:

1. Toggle `● Record` (inspector or window).
2. Scrub the playhead to the time you want.
3. Change a value on the renderer (color, trim, slot intensity,
   etc.) in its inspector.
4. The animator captures the change as a new key on a track for
   that channel — creating the track if it doesn't exist yet. The
   new track gets a `t=0` baseline key holding the previous value
   so the interpolation is meaningful.

Record auto-stops when you press `Play`, enter Play mode, or
destroy the target.

## Edit-mode preview & restore

The inspector / window `Play` button uses an editor-only preview
loop (no scene reload, no AnimationMode flicker). The first time
preview runs it captures every bound channel's current value as a
"baseline." When you press `Stop`, the baseline is restored —
that's what "the pre-animation state of the hierarchy" means.

This means:

- You can scrub freely and the hierarchy snaps back on `Stop`.
- `Pause` does NOT restore — only `Stop`.
- Edit-mode preview writes ARE real writes; the scene goes dirty.
  Use `Stop` (or `Ctrl+Z`) to restore.

## Easing

Each key carries `(inTangent, outTangent)` cubic-bezier control
points in normalized `[0,1]` space (CSS `cubic-bezier(x1,y1,x2,y2)`
convention). The inspector exposes:

- A **preset dropdown** (Linear / Ease / EaseIn-Out / Quad / Cubic /
  Sine / Hold / Custom) that stamps tangents.
- A **mini preview** (single-key panel) showing the bezier curve
  for "this key → next key."
- An **Edit Curve…** popup with draggable control handles.

In multi-select mode the same UI applies to all selected keys
simultaneously (one Apply press stamps the template tangents onto
every selected key's outTangent and each one's next neighbour's
inTangent).

## Channel picker filter rules

The picker enumerates:

- `Transform.localPosition`, `localScale`, `localEulerAngles` (with
  `.x/.y/.z` subcomponents as float leaves).
- Every public or `[SerializeField]` field on every MonoBehaviour
  on the animator and its children.
- Walks into `struct` types recursively (max depth 16) so
  `ShapeStack.Slot0.shape.size.x` reaches a float leaf.
- Exposes `.x/.y/.z/.w/.r/.g/.b/.a` subcomponents of
  Vector2/3/4/Color leaves.
- Skips class-typed serialized fields (Unity's Animation window
  limitation applies here too).

## JSON import/export

The animator can serialize and load clips as JSON
(`VMGAnimationClipSerializer.Export` / `Import`). This is intended
for round-trip and backup, not for hand-authoring or external
tooling — the current format embeds the runtime field names
(`Trim.end`, `ShapeStack.Slot0.shape.size.x`) and uses
`AssemblyQualifiedName` for component types. UnityEvent persistent
calls are NOT round-tripped — only `(time, label)` is serialized;
rebind listeners in code after import.

For AE / Figma / Bodymovin handoff, prefer the CSS importer
(`Tools ▸ VMG ▸ Import CSS @keyframes…`) over hand-authoring JSON —
it produces `.vmgfx` script text the script slot consumes directly,
and the CSS surface lines up with what exporters already emit. The
JSON path here remains for round-trip backup.

## What VMGAnimator does NOT depend on

- `UnityEngine.Timeline`
- `UnityEngine.Playables` / `PlayableDirector`
- Unity's `Animator` (it coexists with one if you want, but it is
  not required)

The asmdef chain is `VMG.Runtime` ← `VMG.Runtime.Animation` ←
`VMG.Editor.Animation`. Nothing else.

## Limits and caveats

- Drag actions are grouped into a single Undo step (one `Ctrl+Z`
  per drag, not per per-delta tick).
- Snap is intrinsic per clip via `Snap (per second)` — defaults to
  60, meaning drag/scrub/right-click "Add Key Here" snap to 1/60
  second. Set 0 to disable snap entirely. Hold Shift while
  dragging or scrubbing to bypass snap temporarily.
- Inspectors *already open* on a child component during scrub may
  not refresh until you reselect them. Scene view repaints fine.
- Multi-select is for keys, not events. One event at a time.
- Pasting onto a track of an incompatible type is a hard skip with
  a warning — there's no silent fallback to "some other track."
- Stroke width does not follow `transform.scale`. Animating a vector
  shape's `localScale` to 0 collapses the fill but leaves the stroke
  outline at its mesh-baked thickness. Workarounds: disable the
  stroke on shapes that need to vanish, or animate `Stroke.width`
  alongside scale.
