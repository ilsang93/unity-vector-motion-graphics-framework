using UnityEditor;
using UnityEngine;
using VMG.Core;
using VMG.World;

namespace VMG.EditorTools
{
    [CustomEditor(typeof(VectorSpriteRenderer))]
    [CanEditMultipleObjects]
    internal sealed class VectorSpriteRendererEditor : Editor
    {
        // Active slot for SceneView editing (0..3). Editor instance
        // state, so picking a different object resets it.
        private int m_ActiveSlot;

        private SerializedProperty m_SvgAsset;
        private SerializedProperty m_SvgUnitsPerWorldUnit;
        private SerializedProperty m_ShapeStack;
        private SerializedProperty m_Stroke;
        private SerializedProperty m_Fill;
        private SerializedProperty m_Depth;
        private SerializedProperty m_RoundCorners;
        private SerializedProperty m_Trim;
        private SerializedProperty m_Wiggle;
        private SerializedProperty m_Tint;
        private SerializedProperty m_Material;
        private SerializedProperty m_Texture;
        private SerializedProperty m_MergeStrokeOverlaps;
        private SerializedProperty m_GpuTrimCustomMaterial;
        private SerializedProperty m_SortingLayerID;
        private SerializedProperty m_SortingOrder;
        private SerializedProperty m_SvgFillOverride;
        private SerializedProperty m_SvgStrokeOverride;

