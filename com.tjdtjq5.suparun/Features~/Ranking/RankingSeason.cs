using Tjdtjq5.SupaRun;

/// <summary>
/// 랭킹 시즌 정의. startAt~endAt으로 활성 자동 판단.
/// rewards JSON: [{"rankMin":1,"rankMax":1,"rewards":[{"type":"currency","id":"diamond","amount":1000}]}]
/// </summary>
[Config]
public class RankingSeason
{
    [PrimaryKey] public string id;
    public string boardId;
    public string name;
    public long startAt;
    public long endAt;
    /// <summary>0=전체 랭킹, N=N명 그룹 랭킹.</summary>
    public int groupSize;
    /// <summary>"highest"(기본), "accumulate", "latest", "lowest".</summary>
    public string scorePolicy;
    /// <summary>순위별 시즌 보상. JSON 배열.</summary>
    public string rewards;
}
