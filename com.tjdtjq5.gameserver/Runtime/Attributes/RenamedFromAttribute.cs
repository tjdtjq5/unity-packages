using System;

namespace Tjdtjq5.GameServer
{
    /// <summary>필드 이름 변경 시 마이그레이션이 감지할 수 있도록 이전 이름을 지정한다.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class RenamedFromAttribute : Attribute
    {
        public string OldName { get; }

        public RenamedFromAttribute(string oldName)
        {
            OldName = oldName;
        }
    }
}
