using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// [Service] 메서드를 **CS 액션**으로 노출한다 (③ 트랙, #38~#42).
    ///
    /// [API] 와의 차이: 호출자가 플레이어가 아니라 **운영자**다. 생성되는 엔드포인트는
    /// JWT 의 sub 로 admin_user_role 을 조회해 cs 계열 롤(game-admin/cs-senior/cs-agent)을
    /// 검증하고, 실행을 admin_audit_log 에 기록한다. 어드민은 메타(cs_actions)를 읽어
    /// 플레이어 상세의 Admin Tools 버튼·모달을 자동으로 그린다 — 메서드 하나 = 버튼 하나.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CsActionAttribute : Attribute
    {
        /// <summary>어드민 버튼 라벨. 비우면 메서드 이름.</summary>
        public string Label;
        /// <summary>cs-senior 이상만 (game-admin 포함). GDPR 삭제 같은 파괴적 조작용.</summary>
        public bool SeniorOnly;
        /// <summary>어드민이 위험 조작으로 그린다 — 빨간 버튼 + 대상 ID 재입력 2단계 확인.</summary>
        public bool Dangerous;

        public CsActionAttribute(string label = null)
        {
            Label = label;
        }
    }
}
