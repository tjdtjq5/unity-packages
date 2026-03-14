using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 메서드에 붙이면 Inspector에 버튼으로 표시.
    /// MonoBehaviour의 CustomEditor에서 자동 수집.
    ///
    /// 사용법:
    ///   [InspectorButton]                               — 메서드 이름으로 버튼
    ///   [InspectorButton("Spawn All")]                  — 커스텀 라벨
    ///   [InspectorButton("Clear", 1f, 0.4f, 0.4f)]     — 라벨 + 색상
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class InspectorButtonAttribute : Attribute
    {
        public string Label { get; }
        public Color Color { get; }
        public bool HasColor { get; }
        public float Height { get; }

        public InspectorButtonAttribute()
        {
            Label = null;
            Color = Color.white;
            HasColor = false;
            Height = 28f;
        }

        public InspectorButtonAttribute(string label)
        {
            Label = label;
            Color = Color.white;
            HasColor = false;
            Height = 28f;
        }

        public InspectorButtonAttribute(string label, float r, float g, float b)
        {
            Label = label;
            Color = new Color(r, g, b);
            HasColor = true;
            Height = 28f;
        }

        public InspectorButtonAttribute(string label, float height, float r, float g, float b)
        {
            Label = label;
            Color = new Color(r, g, b);
            HasColor = true;
            Height = height;
        }
    }
}
