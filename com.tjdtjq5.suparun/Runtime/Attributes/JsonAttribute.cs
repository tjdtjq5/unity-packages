using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 string 필드가 JSON 배열/객체를 담고 있음을 표시.
    /// TargetType을 지정하면 DefGenerator가 Def 클래스에서 해당 타입으로 자동 파싱 프로퍼티를 생성한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class JsonAttribute : Attribute
    {
        public Type TargetType { get; }
        public JsonAttribute() { }
        public JsonAttribute(Type targetType) => TargetType = targetType;
    }
}
