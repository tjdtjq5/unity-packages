using Tjdtjq5.SupaRun;

/// <summary>
/// 퀘스트 정의. 관리자가 등록.
/// rewards JSON 예시: [{"type":"currency","id":"gold","amount":100},{"type":"item","id":"potion_hp","amount":3}]
/// </summary>
[Config]
public class QuestDef
{
    [PrimaryKey] public string id;
    [NotNull] public string title;
    public string description;
    /// <summary>퀘스트 타입: daily, weekly, achievement.</summary>
    public string type;
    /// <summary>추적할 행동 키 (예: "kill_monster", "clear_stage").</summary>
    public string actionKey;
    /// <summary>완료 목표 수치.</summary>
    public int targetCount;
    /// <summary>완료 보상. JSON 배열.</summary>
    public string rewards;
    /// <summary>정렬 순서.</summary>
    public int sortOrder;
    public bool isActive;
}
