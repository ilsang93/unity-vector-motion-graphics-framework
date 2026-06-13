using System.IO;
using UnityEditor;
using UnityEngine;
using VMG.Svg;

namespace VMG.EditorTools
{
    /// Editor-only helper that converts a .svg file on disk into a sibling
    /// `<name>.vmgshape.asset` containing a VMGShapeAsset. Lets users drop
    /// .svg files (or Unity VectorImage / Sprite assets backed by .svg)
    /// straight into VMG renderer slots while keeping Unity's built-in SVG
    /// importer untouched.
    ///
    /// Sidecar lives next to the .svg so it's visible and version-controllable.
    /// Stale-detection compares the file's last-write-time stamps; the .svg
    /// being newer than the sidecar triggers a reconvert.
    public static class SvgSidecarFactory
    {
        const string SidecarSuffix = ".vmgshape.asset";

        /// Returns the sibling sidecar asset for the given .svg path, creating
        /// or refreshing it as needed. Returns null if the path isn't a .svg
        /// or the file can't be read.
        public static VMGShapeAsset EnsureSidecarFor(string svgPath)
        {
            if (string.IsNullOrEmpty(svgPath)) return null;
            if (!svgPath.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(svgPath)) return null;

            string sidecarPath = GetSidecarPath(svgPath);
            bool needRebuild = !File.Exists(sidecarPath)
                               || File.GetLastWriteTimeUtc(svgPath) > File.GetLastWriteTimeUtc(sidecarPath);

            VMGShapeAsset existing = AssetDatabase.LoadAssetAtPath<VMGShapeAsset>(sidecarPath);
            if (existing != null && !needRebuild) return existing;

            string text;
            try { text = File.ReadAllText(svgPath); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VMG SVG] Couldn't read '{svgPath}': {e.Message}");
                return existing;
            }

            VMGShapeAsset parsed = SvgDocumentParser.Parse(text);
            if (parsed == null)
            {
                Debug.LogWarning($"[VMG SVG] Parser returned null for '{svgPath}'; sidecar not updated.");
                return existing;
            }
            // Match SvgScriptedImporter's default Y-flip so the sidecar looks
            // the same as a normally-imported .svg would.
            FlipY(parsed);

            if (existing != null)
            {
                // Overwrite in place so any inspector slot already pointing at
                // the sidecar keeps the same object reference.
                EditorUtility.CopySerialized(parsed, existing);
                Object.DestroyImmediate(parsed);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }
            else
            {
                AssetDatabase.CreateAsset(parsed, sidecarPath);
                AssetDatabase.SaveAssets();
                return parsed;
            }
        }

        public static string GetSidecarPath(string svgPath)
        {
            string dir = Path.GetDirectoryName(svgPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(svgPath);
            // Use forward slashes — AssetDatabase APIs expect them.
            return (dir.Length > 0 ? dir.Replace('\\', '/') + "/" : "") + name + SidecarSuffix;
        }

        public static bool IsSidecarPath(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath)
                   && assetPath.EndsWith(SidecarSuffix, System.StringComparison.OrdinalIgnoreCase);
        }

        /// Mirrors SvgScriptedImporter.flipY default-true behaviour so sidecars
        /// produced via drag-and-drop look identical to ScriptedImporter output.
        static void FlipY(VMGShapeAsset asset)
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
    }
}
