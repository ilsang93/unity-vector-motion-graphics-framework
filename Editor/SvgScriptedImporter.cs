using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using VMG.Svg;

namespace VMG.EditorTools
{
    // Unity 6 ships a built-in Unity.VectorGraphics.Editor.SVGImporter
    // (in UnityEditor.VectorGraphicsModule). Two ScriptedImporters
    // claiming the same extension cause Unity to reject both. The PSD
    // importer solves the same problem by passing the contested extension
    // as `overrideExts` rather than `ext` — Unity then treats this importer
    // as an explicit override of the built-in one.
    [ScriptedImporter(version: 5, exts: null, overrideExts: new[] { "svg" }, AllowCaching = true)]
    public sealed class SvgScriptedImporter : ScriptedImporter
    {
        [Tooltip("Flip Y so SVG (top-down) maps cleanly to UGUI (bottom-up). " +
                 "Leave on for icons that should appear right-side-up in Unity.")]
        public bool flipY = true;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            VMGShapeAsset asset = null;
            try
            {
                string text = File.ReadAllText(ctx.assetPath);
                asset = SvgDocumentParser.Parse(text);
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"VMG: SVG parse threw {ex.GetType().Name}: {ex.Message}");
            }

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<VMGShapeAsset>();
                ctx.LogImportError($"VMG: parser returned null for {ctx.assetPath}");
            }

            if (flipY)
            {
                float h = asset.viewBoxSize.y;
                for (int s = 0; s < asset.subShapes.Count; s++)
                {
                    var sub = asset.subShapes[s];
                    for (int i = 0; i < sub.nodes.Count; i++)
                    {
                        var n = sub.nodes[i];
                        n.position = new Vector2(n.position.x, h - n.position.y);
                        n.inTangent = new Vector2(n.inTangent.x, -n.inTangent.y);
                        n.outTangent = new Vector2(n.outTangent.x, -n.outTangent.y);
                        sub.nodes[i] = n;
                    }
                }
            }

            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
            Debug.Log($"VMG: imported {ctx.assetPath} -> {asset.subShapes.Count} sub-shapes, viewBox {asset.viewBoxSize}");
        }
    }
}
