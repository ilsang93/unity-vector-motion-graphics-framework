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
        private SerializedProperty m_RoundCorners;
        private SerializedProperty m_Trim;
        private SerializedProperty m_Tint;
        private SerializedProperty m_Material;
        private SerializedProperty m_Texture;
        private SerializedProperty m_SortingLayerID;
        private SerializedProperty m_SortingOrder;

        private void OnEnable()
        {
            m_SvgAsset = serializedObject.FindProperty("m_SvgAsset");
            m_SvgUnitsPerWorldUnit = serializedObject.FindProperty("m_SvgUnitsPerWorldUnit");
            m_ShapeStack = serializedObject.FindProperty("m_ShapeStack");
            m_Stroke = serializedObject.FindProperty("m_Stroke");
            m_Fill = serializedObject.FindProperty("m_Fill");
            m_RoundCorners = serializedObject.FindProperty("m_RoundCorners");
            m_Trim = serializedObject.FindProperty("m_Trim");
            m_Tint = serializedObject.FindProperty("m_Tint");
            m_Material = serializedObject.FindProperty("m_Material");
            m_Texture = serializedObject.FindProperty("m_Texture");
            m_SortingLayerID = serializedObject.FindProperty("m_SortingLayerID");
            m_SortingOrder = serializedObject.FindProperty("m_SortingOrder");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_SvgAsset);
            EditorGUILayout.PropertyField(m_SvgUnitsPerWorldUnit);

            bool hasSvg = m_SvgAsset.objectReferenceValue != null;
            using (new EditorGUI.DisabledScope(hasSvg))
            {
                EditorGUILayout.PropertyField(m_ShapeStack, true);
                EditorGUILayout.PropertyField(m_Fill, true);
                EditorGUILayout.PropertyField(m_Stroke, true);
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

            if (so.FindProperty("m_SvgAsset").objectReferenceValue != null) return;

            var stack = renderer.ShapeStack;
            m_ActiveSlot = FreePathSceneHandles.DrawSlotOverlay(m_ActiveSlot, ref stack);

            // Each slot is at m_ShapeStack.m_Slot{N}.shape — that's the
            // SerializedProperty path the handle code needs in order to
            // route writes through SerializedProperty (and therefore
            // through Record mode's auto-keyframe capture).
            string shapePath = "m_ShapeStack.m_Slot" + m_ActiveSlot + ".shape";
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
