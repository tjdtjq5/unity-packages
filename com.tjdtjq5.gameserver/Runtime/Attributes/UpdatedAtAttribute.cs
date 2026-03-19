using System;

namespace Tjdtjq5.GameServer
{
    /// <summary>이 필드에 레코드 수정 시간이 자동으로 기록된다. Save 시마다 갱신. long (UnixTime) 또는 DateTime 타입.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class UpdatedAtAttribute : Attribute { }
}
