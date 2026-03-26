using Tjdtjq5.SupaRun;

/// <summary>보유 아이템. playerId + itemId 조합으로 관리.</summary>
[Table]
public class InventoryItem
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string itemId;
    public int amount;
    public string metadata;
    [CreatedAt] public long acquiredAt;
    [UpdatedAt] public long updatedAt;
}
