using Tjdtjq5.GameServer;

/// <summary>상점 상품 정의. 관리자가 등록.</summary>
[Config]
public class ShopProduct
{
    [PrimaryKey] public string id;
    public string name;
    public string description;
    /// <summary>가격 재화 종류 (gold, gem 등).</summary>
    public string currencyId;
    /// <summary>가격.</summary>
    public int price;
    /// <summary>구매 시 지급할 아이템 ID. 없으면 재화만 차감.</summary>
    public string rewardItemId;
    /// <summary>구매 시 지급 수량.</summary>
    public int rewardAmount;
    /// <summary>인당 구매 제한. 0이면 무제한.</summary>
    public int maxPurchase;
    /// <summary>판매 시작 시간 (Unix). 0이면 항상.</summary>
    public long saleStart;
    /// <summary>판매 종료 시간 (Unix). 0이면 항상.</summary>
    public long saleEnd;
    public bool isActive;
}
