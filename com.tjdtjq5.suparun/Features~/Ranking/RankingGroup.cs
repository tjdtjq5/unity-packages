using Tjdtjq5.SupaRun;

/// <summary>그룹 랭킹 할당. 유저가 어떤 그룹에 속하는지 기록.</summary>
[Table]
public class RankingGroup
{
    [PrimaryKey] public string id;          // "{boardId}_{playerId}"
    [Index] public string boardId;
    [Index] public string playerId;
    [Index] public string groupId;
    [CreatedAt] public long createdAt;
}
