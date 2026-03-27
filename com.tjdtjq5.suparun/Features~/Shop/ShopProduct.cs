using Tjdtjq5.SupaRun;

/// <summary>
/// 상점 상품 정의. 관리자가 등록.
/// rewards JSON 예시: [{"type":"currency","id":"gold","amount":10000},{"type":"item","id":"potion_hp","amount":5}]
/// </summary>
[Config]
public class ShopProduct
{
    [PrimaryKey] public string id;
    [NotNull] public string name;
    public string description;
    /// <summary>가격 재화 종류 (gold, diamond 등).</summary>
    public string currencyId;
    /// <summary>가격.</summary>
    public long price;
    /// <summary>보상 목록. JSON 배열. [{"type":"currency|item","id":"...","amount":N}]</summary>
    public string rewards;
    /// <summary>인당 구매 제한. 0이면 무제한.</summary>
    public int maxPurchase;
    /// <summary>판매 시작 시간 (Unix 초). 0이면 항상.</summary>
    public long saleStart;
    /// <summary>판매 종료 시간 (Unix 초). 0이면 항상.</summary>
    public long saleEnd;
    public bool isActive;
}
