using UnityEditor;
using UnityEngine;
using VMG.Core;

namespace VMG.EditorTools
{
    /// Custom drawer for PrimitiveShapeSource. Shows only the fields
    /// relevant to the selected ShapeKind. For FreePath, exposes the
    /// active flat node slots (Node00..) with compact per-slot rows
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
            var cornerRadiiProp = property.FindPropertyRelative("cornerRadii");
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
            if (showCornerRadius) { EditorGUI.PropertyField(r, cornerRadiiProp); r.y += line + pad; }
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
                    int op = DrawSlot(slotRect, slotProp, i, active);
                    r.y += h + pad;
                    if (op != 0)
                    {
                        // Mutating slots invalidates the loop's remaining
                        // slotProps, so apply the edit and bail out of this
                        // OnGUI pass — the inspector repaints immediately.
                        if (op > 0) InsertNodeAt(property, activeNodeCountProp, i + 1);
                        else RemoveNodeAt(property, activeNodeCountProp, i);
                        break;
                    }
                }
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        /// Draws one node slot. Returns +1 when the user clicked the
        /// insert button (insert AFTER this slot), -1 when they clicked
        /// delete, 0 otherwise. The caller performs the mutation so this
        /// stays free of SerializedProperty shifting.
        private static int DrawSlot(Rect rect, SerializedProperty slotProp, int index, int activeCount)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            int op = 0;

            var posProp = slotProp.FindPropertyRelative("position");
            var inProp = slotProp.FindPropertyRelative("inTangent");
            var outProp = slotProp.FindPropertyRelative("outTangent");
            var typeProp = slotProp.FindPropertyRelative("type");

            // Header bar — slot index, node type dropdown, and the
            // per-slot insert / delete buttons on the right.
            Rect header = new Rect(rect.x, rect.y, rect.width, SlotHeaderHeight);
            EditorGUI.LabelField(new Rect(header.x, header.y, 70f, header.height),
                new GUIContent("Node " + index));

            float btnW = 22f;
            Rect insRect = new Rect(header.xMax - btnW * 2f - 2f, header.y, btnW, header.height);
            Rect delRect = new Rect(header.xMax - btnW, header.y, btnW, header.height);
            float typeW = header.width - 72f - btnW * 2f - 6f;

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(
                new Rect(header.x + 72f, header.y, Mathf.Max(40f, typeW), header.height),
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

            // Buttons come last so they sit on top of the indent-level
            // fields and keep their exact header rects.
            using (new EditorGUI.DisabledScope(activeCount >= PrimitiveShapeSource.MaxFreeNodes))
            {
                if (GUI.Button(insRect, new GUIContent("+", "Insert a new node right after this one, at the midpoint of the following segment. Shifts later slots."), EditorStyles.miniButtonLeft))
                    op = 1;
            }
            using (new EditorGUI.DisabledScope(activeCount <= 0))
            {
                if (GUI.Button(delRect, new GUIContent("×", "Delete this node. Shifts later slots down."), EditorStyles.miniButtonRight))
                    op = -1;
            }
            return op;
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

        // Default offset for the first node, and the fallback length when
        // we have no direction information (single existing node with no
        // tangent). Tuned to match the package's default authoring scale
        // (PrimitiveShapeSource.Normalize lazies size to 100,100) — small
        // enough to land near the previous node, large enough to be
        // visible and pickable.
        private const float DefaultNodeSpacing = 20f;

        // Place the new slot so the path continues naturally from the
        // last node: pick the direction of the previous node's outTangent
        // if present, else the previous segment's direction, else fall
        // back to (+x, 0). Distance matches the previous segment so the
        // visual spacing stays consistent.
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
                    Vector2 prevPos = prevSlot.FindPropertyRelative("position").vector2Value;
                    Vector2 prevOut = prevSlot.FindPropertyRelative("outTangent").vector2Value;

                    Vector2 dir = Vector2.zero;
                    float dist = DefaultNodeSpacing;

                    if (prevOut.sqrMagnitude > 1e-6f)
                    {
                        dir = prevOut.normalized;
                    }
                    else if (currentActive >= 2)
                    {
                        var prev2Slot = shapeProp.FindPropertyRelative(SlotName(currentActive - 2));
                        if (prev2Slot != null)
                        {
                            Vector2 prev2Pos = prev2Slot.FindPropertyRelative("position").vector2Value;
                            Vector2 seg = prevPos - prev2Pos;
                            float segLen = seg.magnitude;
                            if (segLen > 1e-4f)
                            {
                                dir = seg / segLen;
                                dist = segLen;
                            }
                        }
                    }

                    if (dir.sqrMagnitude < 1e-8f) dir = new Vector2(1f, 0f);
                    seedPos = prevPos + dir * dist;
                }
            }

            var slot = shapeProp.FindPropertyRelative(SlotName(newIndex));
            if (slot == null) return;
            slot.FindPropertyRelative("position").vector2Value = seedPos;
            slot.FindPropertyRelative("inTangent").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("outTangent").vector2Value = Vector2.zero;
            slot.FindPropertyRelative("type").enumValueIndex = (int)NodeType.Corner;
        }

        // --- Mid-list insert / delete -------------------------------
        //
        // The 64 slots are fixed named fields, so "insert" means copying
        // every later slot one place along. That renames what each slot
        // holds, which is exactly what makes existing AnimationClip
        // curves (bound to NodeNN by name) point at the wrong node
        // afterwards. WarnIfCurvesWouldShift gives the user a chance to
        // back out before that happens.

        private static void CopySlot(SerializedProperty shapeProp, int from, int to)
        {
            var src = shapeProp.FindPropertyRelative(SlotName(from));
            var dst = shapeProp.FindPropertyRelative(SlotName(to));
            if (src == null || dst == null) return;
            dst.FindPropertyRelative("position").vector2Value = src.FindPropertyRelative("position").vector2Value;
            dst.FindPropertyRelative("inTangent").vector2Value = src.FindPropertyRelative("inTangent").vector2Value;
            dst.FindPropertyRelative("outTangent").vector2Value = src.FindPropertyRelative("outTangent").vector2Value;
            dst.FindPropertyRelative("type").enumValueIndex = src.FindPropertyRelative("type").enumValueIndex;
        }

        /// Inserts a node at `index`, seeded at the midpoint of the
        /// segment it splits, shifting slots [index, active) up by one.
        internal static void InsertNodeAt(SerializedProperty shapeProp,
                                          SerializedProperty activeProp, int index)
        {
            int active = Mathf.Clamp(activeProp.intValue, 0, PrimitiveShapeSource.MaxFreeNodes);
            if (active >= PrimitiveShapeSource.MaxFreeNodes) return;
            if (index < 0 || index > active) return;
            if (!WarnIfCurvesWouldShift(shapeProp, index, "inserting")) return;

            // Seed position BEFORE shifting, while index-1 / index still
            // name the nodes that bracket the new one.
            Vector2 seed = MidpointFor(shapeProp, index, active);

            for (int i = active; i > index; i--) CopySlot(shapeProp, i - 1, i);

            var slot = shapeProp.FindPropertyRelative(SlotName(index));
            if (slot != null)
            {
                slot.FindPropertyRelative("position").vector2Value = seed;
                slot.FindPropertyRelative("inTangent").vector2Value = Vector2.zero;
                slot.FindPropertyRelative("outTangent").vector2Value = Vector2.zero;
                // Inherit the preceding node's type so inserting into a
                // bezier run doesn't drop a hard corner into a curve.
                int prev = index - 1;
                if (prev >= 0)
                {
                    var prevSlot = shapeProp.FindPropertyRelative(SlotName(prev));
                    if (prevSlot != null)
                        slot.FindPropertyRelative("type").enumValueIndex =
                            prevSlot.FindPropertyRelative("type").enumValueIndex;
                }
                else
                {
                    slot.FindPropertyRelative("type").enumValueIndex = (int)NodeType.Corner;
                }
            }
            activeProp.intValue = active + 1;
        }

        /// Midpoint of the segment that the new node at `index` splits.
        /// Falls back to offsetting the neighbour when there is no real
        /// segment (index at the open end, or a single-node path).
        private static Vector2 MidpointFor(SerializedProperty shapeProp, int index, int active)
        {
            if (active == 0) return Vector2.zero;

            var freeClosedProp = shapeProp.FindPropertyRelative("freeClosed");
            bool closed = freeClosedProp != null && freeClosedProp.boolValue;

            int prev = index - 1;
            int next = index;
            if (next >= active) next = closed ? 0 : -1;
            if (prev < 0) prev = closed ? active - 1 : -1;

            if (prev >= 0 && next >= 0 && prev != next)
                return (SlotPos(shapeProp, prev) + SlotPos(shapeProp, next)) * 0.5f;

            // Open-ended: place the node one default spacing beyond the
            // single neighbour so it is visible and pickable.
            int anchorIdx = prev >= 0 ? prev : next;
            if (anchorIdx < 0) return Vector2.zero;
            Vector2 anchor = SlotPos(shapeProp, anchorIdx);
            return anchor + new Vector2(DefaultNodeSpacing, 0f);
        }

        private static Vector2 SlotPos(SerializedProperty shapeProp, int i)
        {
            var slot = shapeProp.FindPropertyRelative(SlotName(i));
            return slot == null ? Vector2.zero : slot.FindPropertyRelative("position").vector2Value;
        }

        /// Removes the node at `index`, shifting slots (index, active)
        /// down by one and clearing the vacated tail slot.
        internal static void RemoveNodeAt(SerializedProperty shapeProp,
                                          SerializedProperty activeProp, int index)
        {
            int active = Mathf.Clamp(activeProp.intValue, 0, PrimitiveShapeSource.MaxFreeNodes);
            if (active <= 0 || index < 0 || index >= active) return;
            if (!WarnIfCurvesWouldShift(shapeProp, index, "deleting")) return;

            for (int i = index; i < active - 1; i++) CopySlot(shapeProp, i + 1, i);
            ClearSlot(shapeProp, active - 1);
            activeProp.intValue = active - 1;
        }

        /// Returns true when it is safe to proceed. Scans every
        /// AnimationClip on the target's animator for curves bound to a
        /// node slot at or after `index` — those are the ones a shift
        /// would silently re-target — and asks for confirmation.
        /// Unity cannot remap them, so the only honest options are
        /// "proceed and fix the curves by hand" or "cancel".
        private static bool WarnIfCurvesWouldShift(SerializedProperty shapeProp, int index, string verb)
        {
            int affected = CountAffectedCurves(shapeProp, index);
            if (affected <= 0) return true;

            string slotName = index < 10 ? "Node0" + index : "Node" + index;
            return EditorUtility.DisplayDialog(
                "Animation curves will be re-targeted",
                affected + " animation curve(s) are bound to node slots at or after " + slotName + ".\n\n" +
                "Slots are keyframed by name, so " + verb + " a node here shifts those curves onto " +
                "different nodes. Unity cannot remap them automatically - you will need to fix " +
                "the affected curves by hand.\n\nProceed anyway?",
                "Proceed", "Cancel");
        }

        private static int CountAffectedCurves(SerializedProperty shapeProp, int index)
        {
            var target = shapeProp.serializedObject.targetObject as Component;
            if (target == null) return 0;

            // Walk up to the Animator that would drive this component;
            // clips live there, not on the renderer.
            var animator = target.GetComponentInParent<Animator>();
            AnimationClip[] clips = null;
            if (animator != null && animator.runtimeAnimatorController != null)
                clips = animator.runtimeAnimatorController.animationClips;
            if (clips == null || clips.Length == 0) return 0;

            // Curve paths look like "ShapeStack.Slot0.shape.Node03.position.x".
            // We only need the "NodeNN" token and whether NN >= index.
            int count = 0;
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var b in bindings)
                {
                    int slot = SlotIndexInBinding(b.propertyName);
                    if (slot >= index) count++;
                }
            }
            return count;
        }

        /// Extracts NN from a binding path containing ".NodeNN." - returns
        /// -1 when the binding is not a free-node channel.
        private static int SlotIndexInBinding(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return -1;
            const string token = "Node";
            int at = propertyName.IndexOf(token, System.StringComparison.Ordinal);
            while (at >= 0)
            {
                int d = at + token.Length;
                if (d + 1 < propertyName.Length &&
                    char.IsDigit(propertyName[d]) && char.IsDigit(propertyName[d + 1]))
                {
                    // Must be a whole token: the char after the 2 digits
                    // is '.' (or end), so "Node12" matches but "Node123"
                    // does not.
                    int after = d + 2;
                    if (after >= propertyName.Length || propertyName[after] == '.')
                        return (propertyName[d] - '0') * 10 + (propertyName[d + 1] - '0');
                }
                at = propertyName.IndexOf(token, at + 1, System.StringComparison.Ordinal);
            }
            return -1;
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
            return i < 10 ? "Node0" + i : "Node" + i;
        }
    }
}
