using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

/// <summary>가챠 서비스. 뽑기/천장/이력.</summary>
[Service]
public class GachaService
{
    readonly IGameDB _db;
    readonly CurrencyService _currency;
    readonly InventoryService _inventory;

    public GachaService(IGameDB db, CurrencyService currency, InventoryService inventory)
    {
        _db = db;
        _currency = currency;
        _inventory = inventory;
    }

    /// <summary>활성 배너 목록.</summary>
    [API]
    public async Task<List<GachaBanner>> GetBanners()
    {
        var all = await _db.GetAll<GachaBanner>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return all.Where(b => b.isActive &&
            (b.startAt == 0 || b.startAt <= now) &&
            (b.endAt == 0 || b.endAt >= now))
            .ToList();
    }

    /// <summary>1회 뽑기.</summary>
    [API]
    public async Task<GachaLog> Pull(string playerId, string bannerId)
    {
        var results = await PullMulti(playerId, bannerId, 1);
        return results[0];
    }

    /// <summary>N연차 뽑기.</summary>
    [API]
    public async Task<List<GachaLog>> Pull10(string playerId, string bannerId)
    {
        return await PullMulti(playerId, bannerId, 10);
    }

    /// <summary>N회 뽑기 내부 구현.</summary>
    async Task<List<GachaLog>> PullMulti(string playerId, string bannerId, int count)
    {
        // 배너 확인
        var banner = await _db.Get<GachaBanner>(bannerId);
        if (banner == null || !banner.isActive)
            throw new InvalidOperationException("존재하지 않거나 비활성 배너입니다.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (banner.startAt > 0 && banner.startAt > now)
            throw new InvalidOperationException("아직 시작 전입니다.");
        if (banner.endAt > 0 && banner.endAt < now)
            throw new InvalidOperationException("종료된 배너입니다.");

        // 비용 계산 + 차감
        var totalCost = count == 10 && banner.cost10Amount > 0
            ? banner.cost10Amount
            : banner.costAmount * count;
        await _currency.Subtract(playerId, banner.costCurrencyId, totalCost, $"gacha:{bannerId}x{count}");

        // 풀 로드
        var allPool = await _db.GetAll<GachaPool>();
        var pool = allPool.Where(p => p.bannerId == bannerId).ToList();
        if (pool.Count == 0)
            throw new InvalidOperationException("가챠 풀이 비어있습니다.");

        var totalWeight = pool.Sum(p => p.weight);

        // 천장 로드
        var pity = await GetOrCreatePity(playerId, bannerId);
        var rng = new Random();
        var results = new List<GachaLog>();

        for (var i = 0; i < count; i++)
        {
            pity.counter++;
            var wasPity = false;
            GachaPool picked;

            // 천장 체크
            if (banner.pityCount > 0 && pity.counter >= banner.pityCount &&
                !string.IsNullOrEmpty(banner.pityGrade))
            {
                var pityPool = pool.Where(p => p.grade == banner.pityGrade).ToList();
                picked = pityPool.Count > 0
                    ? pityPool[rng.Next(pityPool.Count)]
                    : DrawWeighted(pool, totalWeight, rng);
                pity.counter = 0;
                wasPity = true;
            }
            else
            {
                picked = DrawWeighted(pool, totalWeight, rng);

                // 최고 등급 뽑으면 천장 리셋
                if (!string.IsNullOrEmpty(banner.pityGrade) && picked.grade == banner.pityGrade)
                    pity.counter = 0;
            }

            // 아이템 지급
            await _inventory.AddItem(playerId, picked.itemId, 1, $"gacha:{bannerId}");

            // 이력 기록
            var log = new GachaLog
            {
                id = Guid.NewGuid().ToString(),
                playerId = playerId,
                bannerId = bannerId,
                itemId = picked.itemId,
                grade = picked.grade,
                pityCounter = pity.counter,
                wasPity = wasPity
            };
            await _db.Save(log);
            results.Add(log);
        }

        // 천장 저장
        await _db.Save(pity);

        return results;
    }

    /// <summary>내 천장 카운터 조회.</summary>
    [API]
    public async Task<int> GetPityCounter(string playerId, string bannerId)
    {
        var pity = await FindPity(playerId, bannerId);
        return pity?.counter ?? 0;
    }

    /// <summary>뽑기 이력. 최신순.</summary>
    [API]
    public async Task<List<GachaLog>> GetHistory(string playerId, string bannerId, int count = 50)
    {
        return await _db.Query<GachaLog>(new QueryOptions()
            .Eq("playerId", playerId).Eq("bannerId", bannerId)
            .OrderByDesc("createdAt").SetLimit(count));
    }

    /// <summary>배너별 확률 테이블 조회.</summary>
    [API]
    public async Task<List<GachaPool>> GetPool(string bannerId)
    {
        var all = await _db.GetAll<GachaPool>();
        return all.Where(p => p.bannerId == bannerId).ToList();
    }

    // ── 내부 ──

    static GachaPool DrawWeighted(List<GachaPool> pool, int totalWeight, Random rng)
    {
        var roll = rng.Next(totalWeight);
        var cumulative = 0;
        foreach (var item in pool)
        {
            cumulative += item.weight;
            if (roll < cumulative)
                return item;
        }
        return pool[pool.Count - 1];
    }

    async Task<GachaPity> GetOrCreatePity(string playerId, string bannerId)
    {
        var existing = await FindPity(playerId, bannerId);
        if (existing != null) return existing;

        var pity = new GachaPity
        {
            id = $"{playerId}_{bannerId}",
            playerId = playerId,
            bannerId = bannerId,
            counter = 0
        };
        await _db.Save(pity);
        return pity;
    }

    async Task<GachaPity> FindPity(string playerId, string bannerId)
    {
        var list = await _db.Query<GachaPity>(new QueryOptions()
            .Eq("playerId", playerId).Eq("bannerId", bannerId).SetLimit(1));
        return list.Count > 0 ? list[0] : null;
    }
}
