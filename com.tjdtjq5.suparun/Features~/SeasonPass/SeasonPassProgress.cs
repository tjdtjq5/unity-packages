using Tjdtjq5.SupaRun;

/// <summary>유저별 시즌패스 진행도.</summary>
[Table]
public class SeasonPassProgress
{
    [PrimaryKey] public string id;          // "{seasonId}_{playerId}"
    [Index] public string seasonId;
    [Index] public string playerId;
    /// <summary>누적 XP.</summary>
    public long xp;
    /// <summary>현재 레벨.</summary>
    public int level;
    /// <summary>프리미엄 구매 여부.</summary>
    public bool isPremium;
    /// <summary>수령한 무료 레벨 목록. JSON "[1,2,3]".</summary>
    public string claimedFree;
    /// <summary>수령한 유료 레벨 목록. JSON "[1,2,3]".</summary>
    public string claimedPremium;
    [UpdatedAt] public long updatedAt;
}
