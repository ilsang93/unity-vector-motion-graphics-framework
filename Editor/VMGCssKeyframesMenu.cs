using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VMG.Animation.Serialization;

namespace VMG.EditorTools
{
    // Editor entry points for VMGCssKeyframes.Translate.
    //   • Tools/VMG/Import CSS @keyframes…     — file dialog, writes .vmgfx next to source
    //   • Tools/VMG/CSS → VMGFx Window         — paste-in window for ad-hoc tests
    internal static class VMGCssKeyframesMenu
    {
        [MenuItem("Tools/VMG/Import CSS @keyframes…", false, 100)]
        private static void ImportCssFile()
        {
            var path = EditorUtility.OpenFilePanel("Pick CSS file", Application.dataPath, "css");
            if (string.IsNullOrEmpty(path)) return;

            var css = File.ReadAllText(path);
            var vmgfx = VMGCssKeyframes.Translate(css, out var warnings);
            ReportWarnings(warnings, Path.GetFileName(path));

            var outPath = Path.ChangeExtension(path, ".vmgfx");
            File.WriteAllText(outPath, vmgfx);
            Debug.Log($"VMGCssKeyframes: wrote {outPath} ({vmgfx.Length} chars)");

            if (outPath.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/')))
                AssetDatabase.Refresh();
        }

        private static void ReportWarnings(List<string> warnings, string label)
        {
            if (warnings == null || warnings.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("VMGCssKeyframes warnings for ").Append(label).Append(':').AppendLine();
            foreach (var w in warnings) sb.Append("  • ").AppendLine(w);
            Debug.LogWarning(sb.ToString());
        }

        public sealed class Window : EditorWindow
        {
            [MenuItem("Tools/VMG/CSS → VMGFx Window", false, 101)]
            private static void Open()
            {
                var win = GetWindow<Window>("CSS → VMGFx");
                win.minSize = new Vector2(640f, 480f);
            }

            [SerializeField] private string _css = "";
            [SerializeField] private string _vmgfx = "";
            [SerializeField] private string _warnings = "";
            private Vector2 _cssScroll;
            private Vector2 _outScroll;

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Paste CSS (with @keyframes)", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_cssScroll, GUILayout.Height(position.height * 0.45f)))
                {
                    _cssScroll = sv.scrollPosition;
                    _css = EditorGUILayout.TextArea(_css, GUILayout.ExpandHeight(true));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Translate", GUILayout.Width(120f))) Translate();
                    GUI.enabled = !string.IsNullOrEmpty(_vmgfx);
                    if (GUILayout.Button("Save .vmgfx…", GUILayout.Width(120f))) Save();
                    if (GUILayout.Button("Copy output", GUILayout.Width(120f))) EditorGUIUtility.systemCopyBuffer = _vmgfx;
                    GUI.enabled = true;
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Clear", GUILayout.Width(80f))) { _css = _vmgfx = _warnings = ""; }
                }

                EditorGUILayout.LabelField("Output .vmgfx", EditorStyles.boldLabel);
                using (var sv = new EditorGUILayout.ScrollViewScope(_outScroll, GUILayout.ExpandHeight(true)))
                {
                    _outScroll = sv.scrollPosition;
                    EditorGUILayout.TextArea(_vmgfx, GUILayout.ExpandHeight(true));
                }

                if (!string.IsNullOrEmpty(_warnings))
                {
                    EditorGUILayout.HelpBox(_warnings, MessageType.Warning);
                }
            }

            private void Translate()
            {
                _vmgfx = VMGCssKeyframes.Translate(_css ?? "", out var warnings);
                _warnings = (warnings == null || warnings.Count == 0)
                    ? ""
                    : string.Join("\n", warnings.ConvertAll(w => "• " + w));
                Repaint();
            }

            private void Save()
            {
                var path = EditorUtility.SaveFilePanel("Save .vmgfx", Application.dataPath, "imported", "vmgfx");
                if (string.IsNullOrEmpty(path)) return;
                File.WriteAllText(path, _vmgfx);
                if (path.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/')))
                    AssetDatabase.Refresh();
                Debug.Log($"VMGCssKeyframes: wrote {path}");
            }
        }
    }
}
