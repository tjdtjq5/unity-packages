using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tjdtjq5.SupaRun;

/// <summary>상점 서비스. 상품 조회/구매(다중 보상)/이력.</summary>
[Service]
public class ShopService
{
    readonly IGameDB _db;
    readonly CurrencyService _currency;
    readonly InventoryService _inventory;

    public ShopService(IGameDB db, CurrencyService currency, InventoryService inventory)
    {
        _db = db;
        _currency = currency;
        _inventory = inventory;
    }

    /// <summary>판매 중인 상품 목록.</summary>
    [API]
    public async Task<List<ShopProduct>> GetProducts()
    {
        var all = await _db.GetAll<ShopProduct>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return all.Where(p => p.isActive &&
            (p.saleStart == 0 || p.saleStart <= now) &&
            (p.saleEnd == 0 || p.saleEnd >= now))
            .ToList();
    }

    /// <summary>
    /// 상품 구매.
    /// 1. 재화 차감
    /// 2. rewards 파싱 → 각 보상 지급 (실패 시 재화 환불)
    /// 3. 이력 기록
    /// </summary>
    [API]
    public async Task<ShopPurchaseLog> Purchase(string playerId, string productId)
    {
        var product = await _db.Get<ShopProduct>(productId);
        if (product == null || !product.isActive)
            throw new InvalidOperationException("존재하지 않거나 판매 중이 아닌 상품입니다.");

        // 판매 기간 확인
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (product.saleStart > 0 && product.saleStart > now)
            throw new InvalidOperationException("아직 판매 시작 전입니다.");
        if (product.saleEnd > 0 && product.saleEnd < now)
            throw new InvalidOperationException("판매가 종료되었습니다.");

        // 구매 제한 확인
        if (product.maxPurchase > 0)
        {
            var count = await GetPurchaseCount(playerId, productId);
            if (count >= product.maxPurchase)
                throw new InvalidOperationException($"구매 제한 초과 ({product.maxPurchase}회)");
        }

        // 1. 재화 차감
        await _currency.Subtract(playerId, product.currencyId, product.price, $"shop:{productId}");

        // 2. 보상 지급
        try
        {
            await GrantRewards(playerId, product, productId);
        }
        catch (Exception ex)
        {
            // 보상 지급 실패 → 재화 환불
            await _currency.Add(playerId, product.currencyId, product.price, $"shop_refund:{productId}");
            throw new InvalidOperationException($"보상 지급 실패, 재화 환불됨: {ex.Message}");
        }

        // 3. 이력 기록
        var log = new ShopPurchaseLog
        {
            id = Guid.NewGuid().ToString(),
            playerId = playerId,
            productId = productId,
            price = product.price,
            currencyId = product.currencyId
        };
        await _db.Save(log);

        return log;
    }

    /// <summary>구매 이력 조회.</summary>
    [API]
    public async Task<List<ShopPurchaseLog>> GetPurchaseHistory(string playerId)
    {
        return await _db.Query<ShopPurchaseLog>(new QueryOptions()
            .Eq("playerId", playerId).OrderByDesc("purchasedAt"));
    }

    /// <summary>특정 상품 구매 횟수.</summary>
    [API]
    public async Task<int> GetPurchaseCount(string playerId, string productId)
    {
        return await _db.Count<ShopPurchaseLog>(new QueryOptions()
            .Eq("playerId", playerId).Eq("productId", productId));
    }

    // ── 보상 지급 ──

    async Task GrantRewards(string playerId, ShopProduct product, string productId)
    {
        if (string.IsNullOrEmpty(product.rewards)) return;

        var rewards = JsonConvert.DeserializeObject<List<RewardEntry>>(product.rewards);
        if (rewards == null || rewards.Count == 0) return;

        var reason = $"shop:{productId}";

        foreach (var reward in rewards)
        {
            switch (reward.type)
            {
                case "currency":
                    await _currency.Add(playerId, reward.id, reward.amount, reason);
                    break;
                case "item":
                    await _inventory.AddItem(playerId, reward.id, (int)reward.amount, reason);
                    break;
            }
        }
    }

    class RewardEntry
    {
        public string type;    // "currency" | "item"
        public string id;
        public long amount;
    }
}
