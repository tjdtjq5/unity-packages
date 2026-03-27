using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

/// <summary>인벤토리 서비스. 스택형(소모품) + 개별형(장비) 지원.</summary>
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

    /// <summary>스택형 아이템 조회 (소모품). 없으면 null.</summary>
    [API]
    public async Task<InventoryItem> GetItem(string playerId, string itemDefId)
    {
        var id = $"{playerId}_{itemDefId}";
        return await _db.Get<InventoryItem>(id);
    }

    /// <summary>개별형 아이템 조회 (장비). UUID로 특정.</summary>
    [API]
    public async Task<InventoryItem> GetById(string inventoryId)
    {
        return await _db.Get<InventoryItem>(inventoryId);
    }

    /// <summary>
    /// 아이템 획득.
    /// 스택형: 기존 row에 amount 합산. maxStack 적용.
    /// 개별형: amount만큼 새 row 생성 (각각 UUID). metadata 적용.
    /// 반환: 스택형=해당 row, 개별형=마지막 생성된 row.
    /// </summary>
    [API]
    public async Task<InventoryItem> AddItem(string playerId, string itemDefId, int amount, string reason, string metadata = null)
    {
        if (amount <= 0)
            throw new ArgumentException("획득 수량은 0보다 커야 합니다.");

        InventoryItem result = null;

        await _db.Transaction(async tx =>
        {
            var def = await tx.Get<InventoryItemDef>(itemDefId);

            if (def != null && !def.stackable)
            {
                // 개별형 (장비): amount만큼 새 row 생성
                for (int i = 0; i < amount; i++)
                {
                    var item = new InventoryItem
                    {
                        id = Guid.NewGuid().ToString(),
                        playerId = playerId,
                        itemDefId = itemDefId,
                        amount = 1,
                        metadata = metadata
                    };
                    await tx.Save(item);
                    result = item;
                }
            }
            else
            {
                // 스택형 (소모품) 또는 def 없음 (기본 스택)
                var id = $"{playerId}_{itemDefId}";
                var item = await tx.Get<InventoryItem>(id);

                if (item == null)
                {
                    item = new InventoryItem
                    {
                        id = id,
                        playerId = playerId,
                        itemDefId = itemDefId,
                        amount = 0
                    };
                }

                item.amount += amount;

                // maxStack 적용
                if (def != null && def.maxStack > 0 && item.amount > def.maxStack)
                    item.amount = def.maxStack;

                await tx.Save(item);
                result = item;
            }

            await tx.Save(new InventoryLog
            {
                id = Guid.NewGuid().ToString(),
                playerId = playerId,
                itemDefId = itemDefId,
                change = amount,
                reason = reason
            });
        });

        return result;
    }

    /// <summary>
    /// 스택형 아이템 소모. 수량 부족 시 예외.
    /// 소모 후 amount가 0이면 row 삭제.
    /// </summary>
    [API]
    public async Task<InventoryItem> RemoveItem(string playerId, string itemDefId, int amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("소모 수량은 0보다 커야 합니다.");

        InventoryItem result = null;

        await _db.Transaction(async tx =>
        {
            var id = $"{playerId}_{itemDefId}";
            var item = await tx.Get<InventoryItem>(id);

            if (item == null || item.amount < amount)
                throw new InvalidOperationException($"수량 부족: 보유 {item?.amount ?? 0}, 필요 {amount}");

            item.amount -= amount;

            if (item.amount <= 0)
                await tx.Delete<InventoryItem>(id);
            else
                await tx.Save(item);

            await tx.Save(new InventoryLog
            {
                id = Guid.NewGuid().ToString(),
                playerId = playerId,
                itemDefId = itemDefId,
                change = -amount,
                reason = reason
            });

            result = item;
        });

        return result;
    }

    /// <summary>개별형 아이템 삭제 (장비). UUID로 특정.</summary>
    [API]
    public async Task RemoveById(string playerId, string inventoryId, string reason)
    {
        await _db.Transaction(async tx =>
        {
            var item = await tx.Get<InventoryItem>(inventoryId);
            if (item == null)
                throw new InvalidOperationException($"아이템 없음: {inventoryId}");
            if (item.playerId != playerId)
                throw new InvalidOperationException("본인의 아이템이 아닙니다.");

            await tx.Delete<InventoryItem>(inventoryId);

            await tx.Save(new InventoryLog
            {
                id = Guid.NewGuid().ToString(),
                playerId = playerId,
                itemDefId = item.itemDefId,
                change = -1,
                reason = reason
            });
        });
    }

    /// <summary>장비 메타데이터 수정 (강화, 내구도 등).</summary>
    [API]
    public async Task<InventoryItem> UpdateMetadata(string playerId, string inventoryId, string metadata)
    {
        var item = await _db.Get<InventoryItem>(inventoryId);
        if (item == null)
            throw new InvalidOperationException($"아이템 없음: {inventoryId}");
        if (item.playerId != playerId)
            throw new InvalidOperationException("본인의 아이템이 아닙니다.");

        item.metadata = metadata;
        await _db.Save(item);
        return item;
    }

    /// <summary>아이템 보유 여부 확인.</summary>
    [API]
    public async Task<bool> HasItem(string playerId, string itemDefId, int amount)
    {
        // 스택형: 단일 row 확인
        var stackItem = await GetItem(playerId, itemDefId);
        if (stackItem != null && stackItem.amount >= amount)
            return true;

        // 개별형: 해당 itemDefId의 row 개수 확인
        var items = await _db.Query<InventoryItem>(new QueryOptions()
            .Eq("playerId", playerId).Eq("itemDefId", itemDefId));
        return items.Count >= amount;
    }

    /// <summary>변동 이력 조회. 최신순.</summary>
    [API]
    public async Task<List<InventoryLog>> GetHistory(string playerId, string itemDefId)
    {
        return await _db.Query<InventoryLog>(new QueryOptions()
            .Eq("playerId", playerId).Eq("itemDefId", itemDefId)
            .OrderByDesc("createdAt"));
    }
}
