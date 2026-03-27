using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

/// <summary>재화 서비스. 지급/차감/잔액 조회/충전형 지원.</summary>
[Service]
public class CurrencyService
{
    readonly IGameDB _db;
    public CurrencyService(IGameDB db) => _db = db;

    /// <summary>잔액 조회. 없으면 0으로 생성. 충전형이면 자동 충전 적용.</summary>
    [API]
    public async Task<Currency> GetBalance(string playerId, string currencyId)
    {
        var id = $"{playerId}_{currencyId}";
        var currency = await _db.Get<Currency>(id);

        if (currency == null)
        {
            currency = new Currency
            {
                id = id,
                playerId = playerId,
                currencyId = currencyId,
                amount = 0,
                lastRechargeAt = 0
            };
            await _db.Save(currency);
        }

        // 충전형 재화 자동 충전
        await ApplyRecharge(currency);

        return currency;
    }

    /// <summary>재화 지급. amount > 0 필수. maxAmount 적용.</summary>
    [API]
    public async Task<Currency> Add(string playerId, string currencyId, long amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("지급 수량은 0보다 커야 합니다.");

        Currency result = null;
        await _db.Transaction(async tx =>
        {
            var currency = await GetBalanceInternal(tx, playerId, currencyId);
            await ApplyRechargeWithTx(tx, currency);

            currency.amount += amount;

            // maxAmount 적용
            var def = await GetDef(tx, currencyId);
            if (def != null && def.maxAmount > 0 && currency.amount > def.maxAmount)
                currency.amount = def.maxAmount;

            await tx.Save(currency);

            await tx.Save(new CurrencyLog
            {
                id = Guid.NewGuid().ToString(),
                playerId = playerId,
                currencyId = currencyId,
                change = amount,
                balanceAfter = currency.amount,
                reason = reason
            });

            result = currency;
        });

        return result;
    }

    /// <summary>재화 차감. 잔액 부족 시 예외.</summary>
    [API]
    public async Task<Currency> Subtract(string playerId, string currencyId, long amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("차감 수량은 0보다 커야 합니다.");

        Currency result = null;
        await _db.Transaction(async tx =>
        {
            var currency = await GetBalanceInternal(tx, playerId, currencyId);
            await ApplyRechargeWithTx(tx, currency);

            if (currency.amount < amount)
                throw new InvalidOperationException($"잔액 부족: 보유 {currency.amount}, 필요 {amount}");

            currency.amount -= amount;
            await tx.Save(currency);

            await tx.Save(new CurrencyLog
            {
                id = Guid.NewGuid().ToString(),
                playerId = playerId,
                currencyId = currencyId,
                change = -amount,
                balanceAfter = currency.amount,
                reason = reason
            });

            result = currency;
        });

        return result;
    }

    /// <summary>변동 이력 조회. 최신순.</summary>
    [API]
    public async Task<List<CurrencyLog>> GetHistory(string playerId, string currencyId)
    {
        return await _db.Query<CurrencyLog>(new QueryOptions()
            .Eq("playerId", playerId).Eq("currencyId", currencyId)
            .OrderByDesc("createdAt"));
    }

    // ── 내부 헬퍼 ──

    async Task<Currency> GetBalanceInternal(IGameDB tx, string playerId, string currencyId)
    {
        var id = $"{playerId}_{currencyId}";
        var currency = await tx.Get<Currency>(id);

        if (currency == null)
        {
            currency = new Currency
            {
                id = id,
                playerId = playerId,
                currencyId = currencyId,
                amount = 0,
                lastRechargeAt = 0
            };
            await tx.Save(currency);
        }

        return currency;
    }

    async Task<CurrencyDef> GetDef(IGameDB db, string currencyId)
    {
        return await db.Get<CurrencyDef>(currencyId);
    }

    async Task ApplyRecharge(Currency currency)
    {
        var def = await _db.Get<CurrencyDef>(currency.currencyId);
        if (def == null || def.rechargeSeconds <= 0 || currency.lastRechargeAt <= 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var elapsed = now - currency.lastRechargeAt;
        var rechargeAmt = def.rechargeAmount > 0 ? def.rechargeAmount : 1;
        var charges = (elapsed / def.rechargeSeconds) * rechargeAmt;

        if (charges > 0)
        {
            currency.amount = def.maxAmount > 0
                ? Math.Min(currency.amount + charges, def.maxAmount)
                : currency.amount + charges;
            currency.lastRechargeAt = now;
            await _db.Save(currency);
        }
    }

    async Task ApplyRechargeWithTx(IGameDB tx, Currency currency)
    {
        var def = await tx.Get<CurrencyDef>(currency.currencyId);
        if (def == null || def.rechargeSeconds <= 0 || currency.lastRechargeAt <= 0) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var elapsed = now - currency.lastRechargeAt;
        var rechargeAmt = def.rechargeAmount > 0 ? def.rechargeAmount : 1;
        var charges = (elapsed / def.rechargeSeconds) * rechargeAmt;

        if (charges > 0)
        {
            currency.amount = def.maxAmount > 0
                ? Math.Min(currency.amount + charges, def.maxAmount)
                : currency.amount + charges;
            currency.lastRechargeAt = now;
            await tx.Save(currency);
        }
    }
}
