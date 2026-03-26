using Tjdtjq5.SupaRun;

/// <summary>출석 기록. playerId당 하루 1개.</summary>
[Table]
public class AttendanceRecord
{
    [PrimaryKey] public string id;
    [Index] public string playerId;
    /// <summary>출석일 (yyyy-MM-dd 형식).</summary>
    [Index] public string date;
    /// <summary>해당 월 누적 출석 일수.</summary>
    public int dayOfMonth;
    /// <summary>연속 출석 일수.</summary>
    public int streak;
    [CreatedAt] public long checkedAt;
}
