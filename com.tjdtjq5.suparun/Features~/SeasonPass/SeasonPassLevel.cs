using Tjdtjq5.SupaRun;

/// <summary>
/// 시즌패스 레벨별 보상 정의.
/// rewards JSON 예시: [{"type":"currency","id":"gold","amount":500},{"type":"item","id":"potion_hp","amount":3}]
/// </summary>
[Config]
public class SeasonPassLevel
{
    [PrimaryKey] public string id;          // "season_1_lv5"
    [Index] public string seasonId;         // SeasonPassDef.id
    /// <summary>레벨 (1~maxLevel).</summary>
    public int level;
    /// <summary>무료 보상. JSON 배열. null이면 무료 보상 없음.</summary>
    public string freeRewards;
    /// <summary>유료(프리미엄) 보상. JSON 배열. null이면 유료 보상 없음.</summary>
    public string premiumRewards;
}
