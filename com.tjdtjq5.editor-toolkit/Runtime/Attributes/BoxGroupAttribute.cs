using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// 연속된 필드들을 시각적 박스로 그룹핑.
    /// 같은 GroupName을 가진 필드는 반드시 연속 선언해야 함.
    /// InspectorButtonEditor 상속 에디터에서만 동작.
    ///
    /// 사용법:
    ///   [BoxGroup("공격")]
    ///   public float damage;
    ///   [BoxGroup("공격")]
    ///   public float range;
    ///
    ///   [BoxGroup("방어", 0.4f, 0.7f, 0.9f)]
    ///   public float armor;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class BoxGroupAttribute : PropertyAttribute
    {
        public string GroupName { get; }
        public Color Color { get; }

        public BoxGroupAttribute(string groupName)
        {
            GroupName = groupName;
            Color = Color.white;
        }

        public BoxGroupAttribute(string groupName, float r, float g, float b)
        {
            GroupName = groupName;
            Color = new Color(r, g, b);
        }
    }
}
