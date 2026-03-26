using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

/// <summary>재화 서비스. 지급/차감/잔액 조회/이력.</summary>
[Service]
public class CurrencyService
{
    readonly IGameDB _db;
    public CurrencyService(IGameDB db) => _db = db;

    /// <summary>잔액 조회. 없으면 0으로 생성.</summary>
    [API]
    public async Task<CurrencyBalance> GetBalance(string playerId, string currencyId)
    {
        var list = await _db.Query<CurrencyBalance>(new QueryOptions()
            .Eq("playerId", playerId).Eq("currencyId", currencyId).SetLimit(1));
        var balance = list.Count > 0 ? list[0] : null;

        if (balance == null)
        {
            balance = new CurrencyBalance
            {
                id = $"{playerId}_{currencyId}",
                playerId = playerId,
                currencyId = currencyId,
                amount = 0
            };
            await _db.Save(balance);
        }

        return balance;
    }

    /// <summary>재화 지급. amount > 0 필수.</summary>
    [API]
    public async Task<CurrencyBalance> Add(string playerId, string currencyId, int amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("지급 수량은 0보다 커야 합니다.");

        var balance = await GetBalance(playerId, currencyId);
        balance.amount += amount;
        await _db.Save(balance);

        await _db.Save(new CurrencyLog
        {
            id = Guid.NewGuid().ToString(),
            playerId = playerId,
            currencyId = currencyId,
            change = amount,
            balanceAfter = balance.amount,
            reason = reason
        });

        return balance;
    }

    /// <summary>재화 차감. 잔액 부족 시 예외.</summary>
    [API]
    public async Task<CurrencyBalance> Subtract(string playerId, string currencyId, int amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("차감 수량은 0보다 커야 합니다.");

        var balance = await GetBalance(playerId, currencyId);

        if (balance.amount < amount)
            throw new InvalidOperationException($"잔액 부족: 보유 {balance.amount}, 필요 {amount}");

        balance.amount -= amount;
        await _db.Save(balance);

        await _db.Save(new CurrencyLog
        {
            id = Guid.NewGuid().ToString(),
            playerId = playerId,
            currencyId = currencyId,
            change = -amount,
            balanceAfter = balance.amount,
            reason = reason
        });

        return balance;
    }

    /// <summary>변동 이력 조회. 최신순.</summary>
    [API]
    public async Task<List<CurrencyLog>> GetHistory(string playerId, string currencyId)
    {
        return await _db.Query<CurrencyLog>(new QueryOptions()
            .Eq("playerId", playerId).Eq("currencyId", currencyId)
            .OrderByDesc("createdAt"));
    }
}
