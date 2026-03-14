using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// Inspector에 색상 바 + 볼드 타이틀 헤더를 삽입.
    /// EditorTabBase.DrawSectionFoldout 스타일과 동일한 룩.
    /// </summary>
    public class SectionHeaderAttribute : PropertyAttribute
    {
        public string Title { get; }
        public Color Color { get; }

        public SectionHeaderAttribute(string title)
        {
            Title = title;
            Color = Color.white;
        }

        public SectionHeaderAttribute(string title, float r, float g, float b)
        {
            Title = title;
            Color = new Color(r, g, b);
        }
    }
}
