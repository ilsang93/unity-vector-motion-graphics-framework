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
        private SerializedProperty m_Wiggle;
        private SerializedProperty m_FitToRect;
        private SerializedProperty m_Texture;
        private SerializedProperty m_SvgFillOverride;
        private SerializedProperty m_SvgStrokeOverride;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_SvgAsset = serializedObject.FindProperty("SvgAsset");
            m_ShapeStack = serializedObject.FindProperty("ShapeStack");
            m_Stroke = serializedObject.FindProperty("Stroke");
            m_Fill = serializedObject.FindProperty("Fill");
            m_RoundCorners = serializedObject.FindProperty("RoundCorners");
            m_Trim = serializedObject.FindProperty("Trim");
            m_Wiggle = serializedObject.FindProperty("Wiggle");
            m_FitToRect = serializedObject.FindProperty("FitToRect");
            m_Texture = serializedObject.FindProperty("Texture");
            m_SvgFillOverride = serializedObject.FindProperty("SvgFillOverride");
            m_SvgStrokeOverride = serializedObject.FindProperty("SvgStrokeOverride");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_FitToRect);
            SvgAssetSlotGUI.Draw(m_SvgAsset);

            bool hasSvg = m_SvgAsset.objectReferenceValue != null;

            // Only the shape SOURCE and its styles are replaced by an
            // SVG asset. Modifiers still run over the imported contours,
            // so they stay enabled.
            using (new EditorGUI.DisabledScope(hasSvg))
            {
                EditorGUILayout.PropertyField(m_ShapeStack, true);
                EditorGUILayout.PropertyField(m_Fill, true);
                EditorGUILayout.PropertyField(m_Stroke, true);
            }

            if (hasSvg)
            {
                EditorGUILayout.HelpBox(
                    "SVG asset assigned. ShapeStack and the per-renderer Fill/Stroke above are ignored — "
                    + "each sub-shape keeps its own fill/stroke from the SVG. Use the Svg Color Override "
                    + "section to replace those colors, and the modifiers below to trim/wiggle/round the "
                    + "imported contours.", MessageType.Info);

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

            if (so.FindProperty("SvgAsset").objectReferenceValue != null) return;

            var stack = graphic.ShapeStack;
            m_ActiveSlot = FreePathSceneHandles.DrawSlotOverlay(m_ActiveSlot, ref stack);

            // Faint guides for the other 3 slots — helps the user line up
            // a multi-slot blend without switching the overlay back and forth.
            FreePathSceneHandles.DrawInactiveSlotGuides(graphic.rectTransform, ref stack, m_ActiveSlot);

            string shapePath = "ShapeStack.Slot" + m_ActiveSlot + ".shape";
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
