using Tjdtjq5.SupaRun;

/// <summary>가챠 확률 테이블 항목. bannerId별 풀.</summary>
[Config]
public class GachaPool
{
    [PrimaryKey] public string id;
    [Index] public string bannerId;
    /// <summary>보상 아이템 ID.</summary>
    public string itemId;
    /// <summary>등급 (예: "N", "R", "SR", "SSR").</summary>
    public string grade;
    /// <summary>가중치. 확률 = weight / 전체 weight 합.</summary>
    public int weight;
}
