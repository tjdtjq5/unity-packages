using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// string 컬럼이 어드레서블 주소임을 선언한다.
    /// 어드민 툴은 "루트에 컴포넌트 T를 가진 어드레서블 프리팹"의 주소 목록을 검색 드롭다운으로 렌더한다.
    /// 값 자체는 어드레서블 주소 string (DB 컬럼 text, 마이그레이션 영향 없음).
    /// 클라이언트는 그 주소를 AddrX.LoadAsync 등으로 그대로 로드하면 된다(별도 헬퍼 불필요).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ComponentAttribute : Attribute
    {
        /// <summary>어드레서블 프리팹 루트가 가져야 할 컴포넌트 타입.</summary>
        public Type ComponentType { get; }

        public ComponentAttribute(Type componentType)
        {
            ComponentType = componentType;
        }
    }
}
