#if UNITY_EDITOR
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>GitHub Actions 빌드 상태 폴링 + 결과 수집</summary>
    public static class BuildTracker
    {
        public enum Status { Idle, Polling, Success, Failed, Timeout }

        public static Status CurrentStatus { get; private set; }
        public static string CurrentVersion { get; private set; }
        public static string FailedLog { get; private set; }
        public static string ReleaseUrl { get; private set; }
        public static float ElapsedSeconds =>
            (float)(EditorApplication.timeSinceStartup - _startTime);

        static double _startTime;
        static double _lastPollTime;
        static bool _polling;

        const double POLL_INTERVAL = 15;
        const double TIMEOUT = 600; // 10분

        // ── 빌드 히스토리 ──

        public struct HistoryEntry
        {
            public string Version;
            public string Status;       // "success", "failure"
            public string Date;
            public long RunId;
        }

        static HistoryEntry[] _cachedHistory;

        // ── 시작/중지 ──

        /// <summary>Release 후 폴링 시작</summary>
        public static void StartTracking(string version)
        {
            CurrentVersion = version;
            _startTime = EditorApplication.timeSinceStartup;
            _lastPollTime = 0;
            CurrentStatus = Status.Polling;
            FailedLog = null;
            ReleaseUrl = null;
            _polling = false;
            _cachedHistory = null;
            EditorApplication.update += Poll;
        }

        /// <summary>폴링 중지</summary>
        public static void Stop()
        {
            EditorApplication.update -= Poll;
            if (CurrentStatus == Status.Polling)
                CurrentStatus = Status.Idle;
        }

        // ── 폴링 ──

        static void Poll()
        {
            if (_polling) return;

            var now = EditorApplication.timeSinceStartup;

            if (now - _startTime > TIMEOUT)
            {
                CurrentStatus = Status.Timeout;
                EditorApplication.update -= Poll;
                return;
            }

            if (now - _lastPollTime < POLL_INTERVAL) return;
            _lastPollTime = now;

            _polling = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                var result = CheckLatestRun();
                EditorApplication.delayCall += () =>
                {
                    _polling = false;
                    HandleResult(result);
                };
            });
        }

        // ── 결과 체크 ──

        struct RunResult
        {
            public string status;      // "queued", "in_progress", "completed"
            public string conclusion;   // "success", "failure", "cancelled"
            public long runId;
        }

        static RunResult CheckLatestRun()
        {
            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo))
                return new RunResult { status = "unknown" };

            var (code, output) = GhChecker.RunGh(
                $"run list --repo {repo} --limit 1 --json databaseId,status,conclusion --jq \".[0]\"");

            if (code != 0 || string.IsNullOrEmpty(output))
                return new RunResult { status = "unknown" };

            var result = new RunResult();
            var idMatch = Regex.Match(output, "\"databaseId\":\\s*(\\d+)");
            var statusMatch = Regex.Match(output, "\"status\":\\s*\"(\\w+)\"");
            var conclusionMatch = Regex.Match(output, "\"conclusion\":\\s*\"(\\w+)\"");

            if (idMatch.Success) result.runId = long.Parse(idMatch.Groups[1].Value);
            if (statusMatch.Success) result.status = statusMatch.Groups[1].Value;
            if (conclusionMatch.Success) result.conclusion = conclusionMatch.Groups[1].Value;

            return result;
        }

        static void HandleResult(RunResult result)
        {
            if (result.status != "completed") return;

            EditorApplication.update -= Poll;

            if (result.conclusion == "success")
            {
                CurrentStatus = Status.Success;
                FetchReleaseUrl();
            }
            else
            {
                CurrentStatus = Status.Failed;
                FetchFailedLog(result.runId);
            }
        }

        // ── 실패 로그 ──

        static void FetchFailedLog(long runId)
        {
            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo)) return;

            System.Threading.Tasks.Task.Run(() =>
            {
                var (code, output) = GhChecker.RunGh(
                    $"run view {runId} --repo {repo} --log-failed");

                EditorApplication.delayCall += () =>
                {
                    if (code == 0 && !string.IsNullOrEmpty(output))
                    {
                        var lines = output.Split('\n');
                        if (lines.Length > 50)
                            output = string.Join("\n",
                                new System.ArraySegment<string>(lines, lines.Length - 50, 50));
                        FailedLog = output;
                    }
                    else
                    {
                        FailedLog = "로그를 가져올 수 없습니다.";
                    }
                };
            });
        }

        // ── Release URL ──

        static void FetchReleaseUrl()
        {
            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(CurrentVersion)) return;

            var tag = CurrentVersion.StartsWith("v") ? CurrentVersion : $"v{CurrentVersion}";
            ReleaseUrl = $"https://github.com/{repo}/releases/tag/{tag}";
        }

        // ── 히스토리 ──

        static bool _historyLoading;

        /// <summary>최근 빌드 히스토리 (캐싱 + 비동기 로드)</summary>
        public static HistoryEntry[] GetHistory(int limit = 5)
        {
            if (_cachedHistory != null) return _cachedHistory;

            // 최초 호출 시 비동기 로드 시작, 빈 배열 반환
            if (!_historyLoading)
            {
                _historyLoading = true;
                var repo = GitHelper.GetGitHubRepo();
                if (string.IsNullOrEmpty(repo))
                {
                    _cachedHistory = System.Array.Empty<HistoryEntry>();
                    _historyLoading = false;
                    return _cachedHistory;
                }

                System.Threading.Tasks.Task.Run(() =>
                {
                    var result = FetchHistorySync(repo, limit);
                    EditorApplication.delayCall += () =>
                    {
                        _cachedHistory = result;
                        _historyLoading = false;
                    };
                });
            }

            return System.Array.Empty<HistoryEntry>();
        }

        static HistoryEntry[] FetchHistorySync(string repo, int limit)
        {
            var (code, output) = GhChecker.RunGh(
                $"run list --repo {repo} --limit {limit} --json databaseId,status,conclusion,displayTitle,createdAt --jq \".[]\"");

            if (code != 0 || string.IsNullOrEmpty(output))
                return System.Array.Empty<HistoryEntry>();

            var entries = new System.Collections.Generic.List<HistoryEntry>();
            var blocks = output.Split('{');

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;
                var json = "{" + block;

                var entry = new HistoryEntry();
                var titleMatch = Regex.Match(json, "\"displayTitle\":\\s*\"([^\"]+)\"");
                var conclusionMatch = Regex.Match(json, "\"conclusion\":\\s*\"(\\w+)\"");
                var dateMatch = Regex.Match(json, "\"createdAt\":\\s*\"([^\"]+)\"");
                var idMatch = Regex.Match(json, "\"databaseId\":\\s*(\\d+)");

                if (titleMatch.Success) entry.Version = titleMatch.Groups[1].Value;
                if (conclusionMatch.Success) entry.Status = conclusionMatch.Groups[1].Value;
                if (dateMatch.Success)
                {
                    var raw = dateMatch.Groups[1].Value;
                    entry.Date = raw.Length >= 10 ? raw.Substring(0, 10) : raw;
                }
                if (idMatch.Success) entry.RunId = long.Parse(idMatch.Groups[1].Value);

                if (!string.IsNullOrEmpty(entry.Version))
                    entries.Add(entry);
            }

            return entries.ToArray();
        }

        /// <summary>히스토리 캐시 초기화</summary>
        public static void InvalidateHistory() => _cachedHistory = null;
    }
}
#endif
