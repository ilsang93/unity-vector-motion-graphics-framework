using System.IO;
using UnityEditor;

namespace VMG.EditorTools
{
    /// When a .svg with an existing sibling sidecar gets re-imported by Unity,
    /// regenerate the sidecar so it stays in sync. Does nothing for .svg files
    /// that don't have a sidecar yet — those are created on demand by the
    /// inspector drag handler in SvgSidecarFactory.
    public sealed class SvgSidecarPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            for (int i = 0; i < imported.Length; i++)
            {
                string path = imported[i];
                if (!path.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase)) continue;
                string sidecar = SvgSidecarFactory.GetSidecarPath(path);
                if (!File.Exists(sidecar)) continue;
                SvgSidecarFactory.EnsureSidecarFor(path);
            }
        }
    }
}
