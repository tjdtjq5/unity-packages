using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tjdtjq5.SupaRun;

/// <summary>랭킹 서비스. 전체/그룹 랭킹 + 점수 정책 + 시즌 보상.</summary>
[Service]
public class RankingService
{
    readonly IGameDB _db;
    readonly MailService _mail;

    public RankingService(IGameDB db, MailService mail)
    {
        _db = db;
        _mail = mail;
    }

    // ── 점수 등록 ──

    /// <summary>점수 등록. 시즌 점수 정책에 따라 갱신.</summary>
    [API]
    public async Task<RankingEntry> SubmitScore(string playerId, string boardId, long score,
        string playerName = null, string metadata = null)
    {
        var season = await GetCurrentSeason(boardId);
        var policy = season?.scorePolicy ?? "highest";

        var id = $"{boardId}_{playerId}";
        var existing = await _db.Get<RankingEntry>(id);

        // 그룹 배정 (그룹 랭킹인 경우)
        string groupId = null;
        if (season != null && season.groupSize > 0)
            groupId = await GetOrAssignGroup(playerId, boardId, season);

        if (existing != null)
        {
            var shouldUpdate = policy switch
            {
                "highest" => score > existing.score,
                "lowest" => score < existing.score,
                "latest" => true,
                "accumulate" => true,
                _ => score > existing.score
            };

            if (shouldUpdate)
            {
                existing.score = policy == "accumulate" ? existing.score + score : score;
                if (playerName != null) existing.playerName = playerName;
                if (metadata != null) existing.metadata = metadata;
                if (groupId != null) existing.groupId = groupId;
                await _db.Save(existing);
            }
            return existing;
        }

        var entry = new RankingEntry
        {
            id = id,
            boardId = boardId,
            playerId = playerId,
            groupId = groupId,
            playerName = playerName ?? playerId,
            score = score,
            metadata = metadata ?? ""
        };
        await _db.Save(entry);
        return entry;
    }

    // ── 전체 랭킹 조회 ──

