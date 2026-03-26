using Tjdtjq5.SupaRun;

/// <summary>랭킹 엔트리. boardId + playerId 조합으로 관리.</summary>
[Table]
public class RankingEntry
{
    [PrimaryKey] public string id;
    /// <summary>리더보드 ID (예: "stage_clear", "pvp_rating").</summary>
    [Index] public string boardId;
    [Index] public string playerId;
    public string playerName;
    /// <summary>점수. 높을수록 상위.</summary>
    public long score;
    /// <summary>부가 데이터 (JSON 등 자유 형식).</summary>
    public string metadata;
    [CreatedAt] public long createdAt;
    [UpdatedAt] public long updatedAt;
}
