using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tjdtjq5.SupaRun;

/// <summary>시즌패스 서비스. XP/레벨/보상/프리미엄.</summary>
[Service]
public class SeasonPassService
{
    readonly IGameDB _db;
    readonly CurrencyService _currency;
    readonly InventoryService _inventory;

    public SeasonPassService(IGameDB db, CurrencyService currency, InventoryService inventory)
    {
        _db = db;
        _currency = currency;
        _inventory = inventory;
    }

    /// <summary>현재 활성 시즌패스.</summary>
    [API]
    public async Task<SeasonPassDef> GetCurrentSeason()
    {
        var all = await _db.GetAll<SeasonPassDef>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return all.Find(s => s.startAt <= now && s.endAt >= now);
    }

    /// <summary>내 진행도. 없으면 자동 생성.</summary>
    [API]
    public async Task<SeasonPassProgress> GetProgress(string playerId, string seasonId)
    {
        var id = $"{seasonId}_{playerId}";
        var progress = await _db.Get<SeasonPassProgress>(id);

        if (progress == null)
        {
            progress = new SeasonPassProgress
            {
                id = id,
                seasonId = seasonId,
                playerId = playerId,
                xp = 0,
                level = 0,
                claimedFree = "[]",
                claimedPremium = "[]"
            };
            await _db.Save(progress);
        }

        return progress;
    }

    /// <summary>레벨별 보상 목록.</summary>
    [API]
    public async Task<List<SeasonPassLevel>> GetLevels(string seasonId)
    {
        var all = await _db.GetAll<SeasonPassLevel>();
        return all.Where(l => l.seasonId == seasonId).OrderBy(l => l.level).ToList();
    }

    /// <summary>XP 추가. 레벨 자동 계산.</summary>
    [API]
    public async Task<SeasonPassProgress> AddXP(string playerId, string seasonId, long amount, string reason)
    {
        if (amount <= 0)
            throw new ArgumentException("XP는 0보다 커야 합니다.");

        var def = await _db.Get<SeasonPassDef>(seasonId);
        if (def == null)
            throw new InvalidOperationException("시즌패스를 찾을 수 없습니다.");

        var progress = await GetProgress(playerId, seasonId);
        progress.xp += amount;
        progress.level = (int)Math.Min(progress.xp / def.xpPerLevel, def.maxLevel);
        await _db.Save(progress);

        return progress;
    }

    /// <summary>프리미엄 구매. 재화 차감.</summary>
    [API]
    public async Task<SeasonPassProgress> BuyPremium(string playerId, string seasonId)
    {
        var def = await _db.Get<SeasonPassDef>(seasonId);
        if (def == null)
            throw new InvalidOperationException("시즌패스를 찾을 수 없습니다.");

        var progress = await GetProgress(playerId, seasonId);
        if (progress.isPremium)
            throw new InvalidOperationException("이미 프리미엄을 구매했습니다.");

        await _currency.Subtract(playerId, def.premiumCurrencyId, def.premiumPrice, $"seasonpass_premium:{seasonId}");

        progress.isPremium = true;
        await _db.Save(progress);

        return progress;
    }

    /// <summary>특정 레벨 보상 수령.</summary>
    [API]
    public async Task<SeasonPassProgress> ClaimReward(string playerId, string seasonId, int level, bool isPremium)
    {
        var progress = await GetProgress(playerId, seasonId);

        if (level > progress.level)
            throw new InvalidOperationException($"아직 레벨 {level}에 도달하지 않았습니다. (현재 {progress.level})");

        if (isPremium && !progress.isPremium)
            throw new InvalidOperationException("프리미엄을 구매해야 유료 보상을 수령할 수 있습니다.");

        // 이미 수령했는지 확인
        var claimed = ParseClaimed(isPremium ? progress.claimedPremium : progress.claimedFree);
        if (claimed.Contains(level))
            throw new InvalidOperationException($"레벨 {level} {(isPremium ? "유료" : "무료")} 보상을 이미 수령했습니다.");

        // 해당 레벨 보상 찾기
        var levels = await _db.GetAll<SeasonPassLevel>();
        var levelDef = levels.Find(l => l.seasonId == seasonId && l.level == level);
        if (levelDef == null)
            throw new InvalidOperationException($"레벨 {level} 보상 정의가 없습니다.");

        var rewardsJson = isPremium ? levelDef.premiumRewards : levelDef.freeRewards;
        if (string.IsNullOrEmpty(rewardsJson))
            throw new InvalidOperationException($"레벨 {level}에 {(isPremium ? "유료" : "무료")} 보상이 없습니다.");

        // 보상 지급
        await GrantRewards(playerId, rewardsJson, $"seasonpass:{seasonId}_lv{level}_{(isPremium ? "premium" : "free")}");

        // 수령 기록
        claimed.Add(level);
        if (isPremium)
            progress.claimedPremium = JsonConvert.SerializeObject(claimed);
        else
            progress.claimedFree = JsonConvert.SerializeObject(claimed);

        await _db.Save(progress);
        return progress;
    }

    /// <summary>수령 가능한 모든 보상 일괄 수령. 수령 건수 반환.</summary>
    [API]
    public async Task<int> ClaimAll(string playerId, string seasonId)
    {
        var progress = await GetProgress(playerId, seasonId);
        var levels = await GetLevels(seasonId);
        var claimedFree = ParseClaimed(progress.claimedFree);
        var claimedPremium = ParseClaimed(progress.claimedPremium);
        var count = 0;

        foreach (var lv in levels.Where(l => l.level <= progress.level))
        {
            // 무료 보상
            if (!string.IsNullOrEmpty(lv.freeRewards) && !claimedFree.Contains(lv.level))
            {
                try
                {
                    await GrantRewards(playerId, lv.freeRewards, $"seasonpass:{seasonId}_lv{lv.level}_free");
                    claimedFree.Add(lv.level);
                    count++;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SupaRun:SeasonPass] ClaimAll free lv{lv.level} 실패: {ex.Message}");
                }
            }

            // 유료 보상
            if (progress.isPremium && !string.IsNullOrEmpty(lv.premiumRewards) && !claimedPremium.Contains(lv.level))
            {
                try
                {
                    await GrantRewards(playerId, lv.premiumRewards, $"seasonpass:{seasonId}_lv{lv.level}_premium");
                    claimedPremium.Add(lv.level);
                    count++;
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[SupaRun:SeasonPass] ClaimAll premium lv{lv.level} 실패: {ex.Message}");
                }
            }
        }

        progress.claimedFree = JsonConvert.SerializeObject(claimedFree);
        progress.claimedPremium = JsonConvert.SerializeObject(claimedPremium);
        await _db.Save(progress);

        return count;
    }

    // ── 내부 ──

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

    static List<int> ParseClaimed(string json)
    {
        if (string.IsNullOrEmpty(json)) return new List<int>();
        try { return JsonConvert.DeserializeObject<List<int>>(json) ?? new List<int>(); }
        catch { return new List<int>(); }
    }

    class RewardEntry
    {
        public string type;
        public string id;
        public long amount;
    }
}
