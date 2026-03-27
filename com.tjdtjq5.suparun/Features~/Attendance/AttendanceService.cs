using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tjdtjq5.SupaRun;

/// <summary>출석 체크 서비스. 일일 체크 + 연속 출석 + 보상 즉시 지급.</summary>
[Service]
public class AttendanceService
{
    readonly IGameDB _db;
    readonly CurrencyService _currency;
    readonly InventoryService _inventory;

    public AttendanceService(IGameDB db, CurrencyService currency, InventoryService inventory)
    {
        _db = db;
        _currency = currency;
        _inventory = inventory;
    }

    /// <summary>오늘 출석 체크. 이미 했으면 기존 기록 반환. 보상 즉시 지급.</summary>
    [API]
    public async Task<AttendanceRecord> CheckIn(string playerId)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // 이미 출석했는지 확인
        var existing = await _db.Get<AttendanceRecord>($"{playerId}_{today}");
        if (existing != null)
            return existing;

        // 어제 기록으로 연속 출석 계산
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var yesterdayRecord = await _db.Get<AttendanceRecord>($"{playerId}_{yesterday}");
        var streak = (yesterdayRecord?.streak ?? 0) + 1;

        // 이번 달 출석 일수
        var monthPrefix = DateTime.UtcNow.ToString("yyyy-MM");
        var monthRecords = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId).Like("date", $"{monthPrefix}%"));
        var monthCount = monthRecords.Count + 1;

        var record = new AttendanceRecord
        {
            id = $"{playerId}_{today}",
            playerId = playerId,
            date = today,
            dayOfMonth = monthCount,
            streak = streak
        };
        await _db.Save(record);

        // 보상 즉시 지급
        await GrantDayReward(playerId, monthCount);

        return record;
    }

    /// <summary>이번 달 출석 기록.</summary>
    [API]
    public async Task<List<AttendanceRecord>> GetMonthRecords(string playerId)
    {
        var monthPrefix = DateTime.UtcNow.ToString("yyyy-MM");
        return await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId).Like("date", $"{monthPrefix}%")
            .OrderByAsc("date"));
    }

    /// <summary>오늘 출석 여부 + 연속 일수 + 월 누적.</summary>
    [API]
    public async Task<AttendanceRecord> GetStatus(string playerId)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return await _db.Get<AttendanceRecord>($"{playerId}_{today}");
    }

    /// <summary>출석 보상 목록 (Config).</summary>
    [API]
    public async Task<List<AttendanceReward>> GetRewards()
    {
        return await _db.GetAll<AttendanceReward>();
    }

    // ── 보상 지급 ──

    async Task GrantDayReward(string playerId, int dayOfMonth)
    {
        var rewards = await _db.GetAll<AttendanceReward>();
        var reward = rewards.Find(r => r.day == dayOfMonth);
        if (reward == null || string.IsNullOrEmpty(reward.rewards)) return;

        var entries = JsonConvert.DeserializeObject<List<RewardEntry>>(reward.rewards);
        if (entries == null) return;

        var reason = $"attendance:day{dayOfMonth}";
        foreach (var entry in entries)
        {
            switch (entry.type)
            {
                case "currency":
                    await _currency.Add(playerId, entry.id, entry.amount, reason);
                    break;
                case "item":
                    await _inventory.AddItem(playerId, entry.id, (int)entry.amount, reason);
                    break;
            }
        }
    }

    class RewardEntry
    {
        public string type;
        public string id;
        public long amount;
    }
}
