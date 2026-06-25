using UnityEngine;
using UnityEditor;
using VMG.Text;

namespace VMG.EditorTools
{
    /// Inspector + Scene-view handles for the vector-text components. Covers
    /// both VMGVectorTextUGUI and VMGVectorTextWorld via the shared base.
    ///
    /// The headline feature here is the Grid (free-form envelope) warp: it
    /// draws a draggable handle per control point in the Scene view so the
    /// text can be distorted PowerPoint-WordArt style. Control points are
    /// stored normalized (0..1) in the warp; handles map them through the
    /// component transform + pre-warp text bounds to world space.
    [CanEditMultipleObjects]
    [CustomEditor(typeof(VMGVectorTextBase), true)]
    public sealed class VMGVectorTextEditor : Editor
    {
        // Names of the warp's 36 flat control-point fields + the init flag.
        // Drawn under a collapsible foldout (not [HideInInspector], which would
        // also strip them from the Animation window).
        private static readonly System.Collections.Generic.HashSet<string> s_gridPointNames = BuildGridPointNames();
        private bool m_PointsFoldout;

        private static System.Collections.Generic.HashSet<string> BuildGridPointNames()
        {
            var set = new System.Collections.Generic.HashSet<string> { "gridInitialized" };
            for (int i = 0; i < VMGTextWarp.MaxPts; i++) set.Add("p" + i.ToString("D2"));
            return set;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var warpProp = serializedObject.FindProperty("Warp");
            bool isGrid = warpProp != null
                && warpProp.FindPropertyRelative("mode") is SerializedProperty mp
                && mp.enumValueIndex == (int)WarpMode.Grid;

            // Draw every serialized property, but route the Warp struct through
            // a custom drawer that tucks the 36 grid control-points into a
            // foldout (keeping the inspector tidy while leaving them animatable).
            var it = serializedObject.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (it.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                    continue;
                }
                if (it.propertyPath == "Warp") { DrawWarp(it, isGrid); continue; }
                EditorGUILayout.PropertyField(it, true);
            }

            if (isGrid) DrawGridControls();

            DrawBakeControls();

            if (serializedObject.ApplyModifiedProperties())
                PushAllTargets(); // value edits (cols/rows/points) → immediate redraw
        }

        // Bake = embed the source font's raw bytes on the component so a BUILD
        // can parse glyph outlines without the TMP font asset's source file
        // (which is frequently null at runtime). The editor already auto-caches
        // bytes on every rebuild, so this is mostly a guarantee + status read;
        // the build pre-process hook also auto-bakes anything still empty.
        private void DrawBakeControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build Bake", EditorStyles.boldLabel);

            bool allBaked = true;
            foreach (var o in targets)
                if (o is VMGVectorTextBase c && !c.HasBakedFontBytes) allBaked = false;

            if (targets.Length == 1 && target is VMGVectorTextBase single)
            {
                int kb = single.FontBytes != null ? single.FontBytes.Length / 1024 : 0;
                EditorGUILayout.HelpBox(
                    single.HasBakedFontBytes
                        ? $"Font bytes embedded ({kb} KB) — this text will render in a build."
                        : "No font bytes embedded yet. They auto-cache on edit; bake to guarantee a build renders.",
                    single.HasBakedFontBytes ? MessageType.Info : MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    allBaked ? "All selected: font bytes embedded." : "Some selected have no embedded font bytes.",
                    allBaked ? MessageType.Info : MessageType.Warning);
            }

