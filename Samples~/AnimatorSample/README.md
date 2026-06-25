# Animator Sample

Two `.vmgfx` scripts that show how to drive `VMGAnimator` from a text
asset. No `AnimationClip`, no `PlayableDirector` — the animator parses
the script and runs the result against the GameObject hierarchy.

## Quick start

1. **Create > UI > Vector Image** in a Canvas.
2. **Add Component > VMG > VMG Animator**.
3. On the animator:
   - Set **Mode** to `Internal`.
   - Drag `pulse.vmgfx` (or `intro-card.vmgfx`) into the **Script** slot.
   - Enable **Play On Enable** for instant playback, or call `Play()`
     from code / a button.
4. Hit **Play** in the Editor. The vector image animates.

For looping playback (good for the breathing pulse), enable
**Loop Script** on the animator alongside Internal mode.

## Files

### `pulse.vmgfx`
Minimal: one `keyframes` block ping-pongs `localScale` + `Fill.color`
on the animator's own GameObject (target `self`). No children are
spawned. Drop on any vector renderer to see it breathe.

### `intro-card.vmgfx`
A 2-second logo intro that exercises the four most-used DSL pieces:

| Block      | Purpose                                                |
|------------|--------------------------------------------------------|
| `add`      | Spawns child vector renderers at authored positions    |
| `group`    | Bundles children so `stagger group/*` can target them  |
| `timeline` | Orchestrates concurrent tweens with labels + offsets   |
| `stagger`  | Fans one tween across N children with a step delay     |

It builds a rounded "card" backdrop, a headline bar, and three dots —
then plays them in with springs, easing, and a staggered pop. The
final `call introDone at=2` shows the event-hook syntax (wire a
matching UnityEvent on the animator's bindings to receive it).

### `gradient-wiggle.vmgfx`
Showcases the 0.42.0 additions:

- **Gradient fill / stroke.** `fill=linear-gradient(90,#10141c,#2a3550)`
  and `fill=radial-gradient(#5ee0ff,#1b4a6b)`; a gradient-stroked ring
  via `stroke=linear-gradient(0,#ff5ea8,#ffd166),6` (color list first,
  width last). Angle is in degrees (0 = left→right, 90 = bottom→top);
  optional for linear, ignored for radial.
- **AE-style line Wiggle.** `wiggle=intensity[,frequency[,seed]]`
  ripples the outline every frame. The path is resampled to a dense
  spacing first, so even a rect/rounded-rect ripples *along its edges*
  rather than sloshing as a whole, and the noise is keyed by arc length
  so the line stays smooth (no spikes). Two outlines use different
  intensities/frequencies/seeds so they ripple independently. Fine
  control over ripple resolution (`spacing`) and wavelength
  (`spatialScale`) lives in the inspector.

Enable **Loop Script** to see the card keep breathing; the wiggle runs
continuously on its own regardless of the timeline.

## DSL primer

A `.vmgfx` file is just text. The most-used statements:

```
# Comments start with # or //

# Spawn children
add myShape circle size=80 fill=#ff5ea8 position=0,0

# Animate one channel
animate myShape Fill.color -> #ffffff duration=0.4 ease=outQuad

# Animate multiple channels at fixed timestamps
keyframes myShape duration=1 ease=easeInOutSine {
  0%:   localScale=1,1
  50%:  localScale=1.2,1.2
  100%: localScale=1,1
}

# Compose multiple animations on one shared clock
timeline duration=2 loop {
  animate myShape Stroke.width -> 8 duration=0.5 at=0
  animate myShape Fill.color -> #5ee0ff duration=0.5 at=0.5
}
```

Targets:

- `self` (aliases `root`, `/`, or empty) — the GameObject the
  VMGAnimator lives on.
- A bare name — looked up via `add`'s named children, then any
  child Transform under the animator's root (supports `group/child`
  paths).
- `name.transform` — explicitly route to the Transform component
  instead of the renderer.

Paths whose first segment is `localPosition` / `localScale` /
`localRotation` / `localEulerAngles` always route to the Transform
automatically (no `.transform` suffix needed).

See the full DSL reference in `Documentation~/animator.md`.
