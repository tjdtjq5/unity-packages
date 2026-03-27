using Tjdtjq5.SupaRun;

/// <summary>
/// 출석 보상 정의. 관리자가 등록.
/// rewards JSON 예시: [{"type":"currency","id":"gold","amount":100},{"type":"item","id":"potion_hp","amount":3}]
/// </summary>
[Config("attendance")]
public class AttendanceReward
{
    [PrimaryKey] public string id;
    /// <summary>출석 일수 (1~31). 이 일수 도달 시 보상.</summary>
    public int day;
    /// <summary>보상 목록. JSON 배열.</summary>
    public string rewards;
}
