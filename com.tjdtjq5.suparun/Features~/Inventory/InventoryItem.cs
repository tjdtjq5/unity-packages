using Tjdtjq5.SupaRun;

/// <summary>
/// 보유 아이템.
/// 스택형(소모품): id = "{playerId}_{itemDefId}", amount = N
/// 개별형(장비): id = UUID, amount = 1, metadata = JSON
/// </summary>
[Table]
public class InventoryItem
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string itemDefId;
    public int amount;
    public string metadata;                 // 장비: {"enhance":5, "durability":80}, 스택: null
    [CreatedAt] public long acquiredAt;
    [UpdatedAt] public long updatedAt;
}
