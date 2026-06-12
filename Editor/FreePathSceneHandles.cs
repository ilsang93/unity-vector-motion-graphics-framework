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
        // Yellow node with a dark outline so the handle stays visible against
        // any fill/stroke color (white fill was the original problem case).
        private static readonly Color NodeColor = new Color(1f, 0.85f, 0.1f, 1f);
        private static readonly Color NodeOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        private const float NodeOutlineScale = 1.5f;
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

                // Dark outline behind the node so the handle stays readable
                // against white fill/stroke. Drawn as a non-interactive cap
                // slightly larger than the actual move handle.
                Handles.color = NodeOutlineColor;
                Handles.RectangleHandleCap(0, worldPos, Quaternion.identity,
                    size * NodeHandleSize * NodeOutlineScale, EventType.Repaint);

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

        // Faint outline color for non-active stack slots. Low alpha so it
        // reads as a guide line, not a primary edit handle.
        private static readonly Color InactiveSlotColor = new Color(1f, 1f, 1f, 0.18f);
        private const float InactiveSlotLineWidth = 1.5f;

        // Scratch path reused across DrawInactiveSlotGuides calls. Build()
        // clears it so contents from a prior call don't leak.
        private static readonly VectorPath s_guidePath = new VectorPath();

        /// Draws every stack slot except `activeSlot` as a faint polyline
        /// in the SceneView so multi-slot blends are easier to read while
        /// editing one slot. Operates on every ShapeKind (not just
        /// FreePath) so guides show up regardless of slot contents.
        public static void DrawInactiveSlotGuides(Transform space, ref ShapeStack stack, int activeSlot)
        {
            if (space == null) return;
            Color prev = Handles.color;
            Handles.color = InactiveSlotColor;
            for (int i = 0; i < ShapeStack.MaxSlots; i++)
            {
                if (i == activeSlot) continue;
                var slot = stack.GetSlot(i);
                s_guidePath.Clear();
                slot.shape.Build(s_guidePath);
                int n = s_guidePath.Count;
                if (n < 2) continue;
                int lineCount = s_guidePath.closed ? n + 1 : n;
                Vector3[] points = new Vector3[lineCount];
                for (int v = 0; v < n; v++)
                {
                    points[v] = space.TransformPoint(s_guidePath.GetPoint(v));
                }
                if (s_guidePath.closed) points[n] = points[0];
                Handles.DrawAAPolyLine(InactiveSlotLineWidth, points);
            }
            Handles.color = prev;
        }

        // Slot field naming convention: Node00 .. Node63. Matches the
        // hand-flattened field layout in PrimitiveShapeSource.
        private static string SlotName(int i)
        {
            return i < 10 ? "Node0" + i : "Node" + i;
        }

        /// SceneView overlay that lets the user pick which ShapeStack
        /// slot the handles operate on. All 4 slots are always
        /// selectable — even at intensity 0 — so the user can edit a
        /// slot before turning it on. The current intensity is shown
        /// next to each button so it's obvious which slots actually
        /// contribute to the rendered shape.
        public static int DrawSlotOverlay(int current, ref ShapeStack stack)
        {
            Handles.BeginGUI();
            try
            {
                const float W = 280f, H = 44f, MARGIN = 8f;
                Rect box = new Rect(MARGIN, MARGIN, W, H);
                GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

                GUI.Label(new Rect(box.x + 6f, box.y + 2f, W - 12f, 16f),
                    "VMG · Edit FreePath (ShapeStack slot)", EditorStyles.miniBoldLabel);

                float btnW = (W - 12f - 3f * 4f) / ShapeStack.MaxSlots;
                for (int i = 0; i < ShapeStack.MaxSlots; i++)
                {
                    Rect btn = new Rect(box.x + 6f + i * (btnW + 4f), box.y + 20f, btnW, 18f);
                    GUIStyle style;
                    if (ShapeStack.MaxSlots == 1) style = EditorStyles.miniButton;
                    else if (i == 0) style = EditorStyles.miniButtonLeft;
                    else if (i == ShapeStack.MaxSlots - 1) style = EditorStyles.miniButtonRight;
                    else style = EditorStyles.miniButtonMid;

                    var slot = stack.GetSlot(i);
                    string label = "S" + i + " (" + slot.intensity.ToString("0.##") + ")";
                    if (GUI.Toggle(btn, current == i, label, style) && current != i) current = i;
                }
            }
            finally
            {
                Handles.EndGUI();
            }
            return Mathf.Clamp(current, 0, ShapeStack.MaxSlots - 1);
        }
    }
}
