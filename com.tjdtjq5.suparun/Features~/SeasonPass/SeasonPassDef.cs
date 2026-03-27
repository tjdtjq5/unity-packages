using Tjdtjq5.SupaRun;

/// <summary>시즌패스 정의. 관리자가 등록.</summary>
[Config("seasonpass")]
public class SeasonPassDef
{
    [PrimaryKey] public string id;          // "season_1"
    [NotNull] public string name;           // "시즌 1 배틀패스"
    /// <summary>최대 레벨.</summary>
    public int maxLevel;                    // 50
    /// <summary>레벨당 필요 XP.</summary>
    public long xpPerLevel;                 // 1000
    /// <summary>프리미엄 구매 가격.</summary>
    public long premiumPrice;
    /// <summary>프리미엄 구매 재화.</summary>
    public string premiumCurrencyId;        // "diamond"
    /// <summary>시즌 시작 (Unix 초).</summary>
    public long startAt;
    /// <summary>시즌 종료 (Unix 초).</summary>
    public long endAt;
}
