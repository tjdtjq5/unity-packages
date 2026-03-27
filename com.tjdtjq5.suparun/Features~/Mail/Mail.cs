using Tjdtjq5.SupaRun;

/// <summary>
/// 우편. 시스템/관리자가 발송, 플레이어가 수신.
/// rewards JSON 예시: [{"type":"currency","id":"gold","amount":1000},{"type":"item","id":"potion_hp","amount":5}]
/// </summary>
[Table]
public class Mail
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [NotNull] public string title;
    public string body;
    /// <summary>첨부 보상. JSON 배열. null이면 보상 없음.</summary>
    public string rewards;
    /// <summary>수령 여부.</summary>
    public bool claimed;
    /// <summary>읽음 여부.</summary>
    public bool isRead;
    /// <summary>만료 시간 (Unix 초). 0이면 무기한.</summary>
    public long expiresAt;
    [CreatedAt] public long createdAt;
}
