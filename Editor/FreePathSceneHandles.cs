using UnityEditor;
using UnityEngine;
using VMG.Core;

namespace VMG.EditorTools
{
    /// Shared SceneView handle drawing for FreePath nodes (both UGUI and
    /// world renderers). Handles node position + in/out cubic tangents
    /// on the active flat node slots (m_Node00..m_NodeNN where NN ==
    /// activeNodeCount - 1).
    ///
    /// All edits are routed through SerializedProperty writes on the
    /// slot fields so Unity's Record mode captures each drag as an
    /// automatic keyframe when active.
    ///
    /// Per Unity guidance, callers MUST pass a SerializedObject built
    /// locally inside OnSceneGUI (do NOT pass the editor's
    /// `serializedObject`).
    internal static class FreePathSceneHandles
    {
        private const float NodeHandleSize = 0.12f;
        private const float TangentHandleSize = 0.09f;
        private static readonly Color NodeColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color OutTangentColor = new Color(0.35f, 0.85f, 1f, 1f);
        private static readonly Color InTangentColor = new Color(1f, 0.55f, 0.35f, 1f);

        public static bool Draw(Transform space, PrimitiveShapeSource shape,
                                UnityEngine.Object owner, string undoLabel,
                                SerializedObject serializedObject, string shapePropertyPath)
        {
            if (shape.kind != ShapeKind.FreePath) return false;
            if (serializedObject == null || shapePropertyPath == null) return false;

            serializedObject.Update();

            var shapeProp = serializedObject.FindProperty(shapePropertyPath);
            if (shapeProp == null) return false;

            int activeCount = Mathf.Clamp(shape.activeNodeCount, 0, PrimitiveShapeSource.MaxFreeNodes);
            bool any = false;
            for (int i = 0; i < activeCount; i++)
            {
                var slotProp = shapeProp.FindPropertyRelative(SlotName(i));
                if (slotProp == null) continue;
                var posProp = slotProp.FindPropertyRelative("position");
                var inProp = slotProp.FindPropertyRelative("inTangent");
                var outProp = slotProp.FindPropertyRelative("outTangent");
                var typeProp = slotProp.FindPropertyRelative("type");

                Vector2 nodePos = posProp.vector2Value;
                Vector2 nodeIn = inProp.vector2Value;
                Vector2 nodeOut = outProp.vector2Value;
                NodeType nodeType = (NodeType)typeProp.enumValueIndex;

                Vector3 worldPos = space.TransformPoint(nodePos);
                float size = HandleUtility.GetHandleSize(worldPos);

                // Node position handle.
                Handles.color = NodeColor;
                EditorGUI.BeginChangeCheck();
                Vector3 movedPos = Handles.FreeMoveHandle(worldPos, size * NodeHandleSize, Vector3.zero, Handles.RectangleHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    posProp.vector2Value = (Vector2)space.InverseTransformPoint(movedPos);
                    any = true;
                    worldPos = movedPos;
                    nodePos = posProp.vector2Value;
                }

                bool showTangents = nodeType == NodeType.Bezier || nodeType == NodeType.Smooth;

                if (nodeOut.sqrMagnitude > 1e-8f || showTangents)
                {
                    Vector3 outWorld = space.TransformPoint(nodePos + nodeOut);
                    Handles.color = OutTangentColor;
                    Handles.DrawAAPolyLine(2.5f, worldPos, outWorld);
                    EditorGUI.BeginChangeCheck();
                    Vector3 movedOut = Handles.FreeMoveHandle(outWorld, size * TangentHandleSize, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        outProp.vector2Value = (Vector2)space.InverseTransformPoint(movedOut) - nodePos;
                        any = true;
                    }
                }

                if (nodeIn.sqrMagnitude > 1e-8f || showTangents)
                {
                    Vector3 inWorld = space.TransformPoint(nodePos + nodeIn);
                    Handles.color = InTangentColor;
                    Handles.DrawAAPolyLine(2.5f, worldPos, inWorld);
                    EditorGUI.BeginChangeCheck();
                    Vector3 movedIn = Handles.FreeMoveHandle(inWorld, size * TangentHandleSize, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        inProp.vector2Value = (Vector2)space.InverseTransformPoint(movedIn) - nodePos;
                        any = true;
                    }
                }
            }
            Handles.color = Color.white;

            if (any)
            {
                serializedObject.ApplyModifiedProperties();
            }
            return any;
        }

        public static bool Draw(Transform space, PrimitiveShapeSource shape, UnityEngine.Object owner, string undoLabel)
        {
            return Draw(space, shape, owner, undoLabel, null, null);
        }

        // Slot field naming convention: m_Node00 .. m_Node63. Matches the
        // hand-flattened field layout in PrimitiveShapeSource.
        private static string SlotName(int i)
        {
            return i < 10 ? "m_Node0" + i : "m_Node" + i;
        }

        /// SceneView overlay that lets the user pick which Shape's
        /// nodes the handles operate on. Without this, base shape and
        /// morph-target handles would overlap and become ambiguous.
        ///
        /// Returns the chosen index (0 = base shape, 1 = morph target).
        /// `morphTargetAvailable` controls whether the Morph option is
        /// enabled; pass false when the morph modifier is disabled or
        /// its target is not a FreePath.
        public static int DrawActiveShapeOverlay(int current, bool morphTargetAvailable)
        {
            Handles.BeginGUI();
            try
            {
                const float W = 220f, H = 44f, MARGIN = 8f;
                Rect box = new Rect(MARGIN, MARGIN, W, H);
                GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

                GUI.Label(new Rect(box.x + 6f, box.y + 2f, W - 12f, 16f),
                    "VMG · Edit FreePath", EditorStyles.miniBoldLabel);

                Rect baseBtn = new Rect(box.x + 6f, box.y + 20f, (W - 18f) * 0.5f, 18f);
                Rect morphBtn = new Rect(baseBtn.xMax + 6f, baseBtn.y, baseBtn.width, baseBtn.height);

                if (GUI.Toggle(baseBtn, current == 0, "Base Shape", EditorStyles.miniButtonLeft) && current != 0)
                    current = 0;

                using (new EditorGUI.DisabledScope(!morphTargetAvailable))
                {
                    if (GUI.Toggle(morphBtn, current == 1, "Morph Target", EditorStyles.miniButtonRight) && current != 1)
                        current = 1;
                }

                // Fall back to Base when Morph becomes unavailable so
                // the user isn't stuck editing a hidden target.
                if (current == 1 && !morphTargetAvailable) current = 0;
            }
            finally
            {
                Handles.EndGUI();
            }
            return current;
        }
    }
}
