using Tjdtjq5.SupaRun;

/// <summary>재화 잔액. playerId + currencyId 조합으로 관리.</summary>
[Table]
public class CurrencyBalance
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string currencyId;
    public int amount;
    [UpdatedAt] public long updatedAt;
}
