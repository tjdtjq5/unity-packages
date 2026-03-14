using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// float/int 필드를 프로그레스바로 시각화. 클릭/드래그로 값 변경 가능.
    ///
    /// 사용법:
    ///   [ProgressBar(0, 100)]
    ///   public float hp = 75;
    ///
    ///   [ProgressBar(0, 1, 0.4f, 0.8f, 0.4f)]
    ///   public float ratio = 0.5f;
    ///
    ///   [ProgressBar(0, 100, "HP")]
    ///   public float hp = 75;
    /// </summary>
    public class ProgressBarAttribute : PropertyAttribute
    {
        public float Min { get; }
        public float Max { get; }
        public Color Color { get; }
        public string Label { get; }

        public ProgressBarAttribute(float min, float max)
        {
            Min = min;
            Max = max;
            Color = new Color(0.30f, 0.70f, 0.30f);
            Label = null;
        }

        public ProgressBarAttribute(float min, float max, string label)
        {
            Min = min;
            Max = max;
            Color = new Color(0.30f, 0.70f, 0.30f);
            Label = label;
        }

        public ProgressBarAttribute(float min, float max, float r, float g, float b)
        {
            Min = min;
            Max = max;
            Color = new Color(r, g, b);
            Label = null;
        }

        public ProgressBarAttribute(float min, float max, string label, float r, float g, float b)
        {
            Min = min;
            Max = max;
            Color = new Color(r, g, b);
            Label = label;
        }
    }
}
