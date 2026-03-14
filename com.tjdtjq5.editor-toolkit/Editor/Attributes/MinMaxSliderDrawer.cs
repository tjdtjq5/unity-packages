#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(MinMaxSliderAttribute))]
    public class MinMaxSliderDrawer : PropertyDrawer
    {
        const float FIELD_WIDTH = 50f;
        const float GAP = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Vector2)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var attr = (MinMaxSliderAttribute)attribute;
            var vec = property.vector2Value;
            float minVal = vec.x;
            float maxVal = vec.y;

            EditorGUI.BeginProperty(position, label, property);

            // 라벨
            position = EditorGUI.PrefixLabel(position, label);

            // min 필드
            var minRect = new Rect(position.x, position.y, FIELD_WIDTH, position.height);
            minVal = EditorGUI.FloatField(minRect, minVal);

            // 슬라이더
            var sliderRect = new Rect(
                position.x + FIELD_WIDTH + GAP, position.y,
                position.width - (FIELD_WIDTH + GAP) * 2, position.height);
            EditorGUI.MinMaxSlider(sliderRect, ref minVal, ref maxVal, attr.Min, attr.Max);

            // max 필드
            var maxRect = new Rect(
                position.x + position.width - FIELD_WIDTH, position.y,
                FIELD_WIDTH, position.height);
            maxVal = EditorGUI.FloatField(maxRect, maxVal);

            // 클램프
            minVal = Mathf.Clamp(minVal, attr.Min, maxVal);
            maxVal = Mathf.Clamp(maxVal, minVal, attr.Max);

            property.vector2Value = new Vector2(minVal, maxVal);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
#endif