    /// <summary>상위 N명. 점수 내림차순 + 동점 시 먼저 달성한 사람 우선.</summary>
    [API]
    public async Task<List<RankingEntry>> GetTopRanks(string boardId, int count = 100)
    {
        var entries = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).OrderByDesc("score").SetLimit(count));
        return entries.OrderByDescending(e => e.score).ThenBy(e => e.updatedAt).ToList();
    }

    /// <summary>내 기록 조회.</summary>
    [API]
    public async Task<RankingEntry> GetMyEntry(string playerId, string boardId)
    {
        return await _db.Get<RankingEntry>($"{boardId}_{playerId}");
    }

    /// <summary>내 순위. 나보다 점수 높은 사람 수 + 1.</summary>
    [API]
    public async Task<int> GetMyRank(string playerId, string boardId)
    {
        var my = await _db.Get<RankingEntry>($"{boardId}_{playerId}");
        if (my == null) return -1;
        var above = await _db.Count<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).Gt("score", my.score));
        return above + 1;
    }

    /// <summary>내 주변 순위.</summary>
    [API]
    public async Task<List<RankingEntry>> GetAroundMe(string playerId, string boardId, int range = 5)
    {
        var rank = await GetMyRank(playerId, boardId);
        if (rank < 0) return new List<RankingEntry>();
        var offset = Math.Max(0, rank - 1 - range);
        var limit = range * 2 + 1;
        var entries = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).OrderByDesc("score").SetLimit(limit).SetOffset(offset));
        return entries.OrderByDescending(e => e.score).ThenBy(e => e.updatedAt).ToList();
    }

    /// <summary>총 참가자 수.</summary>
    [API]
    public async Task<int> GetTotalCount(string boardId)
    {
        return await _db.Count<RankingEntry>(new QueryOptions().Eq("boardId", boardId));
    }

    // ── 그룹 랭킹 ──

    /// <summary>내 그룹 내 전체 순위.</summary>
    [API]
    public async Task<List<RankingEntry>> GetGroupRanks(string playerId, string boardId)
    {
        var group = await _db.Get<RankingGroup>($"{boardId}_{playerId}");
        if (group == null) return new List<RankingEntry>();

        var entries = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).Eq("groupId", group.groupId).OrderByDesc("score"));
        return entries.OrderByDescending(e => e.score).ThenBy(e => e.updatedAt).ToList();
    }

    /// <summary>내 그룹 내 순위.</summary>
    [API]
    public async Task<int> GetMyGroupRank(string playerId, string boardId)
    {
        var group = await _db.Get<RankingGroup>($"{boardId}_{playerId}");
        if (group == null) return -1;

        var my = await _db.Get<RankingEntry>($"{boardId}_{playerId}");
        if (my == null) return -1;

        var above = await _db.Count<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).Eq("groupId", group.groupId).Gt("score", my.score));
        return above + 1;
    }

    /// <summary>내 그룹 정보.</summary>
    [API]
    public async Task<RankingGroup> GetMyGroup(string playerId, string boardId)
    {
        return await _db.Get<RankingGroup>($"{boardId}_{playerId}");
    }

    // ── 시즌 ──

    /// <summary>현재 활성 시즌 (startAt~endAt 기간 자동 판단).</summary>
    [API]
    public async Task<RankingSeason> GetCurrentSeason(string boardId)
    {
        var seasons = await _db.GetAll<RankingSeason>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return seasons.Find(s => s.boardId == boardId && s.startAt <= now && s.endAt >= now);
    }

    /// <summary>시즌 종료. 순위별 보상 우편 발송 + 보드/그룹 리셋. 서버 전용.</summary>
    [API] [Private]
    public async Task<int> EndSeason(string boardId)
    {
        var season = await GetCurrentSeason(boardId);

        // 시즌 보상 발송
        if (season != null && !string.IsNullOrEmpty(season.rewards))
            await DistributeSeasonRewards(boardId, season);

        // 보드 리셋
        var deleted = await _db.DeleteAll<RankingEntry>(new QueryOptions().Eq("boardId", boardId));

        // 그룹 리셋
        await _db.DeleteAll<RankingGroup>(new QueryOptions().Eq("boardId", boardId));

        return deleted;
    }

    // ── 내부 헬퍼 ──

    /// <summary>
    /// [커스텀] 그룹 배정 키. 개발자가 게임에 맞게 수정.
    /// null 반환 시 순서대로 배정.
    /// 예: var player = await _db.Get&lt;Player&gt;(playerId);
    ///     return $"lv{player.level / 10}";
    /// </summary>
    async Task<string> GetGroupKey(string playerId, string boardId)
    {
        // 기본: 순서 배정 (그룹 키 없음)
        return null;
    }

    async Task<string> GetOrAssignGroup(string playerId, string boardId, RankingSeason season)
    {
        var existing = await _db.Get<RankingGroup>($"{boardId}_{playerId}");
        if (existing != null) return existing.groupId;

        var groupKey = await GetGroupKey(playerId, boardId);

        // 빈 자리 있는 그룹 찾기
        var allGroups = await _db.Query<RankingGroup>(new QueryOptions().Eq("boardId", boardId));
        var groupCounts = allGroups.GroupBy(g => g.groupId).ToDictionary(g => g.Key, g => g.Count());

        string targetGroupId = null;

        if (groupKey != null)
        {
            // 같은 키 접두사 그룹 중 빈 자리
            targetGroupId = groupCounts
                .Where(kv => kv.Key.StartsWith($"{boardId}_{groupKey}_") && kv.Value < season.groupSize)
                .Select(kv => kv.Key).FirstOrDefault();

            if (targetGroupId == null)
                targetGroupId = $"{boardId}_{groupKey}_{Guid.NewGuid().ToString("N")[..6]}";
        }
        else
        {
            // 아무 그룹 중 빈 자리
            targetGroupId = groupCounts
                .Where(kv => kv.Value < season.groupSize)
                .Select(kv => kv.Key).FirstOrDefault();

            if (targetGroupId == null)
                targetGroupId = $"{boardId}_g{Guid.NewGuid().ToString("N")[..6]}";
        }

        await _db.Save(new RankingGroup
        {
            id = $"{boardId}_{playerId}",
            boardId = boardId,
            playerId = playerId,
            groupId = targetGroupId
        });

        return targetGroupId;
    }

    async Task DistributeSeasonRewards(string boardId, RankingSeason season)
    {
        var rewardTiers = JsonConvert.DeserializeObject<List<SeasonRewardTier>>(season.rewards);
        if (rewardTiers == null || rewardTiers.Count == 0) return;

        // 전체 순위
        var entries = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).OrderByDesc("score"));
        var sorted = entries.OrderByDescending(e => e.score).ThenBy(e => e.updatedAt).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var rank = i + 1;
            var entry = sorted[i];

            var tier = rewardTiers.Find(t => rank >= t.rankMin && rank <= t.rankMax);
            if (tier == null || string.IsNullOrEmpty(tier.rewards)) continue;

            await _mail.SendMail(
                entry.playerId,
                $"{season.name} 시즌 보상 ({rank}위)",
                $"{season.name} 시즌 {rank}위 보상입니다!",
                tier.rewards
            );
        }
    }

    class SeasonRewardTier
    {
        public int rankMin;
        public int rankMax;
        public string rewards; // JSON — Shop/Mail과 동일 형식
    }
}
