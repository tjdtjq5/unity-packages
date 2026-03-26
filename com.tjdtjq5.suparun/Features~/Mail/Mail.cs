using Tjdtjq5.SupaRun;

/// <summary>우편. 시스템/관리자가 발송, 플레이어가 수신.</summary>
[Table]
public class Mail
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    public string title;
    public string body;
    /// <summary>첨부 보상 타입: none, currency, item.</summary>
    public string rewardType;
    /// <summary>보상 ID (currencyId 또는 itemId).</summary>
    public string rewardId;
    /// <summary>보상 수량.</summary>
    public int rewardAmount;
    /// <summary>수령 여부.</summary>
    public bool claimed;
    /// <summary>읽음 여부.</summary>
    public bool isRead;
    /// <summary>만료 시간 (Unix). 0이면 무기한.</summary>
    public long expiresAt;
    [CreatedAt] public long createdAt;
}
