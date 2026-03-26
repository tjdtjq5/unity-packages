using Tjdtjq5.SupaRun;

/// <summary>퀘스트 정의. 관리자가 등록.</summary>
[Config]
public class QuestDef
{
    [PrimaryKey] public string id;
    public string title;
    public string description;
    /// <summary>퀘스트 타입: daily, weekly, achievement.</summary>
    public string type;
    /// <summary>추적할 행동 키 (예: "kill_monster", "clear_stage").</summary>
    public string actionKey;
    /// <summary>완료 목표 수치.</summary>
    public int targetCount;
    /// <summary>보상 타입: currency, item.</summary>
    public string rewardType;
    /// <summary>보상 ID.</summary>
    public string rewardId;
    /// <summary>보상 수량.</summary>
    public int rewardAmount;
    /// <summary>정렬 순서.</summary>
    public int sortOrder;
    public bool isActive;
}
