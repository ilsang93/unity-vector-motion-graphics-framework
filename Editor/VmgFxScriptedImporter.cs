using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace VMG.EditorTools
{
    // Imports .vmgfx text files as TextAsset so they're droppable into
    // VMGAnimator.script (typed TextAsset). The file IS plain text — we
    // don't parse here; VMGFxScript.Compile runs at VMGAnimator.OnEnable.
    // Keeping the import side dumb means errors surface in the same place
    // they would for a TextAsset assigned manually, and the importer can
    // never go out of sync with the runtime compiler.
    [ScriptedImporter(version: 1, ext: "vmgfx", AllowCaching = true)]
    public sealed class VmgFxScriptedImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string text;
            try
            {
                text = File.ReadAllText(ctx.assetPath);
            }
            catch (Exception ex)
            {
                ctx.LogImportError($"VMG: .vmgfx read failed for {ctx.assetPath}: {ex.GetType().Name}: {ex.Message}");
                text = string.Empty;
            }

            var asset = new TextAsset(text);
            asset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
        }
    }
}
