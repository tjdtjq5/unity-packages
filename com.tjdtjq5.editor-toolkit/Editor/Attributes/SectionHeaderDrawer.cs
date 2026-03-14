#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(SectionHeaderAttribute))]
    public class SectionHeaderDrawer : DecoratorDrawer
    {
        static readonly Color BG_HEADER = new(0.11f, 0.11f, 0.14f);

        const float HEADER_HEIGHT = 26f;
        const float SPACING = 4f;
        const float BAR_WIDTH = 4f;

        public override float GetHeight()
        {
            return HEADER_HEIGHT + SPACING;
        }

        public override void OnGUI(Rect position)
        {
            var attr = (SectionHeaderAttribute)attribute;

            var headerRect = new Rect(position.x, position.y, position.width, HEADER_HEIGHT);

            // 배경
            EditorGUI.DrawRect(headerRect, BG_HEADER);

            // 왼쪽 색상 바
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, BAR_WIDTH, headerRect.height), attr.Color);

            // 타이틀 텍스트
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = attr.Color }
            };
            var textRect = new Rect(headerRect.x + BAR_WIDTH + 8, headerRect.y, headerRect.width - BAR_WIDTH - 8, headerRect.height);
            EditorGUI.LabelField(textRect, attr.Title, titleStyle);
        }
    }
}
#endif
