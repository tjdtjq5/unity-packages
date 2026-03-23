using Tjdtjq5.GameServer;

/// <summary>출석 보상 정의. 관리자가 등록.</summary>
[Config]
public class AttendanceReward
{
    [PrimaryKey] public string id;
    /// <summary>출석 일수 (1~31). 이 일수 도달 시 보상.</summary>
    public int day;
    /// <summary>보상 타입: currency, item.</summary>
    public string rewardType;
    /// <summary>보상 ID (currencyId 또는 itemId).</summary>
    public string rewardId;
    /// <summary>보상 수량.</summary>
    public int rewardAmount;
}
