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
        // 0 = base shape (m_Shape), 1 = morph target (m_Morph.target).
        // Editor instance state, so switching selection resets it.
        private int m_ActiveShape;

        private SerializedProperty m_SvgAsset;
        private SerializedProperty m_SvgUnitsPerWorldUnit;
        private SerializedProperty m_Shape;
        private SerializedProperty m_Stroke;
        private SerializedProperty m_Fill;
        private SerializedProperty m_Morph;
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
            m_Shape = serializedObject.FindProperty("m_Shape");
            m_Stroke = serializedObject.FindProperty("m_Stroke");
            m_Fill = serializedObject.FindProperty("m_Fill");
            m_Morph = serializedObject.FindProperty("m_Morph");
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
                EditorGUILayout.PropertyField(m_Shape, true);
                EditorGUILayout.PropertyField(m_Fill, true);
                EditorGUILayout.PropertyField(m_Stroke, true);
                EditorGUILayout.PropertyField(m_Morph, true);
                EditorGUILayout.PropertyField(m_RoundCorners, true);
                EditorGUILayout.PropertyField(m_Trim, true);
            }
            if (hasSvg)
            {
                EditorGUILayout.HelpBox("SVG asset assigned. Procedural shape, modifiers, and per-renderer fill/stroke are ignored — each sub-shape's own fill/stroke from the SVG is used. Tint multiplies the result.", MessageType.Info);
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
            // Per Unity guidance, do NOT use the editor's `serializedObject`
            // inside OnSceneGUI. Build a local SerializedObject from the
            // target so handle edits can route through SerializedProperty
            // writes (which Unity Record mode captures as keyframes).
            var so = new SerializedObject(renderer);

            // SVG asset overrides the procedural shape entirely — no
            // FreePath handles to draw, no overlay to show. Read from
            // the local SerializedObject to avoid Unity's "do not use
            // the editor's serializedObject inside OnSceneGUI" warning.
            if (so.FindProperty("m_SvgAsset").objectReferenceValue != null) return;

            // Decide whether the morph target is reachable for handle
            // editing — only when the modifier is on AND its target is
            // also a FreePath.
            var morph = renderer.MorphModifier;
            bool morphAvailable = morph.enabled
                                  && morph.target.kind == ShapeKind.FreePath;

            // Overlay only matters when there's a choice to make. When
            // morph isn't a FreePath we silently stay on base.
            if (renderer.Shape.kind == ShapeKind.FreePath || morphAvailable)
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
                activeShape = renderer.Shape;
            }

            if (FreePathSceneHandles.Draw(renderer.transform, activeShape, renderer, "Edit VMG FreePath Node",
                                          so, shapePath))
            {
                EditorUtility.SetDirty(renderer);
                renderer.Rebuild();
            }
        }
    }
}
