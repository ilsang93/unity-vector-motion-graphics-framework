using UnityEditor;
using UnityEngine;
using VMG.Core;

namespace VMG.EditorTools
{
    /// Custom drawer for PrimitiveShapeSource. Shows only the fields
    /// relevant to the selected ShapeKind. For FreePath, exposes the
    /// active flat node slots (m_Node00..) with compact per-slot rows
    /// and Add / Remove Last buttons that adjust activeNodeCount.
    ///
    /// The 64 slot fields exist on the struct specifically so Unity's
    /// Animation window can keyframe their inner values — this drawer
    /// just hides the inactive ones to keep the inspector tidy.
    /// Reorder is unsupported by design (the slot index is the keyframe
    /// channel); add/remove only happens at the end.
    [CustomPropertyDrawer(typeof(PrimitiveShapeSource))]
    internal sealed class PrimitiveShapeSourceDrawer : PropertyDrawer
    {
        // Visual constants. One slot renders as: header (with index +
        // remove icon for the last slot) and three Vector2 fields
        // (position / in tangent / out tangent). Type enum is a fourth
        // line shown only for Bezier/Smooth so the common-case Corner
        // node stays compact.
        private const float SlotHeaderHeight = 18f;
        private const float SlotInnerPad = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var kindProp = property.FindPropertyRelative("kind");
            var centerProp = property.FindPropertyRelative("center");
            var sizeProp = property.FindPropertyRelative("size");
            var sidesProp = property.FindPropertyRelative("sides");
            var cornerRadiusProp = property.FindPropertyRelative("cornerRadius");
            var circleSegmentsProp = property.FindPropertyRelative("circleSegments");
            var freeClosedProp = property.FindPropertyRelative("freeClosed");
            var bezierSamplesProp = property.FindPropertyRelative("bezierSamplesPerSegment");
            var activeNodeCountProp = property.FindPropertyRelative("activeNodeCount");

            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            Rect r = new Rect(position.x, position.y, position.width, line);

            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, label, true);
            if (!property.isExpanded) { EditorGUI.EndProperty(); return; }
            r.y += line + pad;

            EditorGUI.indentLevel++;
            EditorGUI.PropertyField(r, kindProp); r.y += line + pad;

            var kind = (ShapeKind)kindProp.enumValueIndex;
            bool showCenter = kind != ShapeKind.FreePath;
            bool showSize = kind == ShapeKind.Circle || kind == ShapeKind.Ellipse
                            || kind == ShapeKind.Rectangle || kind == ShapeKind.RoundedRectangle
                            || kind == ShapeKind.Polygon;
            bool showSides = kind == ShapeKind.Polygon;
            bool showCornerRadius = kind == ShapeKind.RoundedRectangle;
            bool showCircleSegments = kind == ShapeKind.Circle || kind == ShapeKind.Ellipse || kind == ShapeKind.RoundedRectangle;
            bool showFree = kind == ShapeKind.FreePath;

