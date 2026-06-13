using UnityEditor;
using UnityEngine;
using VMG.Svg;

namespace VMG.EditorTools
{
    /// Shared inspector drawer for any "VMGShapeAsset SvgAsset" field. Draws
    /// the standard ObjectField, but additionally accepts .svg files (and
    /// Unity assets backed by .svg, e.g. VectorImage / Sprite produced by
    /// the built-in SVGImporter) via drag-and-drop. When such a drop happens,
    /// a sibling `<name>.vmgshape.asset` sidecar is created (or refreshed)
    /// and assigned to the slot.
    public static class SvgAssetSlotGUI
    {
        const string SlotLabel = "Svg Asset";
        const string HelpMsg = "Tip: drop a .svg here and a sibling " +
                               "<name>.vmgshape.asset is generated automatically. " +
                               "Editing the .svg keeps the sidecar in sync.";

        public static void Draw(SerializedProperty svgAssetProp)
        {
            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            DrawAt(rect, svgAssetProp);

            // Compact one-time-ish hint — only show when the slot is empty so
            // it doesn't clutter the inspector once the user has set things up.
            if (svgAssetProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(HelpMsg, MessageType.None);
            }
        }

        static void DrawAt(Rect rect, SerializedProperty svgAssetProp)
        {
            var evt = Event.current;
            bool overSlot = rect.Contains(evt.mousePosition);

            if (overSlot && (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform))
            {
                Object resolved = TryResolveDrag();
                if (resolved != null)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        svgAssetProp.objectReferenceValue = resolved;
                        svgAssetProp.serializedObject.ApplyModifiedProperties();
                        GUI.changed = true;
                    }
                    evt.Use();
                    return;
                }
                // No SVG in payload — fall through to default ObjectField, which
                // handles direct VMGShapeAsset drops natively.
            }

            EditorGUI.PropertyField(rect, svgAssetProp, new GUIContent(SlotLabel));
        }

        /// Looks at DragAndDrop.objectReferences and DragAndDrop.paths. If any
        /// entry is a .svg file (Project asset or a Unity asset whose path ends
        /// in .svg, e.g. a Sprite/VectorImage backed by the built-in importer),
        /// produces or refreshes the sidecar and returns it.
        static Object TryResolveDrag()
        {
            // Object references first (covers drops from Project window of any
            // type whose underlying file is .svg).
            var objs = DragAndDrop.objectReferences;
            if (objs != null)
            {
                for (int i = 0; i < objs.Length; i++)
                {
                    var o = objs[i];
                    if (o == null) continue;
                    if (o is VMGShapeAsset shape) return shape;
                    string p = AssetDatabase.GetAssetPath(o);
                    if (!string.IsNullOrEmpty(p) && p.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var sidecar = SvgSidecarFactory.EnsureSidecarFor(p);
                        if (sidecar != null) return sidecar;
                    }
                }
            }

            // External file drag (OS drop into Project). DragAndDrop.paths is
            // populated even before the file is imported as an asset.
            var paths = DragAndDrop.paths;
            if (paths != null)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    string p = paths[i];
                    if (!string.IsNullOrEmpty(p) && p.EndsWith(".svg", System.StringComparison.OrdinalIgnoreCase))
                    {
                        var sidecar = SvgSidecarFactory.EnsureSidecarFor(p);
                        if (sidecar != null) return sidecar;
                    }
                }
            }
            return null;
        }
    }
}
