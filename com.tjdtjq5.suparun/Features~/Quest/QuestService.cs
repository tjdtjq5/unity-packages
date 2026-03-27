using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tjdtjq5.SupaRun;

/// <summary>퀘스트 서비스. 진행도 추적/완료/보상 즉시 지급.</summary>
[Service]
public class QuestService
{
    readonly IGameDB _db;
    readonly CurrencyService _currency;
    readonly InventoryService _inventory;

    public QuestService(IGameDB db, CurrencyService currency, InventoryService inventory)
    {
        _db = db;
        _currency = currency;
        _inventory = inventory;
    }

    /// <summary>퀘스트 목록 + 내 진행도.</summary>
    [API]
    public async Task<List<QuestWithProgress>> GetQuests(string playerId, string type = null)
    {
        var defs = await _db.GetAll<QuestDef>();
        var playerProgress = await _db.Query<QuestProgress>(new QueryOptions()
            .Eq("playerId", playerId));

        var result = new List<QuestWithProgress>();
        foreach (var def in defs.Where(d => d.isActive && (type == null || d.type == type)).OrderBy(d => d.sortOrder))
        {
            var period = GetCurrentPeriod(def.type);
            var progress = playerProgress.Find(p => p.questId == def.id && p.period == period);
            result.Add(new QuestWithProgress { quest = def, progress = progress });
        }
        return result;
    }

    /// <summary>행동 보고. 해당 actionKey의 퀘스트들 진행도 증가.</summary>
    [API]
    public async Task<List<QuestProgress>> ReportAction(string playerId, string actionKey, int count = 1)
    {
        if (count <= 0) throw new ArgumentException("count는 1 이상이어야 합니다.");

        var defs = await _db.GetAll<QuestDef>();
        var targets = defs.Where(d => d.isActive && d.actionKey == actionKey).ToList();
        if (targets.Count == 0) return new List<QuestProgress>();

        var updated = new List<QuestProgress>();

        foreach (var def in targets)
        {
            var period = GetCurrentPeriod(def.type);
            var id = $"{playerId}_{def.id}_{period}";
            var progress = await _db.Get<QuestProgress>(id);

            if (progress == null)
            {
                progress = new QuestProgress
                {
                    id = id,
                    playerId = playerId,
                    questId = def.id,
                    period = period
                };
            }

            if (progress.completed) continue;

            progress.currentCount = Math.Min(progress.currentCount + count, def.targetCount);
            if (progress.currentCount >= def.targetCount)
                progress.completed = true;

            await _db.Save(progress);
            updated.Add(progress);
        }

        return updated;
    }

    /// <summary>퀘스트 보상 즉시 수령.</summary>
    [API]
    public async Task<QuestProgress> ClaimReward(string playerId, string questId)
    {
        var def = await _db.Get<QuestDef>(questId);
        if (def == null)
            throw new InvalidOperationException("퀘스트를 찾을 수 없습니다.");

        var period = GetCurrentPeriod(def.type);
        var id = $"{playerId}_{questId}_{period}";
        var progress = await _db.Get<QuestProgress>(id);

        if (progress == null || !progress.completed)
            throw new InvalidOperationException("퀘스트가 완료되지 않았습니다.");
        if (progress.claimed)
            throw new InvalidOperationException("이미 보상을 수령했습니다.");

        // 보상 즉시 지급
        await GrantRewards(playerId, def.rewards, $"quest:{questId}");

        progress.claimed = true;
        await _db.Save(progress);
        return progress;
    }

    /// <summary>완료 + 미수령 퀘스트 수.</summary>
    [API]
    public async Task<int> GetClaimableCount(string playerId, string type = null)
    {
        var quests = await GetQuests(playerId, type);
        return quests.Count(q => q.progress != null && q.progress.completed && !q.progress.claimed);
    }

    // ── 보상 지급 ──

    async Task GrantRewards(string playerId, string rewardsJson, string reason)
    {
        if (string.IsNullOrEmpty(rewardsJson)) return;

        var rewards = JsonConvert.DeserializeObject<List<RewardEntry>>(rewardsJson);
        if (rewards == null) return;

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

    // ── 기간 계산 ──

    static string GetCurrentPeriod(string type)
    {
        var now = DateTime.UtcNow;
        return type switch
        {
            "daily" => now.ToString("yyyy-MM-dd"),
            "weekly" => $"{ISOWeek.GetYear(now)}-W{ISOWeek.GetWeekOfYear(now):D2}",
            _ => "permanent"
        };
    }

    // ── DTO ──

    class RewardEntry
    {
        public string type;
        public string id;
        public long amount;
    }
}

/// <summary>퀘스트 + 진행도 조합. JSON 직렬화 가능.</summary>
public class QuestWithProgress
{
    public QuestDef quest;
    public QuestProgress progress;
}
