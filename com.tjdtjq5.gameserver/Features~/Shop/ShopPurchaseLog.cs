using Tjdtjq5.GameServer;

/// <summary>구매 이력.</summary>
[Table]
public class ShopPurchaseLog
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string productId;
    public int price;
    public string currencyId;
    [CreatedAt] public long purchasedAt;
}