            if (GUILayout.Button("Bake Font Bytes"))
            {
                foreach (var o in targets)
                {
                    if (o is VMGVectorTextBase c)
                    {
                        Undo.RecordObject(c, "Bake Font Bytes");
                        if (c.BakeFontBytes())
                        {
                            EditorUtility.SetDirty(c);
                            c.EditorRebuildAndPush();
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[VMGVectorText] Bake failed on '{c.name}': could not resolve a parseable " +
                                "source font (.ttf/.otf) for its TMP font, or the font has no vector outlines.", c);
                        }
                    }
                }
                RepaintScene();
            }
        }

        private void DrawWarp(SerializedProperty warp, bool isGrid)
        {
            EditorGUILayout.PropertyField(warp, false); // foldout header
            if (!warp.isExpanded) return;

            var modeProp = warp.FindPropertyRelative("mode");
            var mode = modeProp != null ? (WarpMode)modeProp.enumValueIndex : WarpMode.None;

            EditorGUI.indentLevel++;
            var child = warp.Copy();
            var end = warp.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                string name = child.name;
                if (s_gridPointNames.Contains(name)) continue; // grouped below
                // Grid cols/rows only matter in Grid mode; everything else is
                // driven by amount/secondary.
                if ((name == "gridCols" || name == "gridRows") && mode != WarpMode.Grid) continue;
                // Relabel/hide the generic "secondary" field per mode so the
                // inspector reads in the warp's own terms.
                if (name == "secondary")
                {
                    if (!ModeUsesSecondary(mode)) continue; // Arc/Trapezoid/None/Grid don't use it
                    EditorGUILayout.PropertyField(child, new GUIContent(SecondaryLabel(mode), child.tooltip), true);
                    continue;
                }
                EditorGUILayout.PropertyField(child, true);
            }

            // Collapsible block for the 36 control points (Grid mode only).
            if (isGrid)
            {
                m_PointsFoldout = EditorGUILayout.Foldout(m_PointsFoldout, "Control Points (animatable)", true);
                if (m_PointsFoldout)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < VMGTextWarp.MaxPts; i++)
                    {
                        var p = warp.FindPropertyRelative("p" + i.ToString("D2"));
                        if (p != null) EditorGUILayout.PropertyField(p, true);
                    }
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUI.indentLevel--;
        }

        // Only Circle (sweep degrees) and Wave (crest count) read the warp's
        // `secondary` knob; Arc / Trapezoid / None / Grid ignore it.
        private static bool ModeUsesSecondary(WarpMode mode)
            => mode == WarpMode.Circle || mode == WarpMode.Wave;

        private static string SecondaryLabel(WarpMode mode)
        {
            switch (mode)
            {
                case WarpMode.Circle: return "Sweep (degrees)";
                case WarpMode.Wave:   return "Crests";
                default:              return "Secondary";
            }
        }

        private void DrawGridControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grid Warp", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag the control-point handles in the Scene view to distort the text. " +
                "Changing rows/cols resets the grid.", MessageType.Info);

            if (GUILayout.Button("Reset Grid"))
            {
                foreach (var o in targets)
                {
                    var c = (VMGVectorTextBase)o;
                    Undo.RecordObject(c, "Reset Text Grid");
                    var wc = c.Warp; wc.ResetGrid(); c.Warp = wc;
                    EditorUtility.SetDirty(c);
                    c.EditorRebuildAndPush();
                }
                RepaintScene();
            }
        }

        // Force every selected component to rebuild + re-push now and repaint,
        // so inspector value edits show up without waiting for an Update tick.
        private void PushAllTargets()
        {
            foreach (var o in targets)
                if (o is VMGVectorTextBase c) c.EditorRebuildAndPush();
            RepaintScene();
        }

        private static void RepaintScene()
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void OnSceneGUI()
        {
            var comp = (VMGVectorTextBase)target;
            if (comp.Warp.mode != WarpMode.Grid) return;
            if (!comp.TryGetPreWarpBounds(out var b)) return;

            // Materialize the grid so handles exist on first selection. Warp
            // is a struct field; write the ensured copy back so it sticks
            // (EnsureGrid on the field's value-copy alone is lost).
            var warp = comp.Warp;
            if (warp.EnsureGrid()) { comp.Warp = warp; EditorUtility.SetDirty(comp); }

            int cols = warp.CtrlCols;
            int rows = warp.CtrlRows;
            Transform tr = comp.transform;
            float w = Mathf.Max(b.width, 1e-4f);
            float h = Mathf.Max(b.height, 1e-4f);

            // Draw grid lines between adjacent active control points.
            Handles.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    Vector3 p = CtrlToWorld(warp.GetPt(VMGTextWarp.Slot(c, r)), b, w, h, tr);
                    if (c + 1 < cols) Handles.DrawLine(p, CtrlToWorld(warp.GetPt(VMGTextWarp.Slot(c + 1, r)), b, w, h, tr));
                    if (r + 1 < rows) Handles.DrawLine(p, CtrlToWorld(warp.GetPt(VMGTextWarp.Slot(c, r + 1)), b, w, h, tr));
                }

            // Draggable handle per active control point.
            float size = HandleUtility.GetHandleSize(tr.position) * 0.06f;
            bool changed = false;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int slot = VMGTextWarp.Slot(c, r);
                    Vector3 world = CtrlToWorld(warp.GetPt(slot), b, w, h, tr);
                    Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.DotHandleCap);
                    if (moved != world)
                    {
                        Undo.RecordObject(comp, "Move Text Grid Point");
                        Vector3 local = tr.InverseTransformPoint(moved);
                        warp.SetPt(slot, new Vector2((local.x - b.minX) / w, (local.y - b.minY) / h));
                        changed = true;
                    }
                }
            if (changed)
            {
                comp.Warp = warp;
                EditorUtility.SetDirty(comp);
                comp.EditorRebuildAndPush(); // reflect the drag immediately
            }
        }

        private static Vector3 CtrlToWorld(Vector2 norm, in VMGTextWarp.Bounds2D b, float w, float h, Transform tr)
        {
            Vector3 local = new Vector3(b.minX + norm.x * w, b.minY + norm.y * h, 0f);
            return tr.TransformPoint(local);
        }
    }
}
