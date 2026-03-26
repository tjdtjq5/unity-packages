using Tjdtjq5.SupaRun;

/// <summary>가챠 뽑기 이력.</summary>
[Table]
public class GachaLog
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string bannerId;
    public string itemId;
    public string grade;
    /// <summary>천장 카운터 (이 뽑기 시점의 pity 값).</summary>
    public int pityCounter;
    /// <summary>천장으로 확정된 뽑기인지.</summary>
    public bool wasPity;
    [CreatedAt] public long createdAt;
}
