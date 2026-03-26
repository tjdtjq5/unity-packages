using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

/// <summary>퀘스트 서비스. 진행도 추적/완료/보상 수령.</summary>
[Service]
public class QuestService
{
    readonly IGameDB _db;
    readonly MailService _mail;

    public QuestService(IGameDB db, MailService mail)
    {
        _db = db;
        _mail = mail;
    }

    /// <summary>퀘스트 목록 + 내 진행도.</summary>
    [API]
    public async Task<List<(QuestDef quest, QuestProgress progress)>> GetQuests(string playerId, string type = null)
    {
        var defs = await _db.GetAll<QuestDef>(); // [Config]
        var playerProgress = await _db.Query<QuestProgress>(new QueryOptions()
            .Eq("playerId", playerId));
        var period = GetCurrentPeriod(type);

        var result = new List<(QuestDef, QuestProgress)>();
        foreach (var def in defs.Where(d => d.isActive && (type == null || d.type == type)).OrderBy(d => d.sortOrder))
        {
            var progress = playerProgress.Find(p => p.questId == def.id && p.period == period);
            result.Add((def, progress));
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
            var progressList = await _db.Query<QuestProgress>(new QueryOptions()
                .Eq("playerId", playerId).Eq("questId", def.id).SetLimit(1));
            var progress = progressList.Find(p => p.period == period);

            if (progress == null)
            {
                progress = new QuestProgress
                {
                    id = $"{playerId}_{def.id}_{period}",
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

    /// <summary>퀘스트 보상 수령. 우편으로 발송.</summary>
    [API]
    public async Task<QuestProgress> ClaimReward(string playerId, string questId)
    {
        var def = await _db.Get<QuestDef>(questId);
        if (def == null)
            throw new InvalidOperationException("퀘스트를 찾을 수 없습니다.");

        var period = GetCurrentPeriod(def.type);
        var progressList = await _db.Query<QuestProgress>(new QueryOptions()
            .Eq("playerId", playerId).Eq("questId", questId).SetLimit(1));
        var progress = progressList.Find(p => p.period == period);

        if (progress == null || !progress.completed)
            throw new InvalidOperationException("퀘스트가 완료되지 않았습니다.");
        if (progress.claimed)
            throw new InvalidOperationException("이미 보상을 수령했습니다.");

        // 우편으로 보상 발송
        await _mail.SendMail(
            playerId,
            $"퀘스트 완료: {def.title}",
            $"'{def.title}' 완료 보상입니다!",
            def.rewardType,
            def.rewardId,
            def.rewardAmount
        );

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

    // ── 기간 계산 ──

    static string GetCurrentPeriod(string type)
    {
        var now = DateTime.UtcNow;
        return type switch
        {
            "daily" => now.ToString("yyyy-MM-dd"),
            "weekly" => $"{ISOWeek.GetYear(now)}-W{ISOWeek.GetWeekOfYear(now):D2}",
            _ => "permanent" // achievement
        };
    }
}
