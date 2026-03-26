using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

/// <summary>인벤토리 서비스. 아이템 획득/소모/조회/이력.</summary>
[Service]
public class InventoryService
{
    readonly IGameDB _db;
    public InventoryService(IGameDB db) => _db = db;

    /// <summary>전체 보유 아이템 조회.</summary>
    [API]
    public async Task<List<InventoryItem>> GetItems(string playerId)
    {
        return await _db.Query<InventoryItem>(new QueryOptions().Eq("playerId", playerId));
    }

    /// <summary>특정 아이템 조회. 없으면 null.</summary>
    [API]
    public async Task<InventoryItem> GetItem(string playerId, string itemId)
    {
        var list = await _db.Query<InventoryItem>(new QueryOptions()
            .Eq("playerId", playerId).Eq("itemId", itemId).SetLimit(1));
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>아이템 획득. 기존 보유 시 수량 합산.</summary>
    [API]
    public async Task<InventoryItem> AddItem(string playerId, string itemId, int amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("획득 수량은 0보다 커야 합니다.");

        var item = await GetItem(playerId, itemId);

        if (item == null)
        {
            item = new InventoryItem
            {
                id = $"{playerId}_{itemId}",
                playerId = playerId,
                itemId = itemId,
                amount = amount
            };
        }
        else
        {
            item.amount += amount;
        }

        await _db.Save(item);

        await _db.Save(new InventoryLog
        {
            id = Guid.NewGuid().ToString(),
            playerId = playerId,
            itemId = itemId,
            change = amount,
            amountAfter = item.amount,
            reason = reason
        });

        return item;
    }

    /// <summary>아이템 소모. 수량 부족 시 예외.</summary>
    [API]
    public async Task<InventoryItem> RemoveItem(string playerId, string itemId, int amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("소모 수량은 0보다 커야 합니다.");

        var item = await GetItem(playerId, itemId);

        if (item == null || item.amount < amount)
            throw new InvalidOperationException($"수량 부족: 보유 {item?.amount ?? 0}, 필요 {amount}");

        item.amount -= amount;
        await _db.Save(item);

        await _db.Save(new InventoryLog
        {
            id = Guid.NewGuid().ToString(),
            playerId = playerId,
            itemId = itemId,
            change = -amount,
            amountAfter = item.amount,
            reason = reason
        });

        return item;
    }

    /// <summary>아이템 보유 여부 확인.</summary>
    [API]
    public async Task<bool> HasItem(string playerId, string itemId, int amount)
    {
        var item = await GetItem(playerId, itemId);
        return item != null && item.amount >= amount;
    }

    /// <summary>변동 이력 조회. 최신순.</summary>
    [API]
    public async Task<List<InventoryLog>> GetHistory(string playerId, string itemId)
    {
        return await _db.Query<InventoryLog>(new QueryOptions()
            .Eq("playerId", playerId).Eq("itemId", itemId)
            .OrderByDesc("createdAt"));
    }
}
