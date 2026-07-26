using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 string 컬럼이 <see cref="BaseType"/> 파생 중 **하나**를 담고 있음을 표시한다.
    /// 어드민은 셀을 텍스트가 아니라 "타입 드롭다운 + 그 타입의 필드 폼" 으로 연다.
    ///
    /// 컬럼 하나가 행마다 다른 뜻을 갖는 구조를 푸는 데 쓴다 —
    /// 예전에는 공용 컬럼 하나에 `[VisibleIf]` 를 잔뜩 달아 가렸다면,
    /// 이제 타입마다 자기 이름의 필드를 갖는다.
    ///
    /// <code>
    /// [SpecData("InGame")]
    /// public class SkillData
    /// {
    ///     [PrimaryKey] public string id;
    ///     public float cooldown;                                   // 뜻이 하나인 것만 공통
    ///     [Polymorphic(typeof(SkillPatternData))] public string pattern;
    /// }
    ///
    /// public class GunPatternData : SkillPatternData
    /// {
    ///     public float range;          // "자동 조준 탐색 거리" — 이름이 곧 뜻이다
    ///     public int magazine_size;
    /// }
    /// </code>
    ///
    /// 저장 형태는 노드 하나와 같다: <c>{"type":"GunPatternData","range":10,"magazine_size":3}</c>.
    /// 실제로 다형 필드는 **연결 없는 노드 하나**라, 카탈로그·역직렬화·필드 렌더러를
    /// <see cref="NodeGraphAttribute"/> 쪽과 공유한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class PolymorphicAttribute : Attribute
    {
        /// <summary>파생 목록을 모을 기준 타입.</summary>
        public Type BaseType { get; }

        public PolymorphicAttribute(Type baseType) => BaseType = baseType;
    }
}
