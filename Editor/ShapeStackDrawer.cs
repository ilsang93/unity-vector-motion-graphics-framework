using UnityEditor;
using UnityEngine;
using VMG.Core;

namespace VMG.EditorTools
{
    /// Inspector for ShapeStack. Each of the 4 slots renders as a
    /// foldout containing an intensity slider and the standard
    /// PrimitiveShapeSource drawer.
    [CustomPropertyDrawer(typeof(ShapeStack))]
    internal sealed class ShapeStackDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var resampleProp = property.FindPropertyRelative("resampleCount");
            var alignmentProp = property.FindPropertyRelative("alignment");
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            Rect r = new Rect(position.x, position.y, position.width, line);

            // Header row: foldout on the left, "⋯" stack menu on the right.
            // Menu items are infrequent ops (reset / swap) so a single
            // dropdown keeps the header uncluttered.
            const float MenuW = 22f;
            Rect foldRect = new Rect(r.x, r.y, r.width - MenuW - 2f, r.height);
            Rect menuRect = new Rect(r.x + r.width - MenuW, r.y, MenuW, r.height);
            property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, label, true);
            if (GUI.Button(menuRect, new GUIContent("⋯", "Stack actions: reset intensities or swap slots."), EditorStyles.miniButton))
            {
                ShowStackMenu(property);
            }
            if (!property.isExpanded) { EditorGUI.EndProperty(); return; }
            r.y += line + pad;

            EditorGUI.indentLevel++;

            EditorGUI.PropertyField(r, resampleProp); r.y += line + pad;
            EditorGUI.PropertyField(r, alignmentProp); r.y += line + pad;

            for (int i = 0; i < ShapeStack.MaxSlots; i++)
            {
                var slotProp = property.FindPropertyRelative(SlotName(i));
                if (slotProp == null) continue;
                float h = SlotHeight(slotProp);
                Rect slotRect = new Rect(r.x, r.y, r.width, h);
                DrawSlot(slotRect, slotProp, i);
                r.y += h + pad;
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static void DrawSlot(Rect rect, SerializedProperty slotProp, int index)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;

            var intensityProp = slotProp.FindPropertyRelative("intensity");
            var shapeProp = slotProp.FindPropertyRelative("shape");

            // Slots that don't contribute (intensity == 0) get a dimmed
            // header label so the user can scan the stack at a glance and
            // see which slots are actually live. No auto-collapse — the
            // user's expanded/collapsed state stays under their control.
            bool inactive = intensityProp.floatValue <= 0f;

            // Header — slot label + intensity slider on the same row.
            // Intensity is the most-edited field by far, so it lives
            // here instead of being buried inside the foldout.
            Rect headerLabel = new Rect(rect.x, rect.y, 64f, line);
            Rect headerSlider = new Rect(rect.x + 66f, rect.y, rect.width - 66f, line);

            string label = inactive ? "Slot " + index + "  ·" : "Slot " + index;
            Color prevContent = GUI.contentColor;
            if (inactive) GUI.contentColor = new Color(prevContent.r, prevContent.g, prevContent.b, 0.5f);
            slotProp.isExpanded = EditorGUI.Foldout(headerLabel, slotProp.isExpanded,
                new GUIContent(label), true);
            if (inactive) GUI.contentColor = prevContent;
            EditorGUI.PropertyField(headerSlider, intensityProp, GUIContent.none);

            if (!slotProp.isExpanded) return;

            float y = rect.y + line + pad;
            EditorGUI.indentLevel++;
            float shapeH = EditorGUI.GetPropertyHeight(shapeProp, true);
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, shapeH), shapeProp, true);
            EditorGUI.indentLevel--;
        }

        private static float SlotHeight(SerializedProperty slotProp)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            float h = line; // header
            if (slotProp.isExpanded)
            {
                var shapeProp = slotProp.FindPropertyRelative("shape");
                h += pad + EditorGUI.GetPropertyHeight(shapeProp, true);
            }
            return h;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            if (!property.isExpanded) return line;

            float h = line + pad;            // foldout
            h += line + pad;                 // resampleCount
            h += line + pad;                 // alignment
            for (int i = 0; i < ShapeStack.MaxSlots; i++)
            {
                var slotProp = property.FindPropertyRelative(SlotName(i));
                if (slotProp == null) continue;
                h += SlotHeight(slotProp) + pad;
            }
            return h;
        }

        private static string SlotName(int i)
        {
            return "m_Slot" + i;
        }

        // Header "⋯" menu. Reset returns the stack to its default state
        // (slot 0 fully on, others off). Swap exchanges two whole slots
        // including their shape data — used when the user wants to
        // reorder which slot acts as the visual "base".
        private static void ShowStackMenu(SerializedProperty stackProp)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Reset intensities (slot 0 = 1, others = 0)"), false,
                () => ResetIntensities(stackProp));
            menu.AddSeparator(string.Empty);
            for (int a = 0; a < ShapeStack.MaxSlots; a++)
            {
                for (int b = a + 1; b < ShapeStack.MaxSlots; b++)
                {
                    int ia = a, ib = b;
                    menu.AddItem(new GUIContent("Swap/Slot " + ia + " ↔ Slot " + ib), false,
                        () => SwapSlots(stackProp, ia, ib));
                }
            }
            menu.ShowAsContext();
        }

        private static void ResetIntensities(SerializedProperty stackProp)
        {
            stackProp.serializedObject.Update();
            for (int i = 0; i < ShapeStack.MaxSlots; i++)
            {
                var slot = stackProp.FindPropertyRelative(SlotName(i));
                if (slot == null) continue;
                var intensity = slot.FindPropertyRelative("intensity");
                if (intensity == null) continue;
                intensity.floatValue = i == 0 ? 1f : 0f;
            }
            stackProp.serializedObject.ApplyModifiedProperties();
        }

        // Whole-slot exchange via boxedValue (Unity 2022.1+) so shape
        // data, intensity, and any future ShapeSlot fields move together
        // without per-field bookkeeping here.
        private static void SwapSlots(SerializedProperty stackProp, int a, int b)
        {
            if (a == b) return;
            stackProp.serializedObject.Update();
            var slotA = stackProp.FindPropertyRelative(SlotName(a));
            var slotB = stackProp.FindPropertyRelative(SlotName(b));
            if (slotA == null || slotB == null) return;
            object tmp = slotA.boxedValue;
            slotA.boxedValue = slotB.boxedValue;
            slotB.boxedValue = tmp;
            stackProp.serializedObject.ApplyModifiedProperties();
        }
    }
}