        private void OnEnable()
        {
            m_SvgAsset = serializedObject.FindProperty("SvgAsset");
            m_SvgUnitsPerWorldUnit = serializedObject.FindProperty("SvgUnitsPerWorldUnit");
            m_ShapeStack = serializedObject.FindProperty("ShapeStack");
            m_Stroke = serializedObject.FindProperty("Stroke");
            m_Fill = serializedObject.FindProperty("Fill");
            m_Depth = serializedObject.FindProperty("Depth");
            m_RoundCorners = serializedObject.FindProperty("RoundCorners");
            m_Trim = serializedObject.FindProperty("Trim");
            m_Wiggle = serializedObject.FindProperty("Wiggle");
            m_Tint = serializedObject.FindProperty("Tint");
            m_Material = serializedObject.FindProperty("Material");
            m_Texture = serializedObject.FindProperty("Texture");
            m_MergeStrokeOverlaps = serializedObject.FindProperty("MergeStrokeOverlaps");
            m_GpuTrimCustomMaterial = serializedObject.FindProperty("GpuTrimCustomMaterial");
            m_SortingLayerID = serializedObject.FindProperty("SortingLayerID");
            m_SortingOrder = serializedObject.FindProperty("SortingOrder");
            m_SvgFillOverride = serializedObject.FindProperty("SvgFillOverride");
            m_SvgStrokeOverride = serializedObject.FindProperty("SvgStrokeOverride");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SvgAssetSlotGUI.Draw(m_SvgAsset);
            EditorGUILayout.PropertyField(m_SvgUnitsPerWorldUnit);

            bool hasSvg = m_SvgAsset.objectReferenceValue != null;
            using (new EditorGUI.DisabledScope(hasSvg))
            {
                EditorGUILayout.PropertyField(m_ShapeStack, true);
                EditorGUILayout.PropertyField(m_Fill, true);
                EditorGUILayout.PropertyField(m_Stroke, true);
                EditorGUILayout.PropertyField(m_Depth, true);
                // When depth is on, the renderer forces stroke alignment
                // to Inner so the ribbon stays inside the extruded fill
                // silhouette. Surface this to the user so the apparent
                // override doesn't look like a bug.
                if (DepthEnabled() && !StrokeAlignmentIsInner())
                {
                    EditorGUILayout.HelpBox(
                        "Depth is enabled — stroke alignment is rendered as Inner regardless of the field above so the outline stays inside the 3D silhouette.",
                        MessageType.Info);
                }
            }

            if (hasSvg)
            {
                EditorGUILayout.HelpBox(
                    "SVG asset assigned. ShapeStack and the per-renderer Fill/Stroke above are ignored — "
                    + "each sub-shape keeps its own fill/stroke from the SVG. Use the Svg Color Override "
                    + "section to replace those colors, and the modifiers below to trim/wiggle/round the "
                    + "imported contours.", MessageType.Info);

                if (DepthEnabled())
                {
                    EditorGUILayout.HelpBox(
                        "Depth extrusion is not applied to SVG assets — the mesh stays flat. "
                        + "Extruding imported contours would need per-contour side walls that respect "
                        + "the even-odd holes, which the depth builder does not do yet.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Svg Color Override", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(m_SvgFillOverride, true);
                EditorGUILayout.PropertyField(m_SvgStrokeOverride, true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Modifiers", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_RoundCorners, true);
            EditorGUILayout.PropertyField(m_Trim, true);
            EditorGUILayout.PropertyField(m_Wiggle, true);
            if (hasSvg)
            {
                EditorGUILayout.HelpBox(
                    "Modifiers apply to each SVG sub-shape independently, so Trim runs over every "
                    + "contour's own length (AE's \"Trim Multiple Shapes: Individually\"), not one sweep "
                    + "across the whole icon.", MessageType.None);
            }

            EditorGUILayout.PropertyField(m_Tint);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Material);
            EditorGUILayout.PropertyField(m_Texture);
            EditorGUILayout.PropertyField(m_MergeStrokeOverlaps);
            EditorGUILayout.PropertyField(m_GpuTrimCustomMaterial);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sorting", EditorStyles.boldLabel);
            SortingLayerField(m_SortingLayerID);
            EditorGUILayout.PropertyField(m_SortingOrder, new GUIContent("Order in Layer"));

            serializedObject.ApplyModifiedProperties();
        }

        private bool DepthEnabled()
        {
            var enabledProp = m_Depth.FindPropertyRelative("enabled");
            var thicknessProp = m_Depth.FindPropertyRelative("thickness");
            return enabledProp != null && enabledProp.boolValue
                   && thicknessProp != null && thicknessProp.floatValue > 0f;
        }

        private bool StrokeAlignmentIsInner()
        {
            var alignProp = m_Stroke.FindPropertyRelative("alignment");
            return alignProp != null && alignProp.enumValueIndex == (int)StrokeAlignment.Inner;
        }

        private static void SortingLayerField(SerializedProperty layerID)
        {
            var layers = SortingLayer.layers;
            int count = layers.Length;
            var names = new GUIContent[count];
            var ids = new int[count];
            int selectedIndex = 0;
            for (int i = 0; i < count; i++)
            {
                names[i] = new GUIContent(layers[i].name);
                ids[i] = layers[i].id;
                if (layers[i].id == layerID.intValue) selectedIndex = i;
            }
            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(new GUIContent("Sorting Layer"), selectedIndex, names);
            if (EditorGUI.EndChangeCheck())
            {
                layerID.intValue = ids[newIndex];
            }
        }

        private void OnSceneGUI()
        {
            var renderer = (VectorSpriteRenderer)target;
            var so = new SerializedObject(renderer);

            if (so.FindProperty("SvgAsset").objectReferenceValue != null) return;

            var stack = renderer.ShapeStack;
            m_ActiveSlot = FreePathSceneHandles.DrawSlotOverlay(m_ActiveSlot, ref stack);

            // Faint guides for the other 3 slots — helps the user line up
            // a multi-slot blend without switching the overlay back and forth.
            FreePathSceneHandles.DrawInactiveSlotGuides(renderer.transform, ref stack, m_ActiveSlot);

            // Each slot is at ShapeStack.Slot{N}.shape — that's the
            // SerializedProperty path the handle code needs in order to
            // route writes through SerializedProperty (and therefore
            // through Record mode's auto-keyframe capture).
            string shapePath = "ShapeStack.Slot" + m_ActiveSlot + ".shape";
            var slot = stack.GetSlot(m_ActiveSlot);

            if (slot.shape.kind != ShapeKind.FreePath) return;

            if (FreePathSceneHandles.Draw(renderer.transform, slot.shape, renderer, "Edit VMG FreePath Node",
                                          so, shapePath))
            {
                EditorUtility.SetDirty(renderer);
                renderer.Rebuild();
            }
        }
    }
}
