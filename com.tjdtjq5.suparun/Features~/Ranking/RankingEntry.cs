using Tjdtjq5.SupaRun;

/// <summary>랭킹 엔트리. boardId + playerId 조합으로 관리.</summary>
[Table]
public class RankingEntry
{
    [PrimaryKey] public string id;
    [Index] public string boardId;
    [Index] public string playerId;
    /// <summary>그룹 ID. null이면 전체 랭킹.</summary>
    [Index] public string groupId;
    public string playerName;
    public long score;
    public string metadata;
    [CreatedAt] public long createdAt;
    [UpdatedAt] public long updatedAt;
}
