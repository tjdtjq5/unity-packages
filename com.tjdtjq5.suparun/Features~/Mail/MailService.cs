using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tjdtjq5.SupaRun;

/// <summary>우편 서비스. 발송/조회/수령/삭제. 다중 보상 지원.</summary>
[Service]
public class MailService
{
    readonly IGameDB _db;
    readonly CurrencyService _currency;
    readonly InventoryService _inventory;

    public MailService(IGameDB db, CurrencyService currency, InventoryService inventory)
    {
        _db = db;
        _currency = currency;
        _inventory = inventory;
    }

    /// <summary>우편 목록 조회. 만료 제외, 최신순.</summary>
    [API]
    public async Task<List<Mail>> GetMails(string playerId)
    {
        var mails = await _db.Query<Mail>(new QueryOptions()
            .Eq("playerId", playerId).OrderByDesc("createdAt"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return mails.Where(m => m.expiresAt == 0 || m.expiresAt > now).ToList();
    }

    /// <summary>우편 읽음 처리.</summary>
    [API]
    public async Task<Mail> ReadMail(string playerId, string mailId)
    {
        var mail = await GetAndValidate(playerId, mailId);
        mail.isRead = true;
        await _db.Save(mail);
        return mail;
    }

    /// <summary>첨부 보상 수령. 다중 보상 지급.</summary>
    [API]
    public async Task<Mail> ClaimReward(string playerId, string mailId)
    {
        var mail = await GetAndValidate(playerId, mailId);

        if (mail.claimed)
            throw new InvalidOperationException("이미 수령한 보상입니다.");
        if (string.IsNullOrEmpty(mail.rewards))
            throw new InvalidOperationException("첨부 보상이 없는 우편입니다.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (mail.expiresAt > 0 && mail.expiresAt <= now)
            throw new InvalidOperationException("만료된 우편입니다.");

        // 보상 지급
        await GrantRewards(playerId, mail.rewards, $"mail:{mailId}");

        mail.claimed = true;
        mail.isRead = true;
        await _db.Save(mail);
        return mail;
    }

    /// <summary>모든 보상 일괄 수령. 성공 건수 반환.</summary>
    [API]
    public async Task<int> ClaimAll(string playerId)
    {
        var mails = await GetMails(playerId);
        var count = 0;
        foreach (var mail in mails)
        {
            if (mail.claimed || string.IsNullOrEmpty(mail.rewards)) continue;
            try
            {
                await ClaimReward(playerId, mail.id);
                count++;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SupaRun:Mail] ClaimAll 개별 실패: {mail.id} — {ex.Message}");
            }
        }
        return count;
    }

    /// <summary>우편 삭제.</summary>
    [API]
    public async Task DeleteMail(string playerId, string mailId)
    {
        await GetAndValidate(playerId, mailId);
        await _db.Delete<Mail>(mailId);
    }

    /// <summary>시스템 우편 발송 (서버 전용).</summary>
    [API] [Private]
    public async Task<Mail> SendMail(string playerId, string title, string body,
        string rewards = null, long expiresAt = 0)
    {
        var mail = new Mail
        {
            id = Guid.NewGuid().ToString(),
            playerId = playerId,
            title = title,
            body = body,
            rewards = rewards,
            expiresAt = expiresAt
        };
        await _db.Save(mail);
        return mail;
    }

    /// <summary>안 읽은 우편 수.</summary>
    [API]
    public async Task<int> GetUnreadCount(string playerId)
    {
        return await _db.Count<Mail>(new QueryOptions()
            .Eq("playerId", playerId).Eq("isRead", false));
    }

    // ── 내부 헬퍼 ──

    async Task<Mail> GetAndValidate(string playerId, string mailId)
    {
        var mail = await _db.Get<Mail>(mailId);
        if (mail == null || mail.playerId != playerId)
            throw new InvalidOperationException("우편을 찾을 수 없습니다.");
        return mail;
    }

    async Task GrantRewards(string playerId, string rewardsJson, string reason)
    {
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

    class RewardEntry
    {
        public string type;
        public string id;
        public long amount;
    }
}
