#nullable enable
namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// config 세션 협상 결과 (ADR-0010 결정 6, #35).
    ///
    /// 세션 시작에 활성 config 버전을 스탬프하고, 세션 동안 [SpecData] 조회는 그 시점 값으로
    /// 고정된다(런타임 세션 캐시). 게시(publish)가 세션 중에 일어나도 플레이 중 밸런스가
    /// 바뀌지 않는다 — 새 버전은 새 세션(재시작 또는 명시적 재협상)부터다.
    ///
    /// **알려진 경계**: 고정 단위는 타입별 첫 조회 시점이다(lazy). 게시 커밋을 사이에 두고
    /// 서로 다른 타입을 처음 읽으면 한 세션에 두 버전이 섞일 수 있다 — 세션 시작 직후 쓸
    /// 타입을 모두 한 번 읽어 두면(게임의 DataRegistry 로드 패턴) 실질적으로 전체가 고정된다.
    /// </summary>
    public class ConfigSessionInfo
    {
        /// <summary>활성 버전의 내용 해시. null = 스탬프 없음(게시를 한 번도 안 한 환경 — dev 등).</summary>
        public string? ActiveVersionHash;
        public string? ActiveVersionGitSha;
        public long ActivePublishedAt;

        /// <summary>
        /// 클라 logic version 이 서버 허용 범위 안인가. false 면 강제 업데이트 안내로 빠져야 한다 —
        /// 판단은 여기까지고, UI·차단은 게임의 책임이다(SupaRun 은 범용 데이터 계층).
        /// </summary>
        public bool LogicCompatible = true;
        /// <summary>서버 허용 범위(suparun_meta.logic_version_range). 0 = 제한 없음.</summary>
        public int LogicMin;
        public int LogicMax;
    }
}
