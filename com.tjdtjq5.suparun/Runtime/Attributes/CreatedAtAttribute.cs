using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>이 필드에 레코드 생성 시간이 자동으로 기록된다. long (UnixTime) 또는 DateTime 타입.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class CreatedAtAttribute : Attribute { }
}
