using Tjdtjq5.SupaRun;

/// <summary>플레이어별 배너 천장 카운터.</summary>
[Table]
public class GachaPity
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    [Index] public string bannerId;
    /// <summary>현재 천장 카운터.</summary>
    public int counter;
    [UpdatedAt] public long updatedAt;
}
