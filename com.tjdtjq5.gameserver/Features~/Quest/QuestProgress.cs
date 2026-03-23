using Tjdtjq5.GameServer;

/// <summary>퀘스트 진행 상태. playerId + questId 조합.</summary>
[Table]
public class QuestProgress
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string questId;
    /// <summary>현재 진행 수치.</summary>
    public int currentCount;
    /// <summary>완료 여부.</summary>
    public bool completed;
    /// <summary>보상 수령 여부.</summary>
    public bool claimed;
    /// <summary>리셋 기준일 (daily: yyyy-MM-dd, weekly: yyyy-Www).</summary>
    public string period;
    [CreatedAt] public long createdAt;
    [UpdatedAt] public long updatedAt;
}