            if (showCenter) { EditorGUI.PropertyField(r, centerProp); r.y += line + pad; }
            if (showSize)
            {
                if (kind == ShapeKind.Circle || kind == ShapeKind.Polygon)
                {
                    float diameter = EditorGUI.FloatField(r, "Diameter", sizeProp.vector2Value.x);
                    sizeProp.vector2Value = new Vector2(diameter, diameter);
                }
                else
                {
                    EditorGUI.PropertyField(r, sizeProp);
                }
                r.y += line + pad;
            }
            if (showSides) { EditorGUI.PropertyField(r, sidesProp); r.y += line + pad; }
            if (showCornerRadius) { EditorGUI.PropertyField(r, cornerRadiusProp); r.y += line + pad; }
            if (showCircleSegments) { EditorGUI.PropertyField(r, circleSegmentsProp); r.y += line + pad; }
            if (showFree)
            {
                EditorGUI.PropertyField(r, freeClosedProp); r.y += line + pad;
                EditorGUI.PropertyField(r, bezierSamplesProp); r.y += line + pad;

                int active = Mathf.Clamp(activeNodeCountProp.intValue, 0, PrimitiveShapeSource.MaxFreeNodes);

                // Slot count row with +/- buttons. activeNodeCount is
                // still shown as a property field so it remains a
                // visible, keyframable channel.
                Rect countRect = new Rect(r.x, r.y, r.width - 56f, line);
                Rect addRect = new Rect(r.x + r.width - 54f, r.y, 25f, line);
                Rect remRect = new Rect(r.x + r.width - 27f, r.y, 25f, line);
                EditorGUI.PropertyField(countRect, activeNodeCountProp);
                using (new EditorGUI.DisabledScope(active >= PrimitiveShapeSource.MaxFreeNodes))
                {
                    if (GUI.Button(addRect, new GUIContent("+", "Append a node slot at the end (activeNodeCount++).")))
                    {
                        AddNodeAtEnd(property, active);
                        activeNodeCountProp.intValue = active + 1;
                    }
                }
                using (new EditorGUI.DisabledScope(active <= 0))
                {
                    if (GUI.Button(remRect, new GUIContent("-", "Remove the last node slot (activeNodeCount--). Clears slot data so the next append starts clean.")))
                    {
                        ClearSlot(property, active - 1);
                        activeNodeCountProp.intValue = active - 1;
                    }
                }
                r.y += line + pad;

                // Re-read in case the buttons above mutated it.
                active = Mathf.Clamp(activeNodeCountProp.intValue, 0, PrimitiveShapeSource.MaxFreeNodes);

                // One row per active slot.
                for (int i = 0; i < active; i++)
                {
                    var slotProp = property.FindPropertyRelative(SlotName(i));
                    if (slotProp == null) continue;
                    float h = SlotHeight(slotProp);
                    Rect slotRect = new Rect(r.x, r.y, r.width, h);
                    DrawSlot(slotRect, slotProp, i);
                    r.y += h + pad;
                }
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static void DrawSlot(Rect rect, SerializedProperty slotProp, int index)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;

            var posProp = slotProp.FindPropertyRelative("position");
            var inProp = slotProp.FindPropertyRelative("inTangent");
            var outProp = slotProp.FindPropertyRelative("outTangent");
            var typeProp = slotProp.FindPropertyRelative("type");

            // Header bar — slot index and node type dropdown.
            Rect header = new Rect(rect.x, rect.y, rect.width, SlotHeaderHeight);
            EditorGUI.LabelField(new Rect(header.x, header.y, 70f, header.height),
                new GUIContent("Node " + index));
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(
                new Rect(header.x + 72f, header.y, header.width - 72f, header.height),
                typeProp, GUIContent.none);
            // Switching type AWAY from Bezier/Smooth must clear the
            // tangents — otherwise lingering non-zero values keep
            // BezierTessellator curving the segment and SceneView
            // tangent handles stay visible. Type is the single source
            // of truth for "corner-ness"; tangents follow.
            if (EditorGUI.EndChangeCheck())
            {
                NodeType newType = (NodeType)typeProp.enumValueIndex;
                if (newType == NodeType.Corner)
                {
                    inProp.vector2Value = Vector2.zero;
                    outProp.vector2Value = Vector2.zero;
                }
            }
            float y = rect.y + SlotHeaderHeight + SlotInnerPad;

            EditorGUI.indentLevel++;
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, line), posProp, new GUIContent("Position"));
            y += line + pad;

            NodeType nt = (NodeType)typeProp.enumValueIndex;
            if (nt == NodeType.Bezier || nt == NodeType.Smooth)
            {
                EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, line), inProp, new GUIContent("In Tangent"));
                y += line + pad;
                EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, line), outProp, new GUIContent("Out Tangent"));
            }
            EditorGUI.indentLevel--;
        }

        private static float SlotHeight(SerializedProperty slotProp)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            var typeProp = slotProp.FindPropertyRelative("type");
            NodeType nt = (NodeType)typeProp.enumValueIndex;
            // header + position; tangents only if Bezier/Smooth.
            float h = SlotHeaderHeight + SlotInnerPad + line + pad;
            if (nt == NodeType.Bezier || nt == NodeType.Smooth)
            {
                h += (line + pad) * 2;
            }
            return h;
        }

        // Place the new slot near the previous last slot so the path
        // doesn't appear to "spawn" at (0, 0). Offset is intentionally
        // small so the user can see where the new node landed and tweak.
        private static void AddNodeAtEnd(SerializedProperty shapeProp, int currentActive)
        {
            int newIndex = currentActive;
            if (newIndex >= PrimitiveShapeSource.MaxFreeNodes) return;

            Vector2 seedPos = Vector2.zero;
            if (currentActive > 0)
            {
                var prevSlot = shapeProp.FindPropertyRelative(SlotName(currentActive - 1));
                if (prevSlot != null)
                {
                    seedPos = prevSlot.FindPropertyRelative("position").vector2Value + new Vector2(20f, 0f);
                }
            }

            var slot = shapeProp.FindPropertyRelative(SlotName(newIndex));
            if (slot == null) return;
            slot.FindPropertyRelative("position").vector2Value = seedPos;
            slot.FindPropertyRelative("inTangent").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("outTangent").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("type").enumValueIndex = (int)NodeType.Corner;
        }

        private static void ClearSlot(SerializedProperty shapeProp, int index)
        {
            var slot = shapeProp.FindPropertyRelative(SlotName(index));
            if (slot == null) return;
            slot.FindPropertyRelative("position").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("inTangent").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("outTangent").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("type").enumValueIndex = (int)NodeType.Corner;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            if (!property.isExpanded) return line;

            var kindProp = property.FindPropertyRelative("kind");
            var kind = (ShapeKind)kindProp.enumValueIndex;

            int rows = 1 /*kind*/;
            if (kind != ShapeKind.FreePath) rows++; // center
            if (kind == ShapeKind.Circle || kind == ShapeKind.Ellipse
                || kind == ShapeKind.Rectangle || kind == ShapeKind.RoundedRectangle
                || kind == ShapeKind.Polygon) rows++;
            if (kind == ShapeKind.Polygon) rows++;
            if (kind == ShapeKind.RoundedRectangle) rows++;
            if (kind == ShapeKind.Circle || kind == ShapeKind.Ellipse || kind == ShapeKind.RoundedRectangle) rows++;

            float h = line + pad + rows * (line + pad);
            if (kind == ShapeKind.FreePath)
            {
                h += (line + pad); // freeClosed
                h += (line + pad); // bezierSamplesPerSegment
                h += (line + pad); // activeNodeCount + buttons row

                var activeNodeCountProp = property.FindPropertyRelative("activeNodeCount");
                int active = Mathf.Clamp(activeNodeCountProp.intValue, 0, PrimitiveShapeSource.MaxFreeNodes);
                for (int i = 0; i < active; i++)
                {
                    var slotProp = property.FindPropertyRelative(SlotName(i));
                    if (slotProp == null) continue;
                    h += SlotHeight(slotProp) + pad;
                }
            }
            return h;
        }

        private static string SlotName(int i)
        {
            return i < 10 ? "m_Node0" + i : "m_Node" + i;
        }
    }
}
