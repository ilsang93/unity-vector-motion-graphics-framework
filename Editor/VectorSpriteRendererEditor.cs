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
        private SerializedProperty m_Tint;
        private SerializedProperty m_Material;
        private SerializedProperty m_Texture;
        private SerializedProperty m_SortingLayerID;
        private SerializedProperty m_SortingOrder;

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
            m_Tint = serializedObject.FindProperty("Tint");
            m_Material = serializedObject.FindProperty("Material");
            m_Texture = serializedObject.FindProperty("Texture");
            m_SortingLayerID = serializedObject.FindProperty("SortingLayerID");
            m_SortingOrder = serializedObject.FindProperty("SortingOrder");
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
                EditorGUILayout.PropertyField(m_RoundCorners, true);
                EditorGUILayout.PropertyField(m_Trim, true);
            }
            if (hasSvg)
            {
                EditorGUILayout.HelpBox("SVG asset assigned. ShapeStack, modifiers, and per-renderer fill/stroke are ignored — each sub-shape's own fill/stroke from the SVG is used. Tint multiplies the result.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(m_Tint);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_Material);
            EditorGUILayout.PropertyField(m_Texture);

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
