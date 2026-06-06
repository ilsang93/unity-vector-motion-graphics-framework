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
        // 0 = base shape (m_Shape), 1 = morph target (m_Morph.target).
        // Editor instance state, so switching selection resets it.
        private int m_ActiveShape;

        private SerializedProperty m_SvgAsset;
        private SerializedProperty m_Shape;
        private SerializedProperty m_Stroke;
        private SerializedProperty m_Fill;
        private SerializedProperty m_Morph;
        private SerializedProperty m_RoundCorners;
        private SerializedProperty m_Trim;
        private SerializedProperty m_FitToRect;
        private SerializedProperty m_Texture;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_SvgAsset = serializedObject.FindProperty("m_SvgAsset");
            m_Shape = serializedObject.FindProperty("m_Shape");
            m_Stroke = serializedObject.FindProperty("m_Stroke");
            m_Fill = serializedObject.FindProperty("m_Fill");
            m_Morph = serializedObject.FindProperty("m_Morph");
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
                EditorGUILayout.PropertyField(m_Shape, true);
                EditorGUILayout.PropertyField(m_Fill, true);
                EditorGUILayout.PropertyField(m_Stroke, true);
                EditorGUILayout.PropertyField(m_Morph, true);
                EditorGUILayout.PropertyField(m_RoundCorners, true);
                EditorGUILayout.PropertyField(m_Trim, true);
            }
            if (hasSvg)
            {
                EditorGUILayout.HelpBox("SVG asset assigned. Procedural shape, modifiers, and per-renderer fill/stroke are ignored — each sub-shape's own fill/stroke from the SVG is used. Graphic color tints the result.", MessageType.Info);
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
            // Per Unity guidance, do NOT use the editor's `serializedObject`
            // inside OnSceneGUI. Build a local SerializedObject from the
            // target so handle edits can route through SerializedProperty
            // writes (which Unity Record mode captures as keyframes).
            var so = new SerializedObject(graphic);

            // SVG asset overrides the procedural shape entirely — no
            // FreePath handles to draw, no overlay to show. Read from
            // the local SerializedObject to avoid Unity's "do not use
            // the editor's serializedObject inside OnSceneGUI" warning.
            if (so.FindProperty("m_SvgAsset").objectReferenceValue != null) return;

            var morph = graphic.MorphModifier;
            bool morphAvailable = morph.enabled
                                  && morph.target.kind == ShapeKind.FreePath;

            if (graphic.Shape.kind == ShapeKind.FreePath || morphAvailable)
            {
                m_ActiveShape = FreePathSceneHandles.DrawActiveShapeOverlay(m_ActiveShape, morphAvailable);
            }

            string shapePath;
            PrimitiveShapeSource activeShape;
            if (m_ActiveShape == 1 && morphAvailable)
            {
                shapePath = "m_Morph.target";
                activeShape = morph.target;
            }
            else
            {
                shapePath = "m_Shape";
                activeShape = graphic.Shape;
            }

            if (FreePathSceneHandles.Draw(graphic.rectTransform, activeShape, graphic, "Edit VMG FreePath Node",
                                          so, shapePath))
            {
                EditorUtility.SetDirty(graphic);
                graphic.SetVerticesDirty();
            }
        }
    }
}
