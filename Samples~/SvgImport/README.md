# SVG Import Sample

Drop `heart.svg` or `star.svg` anywhere in your Assets folder. The
`SvgScriptedImporter` converts them to `VMGShapeAsset` automatically.

## Usage

1. Create a Vector Image via `GameObject ▸ UI ▸ Vector Image`.
2. In the Inspector, assign the imported `.svg` to the **Svg Asset** field.
3. The shape draws scaled to the RectTransform, fit-to-rect, using each
   sub-shape's own fill / stroke from the SVG.

When `Svg Asset` is assigned, the procedural shape and modifier fields are
ignored. The `Graphic.color` still tints the result.

## Supported SVG subset (MVP)

| Element | Notes |
| --- | --- |
| `<path d="...">` | M/m L/l H/h V/v C/c S/s Q/q T/t A/a Z/z. Arcs converted to cubic. |
| `<rect>` | including `rx` / `ry` rounded corners |
| `<circle>` `<ellipse>` | cubic-bezier kappa approximation |
| `<line>` `<polyline>` `<polygon>` | as-is |
| `<g>` `<svg>` | container only, used for inheritable style/transform |
| `viewBox`, `width`, `height` | normalization |
| `transform="..."` | matrix / translate / scale / rotate / skewX / skewY |
| Presentation attrs | fill, fill-opacity, stroke, stroke-opacity, stroke-width, stroke-linecap, stroke-linejoin, opacity |
| Inline `style="..."` | overrides the above |

## Not yet supported

Gradients, patterns, filters, text, masks/clip paths, CSS classes,
`<use>` / `<symbol>` references, animations.

## Runtime parsing

```csharp
using VMG.Svg;

string svg = File.ReadAllText(path);
VMGShapeAsset asset = SvgDocumentParser.Parse(svg);
vectorImage.SvgAsset = asset;
```
