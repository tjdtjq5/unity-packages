using Tjdtjq5.SupaRun;

/// <summary>아이템 변동 이력. 획득/소모 시마다 기록.</summary>
[Table]
public class InventoryLog
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    public string itemDefId;
    public int change;                      // +획득, -소모
    [NotNull] public string reason;
    [CreatedAt] public long createdAt;
}
