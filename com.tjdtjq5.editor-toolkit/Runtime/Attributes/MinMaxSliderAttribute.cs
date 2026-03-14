using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// Vector2 필드를 범위 슬라이더로 표시. x=min, y=max.
    ///
    /// 사용법:
    ///   [MinMaxSlider(0.5f, 3f)]
    ///   public Vector2 scaleRange = new(0.8f, 1.2f);
    /// </summary>
    public class MinMaxSliderAttribute : PropertyAttribute
    {
        public float Min { get; }
        public float Max { get; }

        public MinMaxSliderAttribute(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }
}
