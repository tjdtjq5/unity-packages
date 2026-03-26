using Tjdtjq5.SupaRun;

/// <summary>재화 변동 이력. 지급/차감 시마다 기록.</summary>
[Table]
public class CurrencyLog
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    public string currencyId;
    public int change;
    public int balanceAfter;
    [NotNull] public string reason;
    [CreatedAt] public long createdAt;
}
