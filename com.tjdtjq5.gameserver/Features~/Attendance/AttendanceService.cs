using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tjdtjq5.GameServer;

/// <summary>출석 체크 서비스. 일일 체크 + 연속 출석 + 보상.</summary>
[Service]
public class AttendanceService
{
    readonly IGameDB _db;
    readonly MailService _mail;

    public AttendanceService(IGameDB db, MailService mail)
    {
        _db = db;
        _mail = mail;
    }

    /// <summary>오늘 출석 체크. 이미 했으면 기존 기록 반환.</summary>
    [API]
    public async Task<AttendanceRecord> CheckIn(string playerId)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // 이미 출석했는지 확인
        var todayList = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId).Eq("date", today).SetLimit(1));
        if (todayList.Count > 0)
            return todayList[0];

        // 어제 기록으로 연속 출석 계산
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var yesterdayList = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId).Eq("date", yesterday).SetLimit(1));
        var yesterdayRecord = yesterdayList.Count > 0 ? yesterdayList[0] : null;
        var streak = (yesterdayRecord?.streak ?? 0) + 1;

        // 이번 달 출석 일수
        var monthRecords = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId));
        var monthPrefix = DateTime.UtcNow.ToString("yyyy-MM");
        var monthCount = monthRecords.Count(r => r.date.StartsWith(monthPrefix)) + 1;

        var record = new AttendanceRecord
        {
            id = $"{playerId}_{today}",
            playerId = playerId,
            date = today,
            dayOfMonth = monthCount,
            streak = streak
        };
        await _db.Save(record);

        // 출석 보상 확인 + 우편 발송
        await CheckAndSendRewards(playerId, monthCount);

        return record;
    }

    /// <summary>이번 달 출석 기록.</summary>
    [API]
    public async Task<List<AttendanceRecord>> GetMonthRecords(string playerId)
    {
        var records = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId).OrderByAsc("date"));
        var monthPrefix = DateTime.UtcNow.ToString("yyyy-MM");
        return records.Where(r => r.date.StartsWith(monthPrefix)).ToList();
    }

    /// <summary>오늘 출석 여부 + 연속 일수.</summary>
    [API]
    public async Task<(bool checkedIn, int streak, int monthDays)> GetStatus(string playerId)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var todayList = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId).Eq("date", today).SetLimit(1));
        var todayRecord = todayList.Count > 0 ? todayList[0] : null;

        var allRecords = await _db.Query<AttendanceRecord>(new QueryOptions()
            .Eq("playerId", playerId));
        var monthPrefix = DateTime.UtcNow.ToString("yyyy-MM");
        var monthDays = allRecords.Count(r => r.date.StartsWith(monthPrefix));

        return (todayRecord != null, todayRecord?.streak ?? 0, monthDays);
    }

    /// <summary>출석 보상 목록 (Config).</summary>
    [API]
    public async Task<List<AttendanceReward>> GetRewards()
    {
        return await _db.GetAll<AttendanceReward>();
    }

    // ── 보상 처리 ──

    async Task CheckAndSendRewards(string playerId, int dayOfMonth)
    {
        var rewards = await _db.GetAll<AttendanceReward>();
        var reward = rewards.Find(r => r.day == dayOfMonth);
        if (reward == null) return;

        // 우편으로 보상 발송
        await _mail.SendMail(
            playerId,
            $"출석 {dayOfMonth}일 보상",
            $"출석 {dayOfMonth}일 달성 보상입니다!",
            reward.rewardType,
            reward.rewardId,
            reward.rewardAmount
        );
    }
}
