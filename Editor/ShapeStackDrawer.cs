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
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            Rect r = new Rect(position.x, position.y, position.width, line);

            property.isExpanded = EditorGUI.Foldout(r, property.isExpanded, label, true);
            if (!property.isExpanded) { EditorGUI.EndProperty(); return; }
            r.y += line + pad;

            EditorGUI.indentLevel++;

            EditorGUI.PropertyField(r, resampleProp); r.y += line + pad;

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

            // Header — slot label + intensity slider on the same row.
            // Intensity is the most-edited field by far, so it lives
            // here instead of being buried inside the foldout.
            Rect headerLabel = new Rect(rect.x, rect.y, 64f, line);
            Rect headerSlider = new Rect(rect.x + 66f, rect.y, rect.width - 66f, line);

            slotProp.isExpanded = EditorGUI.Foldout(headerLabel, slotProp.isExpanded,
                new GUIContent("Slot " + index), true);
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
    }
}
