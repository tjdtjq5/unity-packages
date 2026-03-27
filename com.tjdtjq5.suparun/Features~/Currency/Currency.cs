using Tjdtjq5.SupaRun;

/// <summary>유저 재화 보유량. playerId + currencyId 조합으로 관리.</summary>
[Table]
public class Currency
{
    [PrimaryKey] public string id;          // "{playerId}_{currencyId}"
    [Index] public string playerId;
    [Index] public string currencyId;
    public long amount;
    public long lastRechargeAt;             // 충전형 전용 (Unix 초). 일반 재화는 0.
    [UpdatedAt] public long updatedAt;
}
