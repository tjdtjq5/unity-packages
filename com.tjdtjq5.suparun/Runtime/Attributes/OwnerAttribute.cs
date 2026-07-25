using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 필드가 행의 소유자(Supabase Auth UUID)임을 표시한다. RLS 정책 생성에만 쓰인다.
    ///
    /// 붙이면 "본인 읽기 + 관리자 전체", 안 붙이면 "관리자만" 으로 정책이 갈린다.
    /// 컬럼 이름 관례로 판별하지 않는 이유: 실제 테이블의 소유자 컬럼이 제각각이고
    /// (userid / playerid / hostuserid / player_id), 이름만 비슷하고 소유자가 아닌 컬럼
    /// (admin_audit_log.admin_id)을 잘못 잡으면 조용히 틀린 정책이 깔린다.
    ///
    /// **값은 반드시 Supabase Auth UUID 여야 한다** — 정책이 auth.uid() 와 직접 비교한다.
    /// 게임 내부 ID를 넣으면 본인 데이터도 못 읽는다.
    /// </summary>
    /// <example>
    /// [UserData]
    /// public class GameResult
    /// {
    ///     [PrimaryKey] public string id;
    ///     [Owner] public string userId;   // auth.uid() 와 비교된다
    /// }
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public class OwnerAttribute : Attribute { }
}
