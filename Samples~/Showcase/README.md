# Showcase

The advanced counterpart to **AnimatorSample**. One `.vmgfx` script
(`showcase.vmgfx`) exercises the full VMGFx DSL surface so you can
see — in a single playable scene — what the package can do.

## Quick start

1. **Create > UI > Vector Image** in a Canvas (this becomes the
   showcase root).
2. **Add Component > VMG > VMG Animator**.
3. On the animator:
   - Set **Mode** to `Internal`.
   - Drag `showcase.vmgfx` into the **Script** slot.
   - Enable **Play On Enable**.
4. Hit **Play**. The animator builds the scene itself (stage, ring,
   hero, eight orbiters, four corner bars, satellite) and runs a
   6-second timeline on loop.

No prebuilt scene is required — VMGAnimator parses the script and
spawns the children at first run.

## What's in the script

| DSL feature              | Where in `showcase.vmgfx`                       |
|--------------------------|-------------------------------------------------|
| `add` + named children   | Stage, ring, hero, satellite                    |
| `group { ... }`          | `orbiters`, `bars`                              |
| `timeline duration loop alternate` | Outer 6 s timeline                    |
| `label` + `at=label+0.2` | `barsIn`, `heroIn`, `orbitIn`, `satOut`         |
| `animate ... from=`      | Stage pop-in, ring trim, satellite stroke       |
| Spring ease              | `ease=spring(180,12,1,0)` on hero               |
| Cubic-bezier ease        | `ease=cubicBezier(0.25,0.1,0.25,1)` on orbiters |
| `keyframes` (multi-stop) | Hero colour/stroke + satellite orbit + spins    |
| `stagger group/* step=` from `first` / `center` | Bars, orbiters     |
| Stagger with seed        | `seed=11` on orbiters                           |
| Stagger with multiple child statements (0.35.0+) | Orbiters pop + colour + stroke all in lockstep via `at=<<` |
| `random(...)` value      | Orbiter `Fill.color` picks from a palette       |
| `loop=N alternate` on a single animate | Orbiter rhythmic pulse              |
| Trim channel             | Ring drawing itself in                          |
| RoundCorners channel     | Stage corner-radius pop                         |
| `call eventName at=...`  | `showcaseLoopDone` at the end                   |

## Notes

### Satellite uses keyframes, not motionPath

The satellite's circular orbit is authored as an 8-stop `keyframes`
block (`localPosition` at 0%, 12.5%, …, 100%) rather than a
`motionPath`. `motionPath` on `RectTransform` works since 0.34.0 and
would also fit here — the keyframes version is kept as a "keyframes
can do anything" demo, and is also the right tool when you want
explicit per-stop timing or to tweak a single waypoint without
re-authoring a `points=` list.

### Orbiters don't self-rotate

The orbiters are circles, so a self-rotation would be invisible. The
showcase keeps the rhythmic scale pulse (`loop=4 alternate`) and
skips the rotation — see the `hero` and `satellite` blocks for what a
keyframed `localEulerAngles.z` spin looks like.

### Why `loop` on a timeline but not on `keyframes`

Inside a timeline, `keyframes` blocks can't loop independently —
their `loop` attr is ignored. The outer timeline's `loop alternate`
replays the whole composition, which is the intended pattern: compose
once, loop the whole.

## See also

- `Samples~/AnimatorSample/` — minimal `pulse.vmgfx` + `intro-card.vmgfx`
- `Documentation~/animator.md` — full VMGFx DSL reference
