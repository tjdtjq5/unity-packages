using Tjdtjq5.SupaRun;

/// <summary>재화 종류 정의. 서버 Config에서 관리.</summary>
[Config]
public class CurrencyDef
{
    [PrimaryKey] public string id;          // "gold", "diamond", "stamina"
    [NotNull] public string name;           // "골드", "다이아", "스태미나"
    public int maxAmount;                   // 0 = 무제한, 10 = 상한
    public int rechargeSeconds;             // 0 = 일반 재화, 300 = 5분마다 충전
    public int rechargeAmount;              // 충전 시 회복량 (기본 1)
}
