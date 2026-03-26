using Tjdtjq5.SupaRun;

/// <summary>랭킹 시즌 정의. 관리자가 등록.</summary>
[Config]
public class RankingSeason
{
    [PrimaryKey] public string id;
    /// <summary>대상 리더보드 ID.</summary>
    public string boardId;
    public string name;
    /// <summary>시즌 시작 (Unix).</summary>
    public long startAt;
    /// <summary>시즌 종료 (Unix).</summary>
    public long endAt;
    /// <summary>현재 활성 시즌 여부.</summary>
    public bool isActive;
}
