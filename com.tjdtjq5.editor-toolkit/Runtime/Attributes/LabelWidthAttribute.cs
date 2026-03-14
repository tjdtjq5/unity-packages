using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 필드의 라벨 영역 폭을 변경.
    ///
    /// 사용법:
    ///   [LabelWidth(80)]
    ///   public float someLongNamedProperty;
    /// </summary>
    public class LabelWidthAttribute : PropertyAttribute
    {
        public float Width { get; }

        public LabelWidthAttribute(float width)
        {
            Width = width;
        }
    }
}
