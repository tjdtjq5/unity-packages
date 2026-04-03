using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 string 필드가 enum 값을 담고 있음을 표시.
    /// DefGenerator가 Def 클래스에서 해당 enum 타입 프로퍼티를 생성하고 자동 변환 코드를 emit한다.
    /// 숫자 문자열("0")과 이름 문자열("Auto") 모두 지원.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class EnumTypeAttribute : Attribute
    {
        public Type EnumType { get; }
        public EnumTypeAttribute(Type enumType) => EnumType = enumType;
    }
}
