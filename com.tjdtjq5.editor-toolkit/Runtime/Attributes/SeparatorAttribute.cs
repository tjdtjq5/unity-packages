using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 필드 위에 수평 구분선 삽입.
    /// [Separator] 또는 [Separator(2, 0.4f, 0.4f, 0.4f)]
    /// </summary>
    public class SeparatorAttribute : PropertyAttribute
    {
        public float Thickness { get; }
        public Color Color { get; }

        public SeparatorAttribute()
        {
            Thickness = 1f;
            Color = new Color(0.3f, 0.3f, 0.36f);
        }

        public SeparatorAttribute(float thickness)
        {
            Thickness = thickness;
            Color = new Color(0.3f, 0.3f, 0.36f);
        }

        public SeparatorAttribute(float thickness, float r, float g, float b)
        {
            Thickness = thickness;
            Color = new Color(r, g, b);
        }
    }
}
