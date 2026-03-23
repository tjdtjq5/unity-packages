using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tjdtjq5.GameServer;

/// <summary>랭킹 서비스. 점수 등록/조회/순위/시즌.</summary>
[Service]
public class RankingService
{
    readonly IGameDB _db;
    public RankingService(IGameDB db) => _db = db;

    /// <summary>점수 등록. 기존보다 높으면 갱신, 없으면 생성.</summary>
    [API]
    public async Task<RankingEntry> SubmitScore(string playerId, string boardId, long score, string playerName = null, string metadata = null)
    {
        var list = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).Eq("playerId", playerId).SetLimit(1));
        var existing = list.Count > 0 ? list[0] : null;

        if (existing != null)
        {
            if (score > existing.score)
            {
                existing.score = score;
                if (playerName != null) existing.playerName = playerName;
                if (metadata != null) existing.metadata = metadata;
                await _db.Save(existing);
            }
            return existing;
        }

        var entry = new RankingEntry
        {
            id = $"{boardId}_{playerId}",
            boardId = boardId,
            playerId = playerId,
            playerName = playerName ?? playerId,
            score = score,
            metadata = metadata ?? ""
        };
        await _db.Save(entry);
        return entry;
    }

    /// <summary>상위 N명 조회. 점수 내림차순.</summary>
    [API]
    public async Task<List<RankingEntry>> GetTopRanks(string boardId, int count = 100)
    {
        return await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).OrderByDesc("score").SetLimit(count));
    }

    /// <summary>내 순위 조회. 1-based. 기록 없으면 -1.</summary>
    [API]
    public async Task<(int rank, RankingEntry entry)> GetMyRank(string playerId, string boardId)
    {
        var sorted = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).OrderByDesc("score"));

        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].playerId == playerId)
                return (i + 1, sorted[i]);
        }

        return (-1, null);
    }

    /// <summary>내 주변 순위 조회 (위아래 range명씩).</summary>
    [API]
    public async Task<List<(int rank, RankingEntry entry)>> GetAroundMe(string playerId, string boardId, int range = 5)
    {
        var sorted = await _db.Query<RankingEntry>(new QueryOptions()
            .Eq("boardId", boardId).OrderByDesc("score"));

        var myIndex = sorted.FindIndex(e => e.playerId == playerId);
        if (myIndex < 0) return new List<(int, RankingEntry)>();

        var start = Math.Max(0, myIndex - range);
        var end = Math.Min(sorted.Count - 1, myIndex + range);

        var result = new List<(int rank, RankingEntry entry)>();
        for (var i = start; i <= end; i++)
            result.Add((i + 1, sorted[i]));
        return result;
    }

    /// <summary>리더보드 총 참가자 수.</summary>
    [API]
    public async Task<int> GetTotalCount(string boardId)
    {
        var entries = await _db.Query<RankingEntry>(new QueryOptions().Eq("boardId", boardId));
        return entries.Count;
    }

    /// <summary>현재 활성 시즌 조회.</summary>
    [API]
    public async Task<RankingSeason> GetCurrentSeason(string boardId)
    {
        var seasons = await _db.GetAll<RankingSeason>(); // [Config] — 소규모
        return seasons.Find(s => s.boardId == boardId && s.isActive);
    }

    /// <summary>리더보드 초기화 (시즌 리셋). 서버 전용.</summary>
    [API] [Private]
    public async Task<int> ResetBoard(string boardId)
    {
        var targets = await _db.Query<RankingEntry>(new QueryOptions().Eq("boardId", boardId));
        foreach (var entry in targets)
            await _db.Delete<RankingEntry>(entry.id);
        return targets.Count;
    }
}
