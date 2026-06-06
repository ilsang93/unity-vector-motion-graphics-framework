# Tween Integration Sample

DOTween extension demo for VMG.

## Prerequisites

- DOTween installed (asset store package or UPM mirror).
- DOTween asmdef created (run **Tools ▸ Demigiant ▸ DOTween Utility Panel ▸
  Create ASMDEF...** once).
- `VMG_DOTWEEN` scripting define symbol — auto-defined when the DOTween UPM
  package is detected; otherwise add it to **Project Settings ▸ Player ▸
  Scripting Define Symbols**.

## Usage

Drop `VMGTweenDemo` on any GameObject with a `VectorImageGraphic`. The demo
plays a draw-on, a stroke pulse, and a color shift on Start.

## API surface

All extension methods sit in the `VMG.Tween` namespace and become available
once `using VMG.Tween;` is added.

```csharp
using VMG.Tween;

vectorImage.DOFade(0f, 0.4f).SetEase(Ease.OutQuad);
vectorImage.DOTrim(1f, 0.8f);
vectorImage.DOStrokeColor(Color.red, 0.5f);
vectorImage.DOSize(new Vector2(300, 300), 0.6f);
vectorImage.DOCornerRadius(40f, 0.3f);
```

## UniTask interop

If DOTween's UniTask integration is enabled in the consuming project, all
tweens are awaitable out of the box:

```csharp
await vectorImage.DOTrim(1f, 0.8f).AsyncWaitForCompletion();
await vectorImage.DOFade(0f, 0.4f).WithCancellation(token);
```

No VMG-specific adapter required.
