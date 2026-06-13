using UnityEditor;
using UnityEngine;
using VMG.Utility;

namespace VMG.EditorTools
{
    [CustomEditor(typeof(VMGBillboard))]
    [CanEditMultipleObjects]
    internal sealed class VMGBillboardEditor : Editor
    {
        SerializedProperty m_TargetTransform;
        SerializedProperty m_TargetCamera;
        SerializedProperty m_Mode;
        SerializedProperty m_FaceAxis;
        SerializedProperty m_TiltOffset;

        void OnEnable()
        {
            m_TargetTransform = serializedObject.FindProperty("TargetTransform");
            m_TargetCamera = serializedObject.FindProperty("TargetCamera");
            m_Mode = serializedObject.FindProperty("Mode");
            m_FaceAxis = serializedObject.FindProperty("FaceAxis");
            m_TiltOffset = serializedObject.FindProperty("TiltOffset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_TargetTransform);

            // Camera slot is only meaningful when no Transform target is set.
            using (new EditorGUI.DisabledScope(m_TargetTransform.objectReferenceValue != null))
            {
                EditorGUILayout.PropertyField(m_TargetCamera);
            }

            EditorGUILayout.PropertyField(m_Mode);
            EditorGUILayout.PropertyField(m_FaceAxis);
            EditorGUILayout.PropertyField(m_TiltOffset);

            // Tell the user which path is active, in plain words.
            EditorGUILayout.Space();
            string activeMode = ResolveActiveModeLabel();
            EditorGUILayout.HelpBox(activeMode, MessageType.None);

            serializedObject.ApplyModifiedProperties();
        }

        string ResolveActiveModeLabel()
        {
            if (m_TargetTransform.objectReferenceValue != null)
                return $"Following Transform: {((Transform)m_TargetTransform.objectReferenceValue).name}";

            if (m_TargetCamera.objectReferenceValue != null)
                return $"Facing camera: {((Camera)m_TargetCamera.objectReferenceValue).name}";

            if (Camera.main != null)
                return "Facing Camera.main (auto). Assign a camera or Transform to override.";

            return "Auto camera (none resolved yet). In edit mode, the SceneView camera is used.";
        }
    }
}
