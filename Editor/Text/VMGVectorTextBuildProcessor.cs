using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VMG.Text;

namespace VMG.EditorTools
{
    /// Build-time safety net for the vector-text feature. The runtime renders
    /// glyph outlines by re-parsing the font bytes embedded on each
    /// VMGVectorText component (m_FontBytes). The editor auto-caches those
    /// bytes on every rebuild, so a component that was ever shown/edited ships
    /// fine. The footgun is a component added and the scene saved WITHOUT the
    /// editor ever rebuilding it — it would ship with empty bytes and render
    /// nothing in the build.
    ///
    /// This pre-process pass scans every scene in the build for vector-text
    /// components with no embedded bytes and bakes them automatically, so a
    /// build "just works" without anyone clicking the Bake button. Prefabs are
    /// covered transitively: a scene instance inherits the prefab's serialized
    /// bytes (and gets baked here if still empty); an unopened prefab not
    /// placed in any build scene contributes nothing to the build anyway.
    ///
    /// Only scenes enabled in Build Settings are scanned. Each is opened
    /// additively, baked, saved if changed, then closed — leaving the user's
    /// open scenes untouched.
    public sealed class VMGVectorTextBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            int baked = 0, failed = 0;

            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!s.enabled || string.IsNullOrEmpty(s.path)) continue;

                bool alreadyOpen = false;
                var open = EditorSceneManager.GetSceneByPath(s.path);
                if (open.IsValid() && open.isLoaded) alreadyOpen = true;

                Scene scene = alreadyOpen
                    ? open
                    : EditorSceneManager.OpenScene(s.path, OpenSceneMode.Additive);

                bool changed = BakeSceneRoots(scene, ref baked, ref failed);

                if (changed)
                    EditorSceneManager.SaveScene(scene);
                if (!alreadyOpen)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }

            if (baked > 0)
                Debug.Log($"[VMGVectorText] Build pre-process baked font bytes into {baked} vector-text " +
                          "component(s) so they render in the build.");
            if (failed > 0)
                Debug.LogWarning($"[VMGVectorText] Build pre-process: {failed} vector-text component(s) could " +
                                 "not be baked (no resolvable TrueType source font). They will render nothing " +
                                 "in the build. Assign a .ttf-backed TMP font.");
        }

        private static bool BakeSceneRoots(Scene scene, ref int baked, ref int failed)
        {
            bool changed = false;
            var roots = scene.GetRootGameObjects();
            var comps = new List<VMGVectorTextBase>();
            foreach (var root in roots)
            {
                comps.Clear();
                root.GetComponentsInChildren(true, comps);
                foreach (var c in comps)
                {
                    if (c.HasBakedFontBytes) continue;
                    if (c.BakeFontBytes())
                    {
                        EditorUtility.SetDirty(c);
                        baked++;
                        changed = true;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }
            return changed;
        }
    }
}
