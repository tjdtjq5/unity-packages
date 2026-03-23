using Tjdtjq5.GameServer;

/// <summary>가챠 배너. 관리자가 등록.</summary>
[Config]
public class GachaBanner
{
    [PrimaryKey] public string id;
    public string name;
    public string description;
    /// <summary>1회 비용 재화 ID.</summary>
    public string costCurrencyId;
    /// <summary>1회 비용.</summary>
    public int costAmount;
    /// <summary>10연차 비용. 0이면 costAmount * 10.</summary>
    public int cost10Amount;
    /// <summary>천장 횟수. 0이면 천장 없음.</summary>
    public int pityCount;
    /// <summary>천장 시 확정 등급 (예: "SSR").</summary>
    public string pityGrade;
    /// <summary>판매 시작 (Unix). 0이면 항상.</summary>
    public long startAt;
    /// <summary>판매 종료 (Unix). 0이면 항상.</summary>
    public long endAt;
    public bool isActive;
}
