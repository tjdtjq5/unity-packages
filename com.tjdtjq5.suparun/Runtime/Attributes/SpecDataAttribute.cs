using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>게임 설정 데이터. 서버에서 읽기 전용.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SpecDataAttribute : Attribute
    {
        public string Group { get; }
        public SpecDataAttribute() { }
        public SpecDataAttribute(string group) => Group = group;
    }
}
