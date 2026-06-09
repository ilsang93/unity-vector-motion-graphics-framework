using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using VMG.Core;
using VMG.UI;

namespace VMG.EditorTools
{
    [CustomEditor(typeof(VectorImageGraphic), true)]
    [CanEditMultipleObjects]
    internal sealed class VectorImageGraphicEditor : GraphicEditor
    {
        private int m_ActiveSlot;

        private SerializedProperty m_SvgAsset;
        private SerializedProperty m_ShapeStack;
        private SerializedProperty m_Stroke;
        private SerializedProperty m_Fill;
        private SerializedProperty m_RoundCorners;
        private SerializedProperty m_Trim;
        private SerializedProperty m_FitToRect;
        private SerializedProperty m_Texture;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_SvgAsset = serializedObject.FindProperty("m_SvgAsset");
            m_ShapeStack = serializedObject.FindProperty("m_ShapeStack");
            m_Stroke = serializedObject.FindProperty("m_Stroke");
            m_Fill = serializedObject.FindProperty("m_Fill");
            m_RoundCorners = serializedObject.FindProperty("m_RoundCorners");
            m_Trim = serializedObject.FindProperty("m_Trim");
            m_FitToRect = serializedObject.FindProperty("m_FitToRect");
            m_Texture = serializedObject.FindProperty("m_Texture");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_FitToRect);
            EditorGUILayout.PropertyField(m_SvgAsset);

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
                EditorGUILayout.HelpBox("SVG asset assigned. ShapeStack, modifiers, and per-renderer fill/stroke are ignored — each sub-shape's own fill/stroke from the SVG is used. Graphic color tints the result.", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rendering", EditorStyles.boldLabel);
            // Material slot inherited from Graphic; Texture is our addition.
            AppearanceControlsGUI();
            EditorGUILayout.PropertyField(m_Texture);

            RaycastControlsGUI();
            MaskableControlsGUI();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var graphic = (VectorImageGraphic)target;
            var so = new SerializedObject(graphic);

            if (so.FindProperty("m_SvgAsset").objectReferenceValue != null) return;

            var stack = graphic.ShapeStack;
            m_ActiveSlot = FreePathSceneHandles.DrawSlotOverlay(m_ActiveSlot, ref stack);

            // Faint guides for the other 3 slots — helps the user line up
            // a multi-slot blend without switching the overlay back and forth.
            FreePathSceneHandles.DrawInactiveSlotGuides(graphic.rectTransform, ref stack, m_ActiveSlot);

            string shapePath = "m_ShapeStack.m_Slot" + m_ActiveSlot + ".shape";
            var slot = stack.GetSlot(m_ActiveSlot);

            if (slot.shape.kind != ShapeKind.FreePath) return;

            if (FreePathSceneHandles.Draw(graphic.rectTransform, slot.shape, graphic, "Edit VMG FreePath Node",
                                          so, shapePath))
            {
                EditorUtility.SetDirty(graphic);
                graphic.SetVerticesDirty();
            }
        }
    }
}
