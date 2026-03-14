#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(ProgressBarAttribute))]
    public class ProgressBarDrawer : PropertyDrawer
    {
        const float BAR_HEIGHT = 20f;
        static readonly Color BG_BAR = new(0.10f, 0.10f, 0.13f);
        static readonly Color BORDER = new(0.30f, 0.30f, 0.36f);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Float &&
                property.propertyType != SerializedPropertyType.Integer)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var attr = (ProgressBarAttribute)attribute;

            EditorGUI.BeginProperty(position, label, property);

            // 라벨
            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, BAR_HEIGHT);
            EditorGUI.LabelField(labelRect, label);

            // 바 영역
            var barRect = new Rect(
                position.x + EditorGUIUtility.labelWidth + 2, position.y,
                position.width - EditorGUIUtility.labelWidth - 2, BAR_HEIGHT);

            float value = property.propertyType == SerializedPropertyType.Float
                ? property.floatValue
                : property.intValue;

            float ratio = Mathf.InverseLerp(attr.Min, attr.Max, value);

            // 배경
            EditorGUI.DrawRect(barRect, BG_BAR);

            // 채움
            var fillRect = new Rect(barRect.x + 1, barRect.y + 1,
                (barRect.width - 2) * Mathf.Clamp01(ratio), barRect.height - 2);
            EditorGUI.DrawRect(fillRect, attr.Color);

            // 테두리
            DrawBorder(barRect, BORDER);

            // 텍스트
            string text = attr.Label != null
                ? $"{attr.Label}: {value:F1} / {attr.Max:F0}"
                : $"{value:F1} ({ratio * 100:F0}%)";

            var textStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            EditorGUI.DropShadowLabel(barRect, text, textStyle);

            // 클릭/드래그로 값 변경
            var evt = Event.current;
            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag)
                && barRect.Contains(evt.mousePosition))
            {
                float t = (evt.mousePosition.x - barRect.x) / barRect.width;
                float newValue = Mathf.Lerp(attr.Min, attr.Max, Mathf.Clamp01(t));

                if (property.propertyType == SerializedPropertyType.Float)
                    property.floatValue = newValue;
                else
                    property.intValue = Mathf.RoundToInt(newValue);

                evt.Use();
                GUI.changed = true;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return BAR_HEIGHT;
        }

        static void DrawBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }
    }
}
#endif
