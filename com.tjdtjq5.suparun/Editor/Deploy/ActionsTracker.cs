using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>GitHub Actions 상태 폴링 + 결과 수집.</summary>
    public static class ActionsTracker
    {
        public enum Status { Idle, Polling, Success, Failed, Timeout }

        public static Status CurrentStatus { get; private set; }
        public static string FailedLog { get; private set; }
        public static string CloudRunUrl { get; private set; }
        public static float ElapsedSeconds =>
            (float)(EditorApplication.timeSinceStartup - _startTime);

        static double _startTime;
        static double _lastPollTime;
        static string _repo;
        static bool _polling;

        const double POLL_INTERVAL = 15;
        const double TIMEOUT = 600; // 10분

        public static void StartTracking(string repo)
        {
            _repo = repo;
            _startTime = EditorApplication.timeSinceStartup;
            _lastPollTime = 0;
            CurrentStatus = Status.Polling;
            FailedLog = null;
            CloudRunUrl = null;
            _polling = false;
            EditorApplication.update += Poll;
        }

        public static void Stop()
        {
            EditorApplication.update -= Poll;
            CurrentStatus = Status.Idle;
        }

        static void Poll()
        {
            if (_polling) return;

            var now = EditorApplication.timeSinceStartup;

            // 타임아웃
            if (now - _startTime > TIMEOUT)
            {
                CurrentStatus = Status.Timeout;
                EditorApplication.update -= Poll;
                return;
            }

            // 간격
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
            public string status;     // queued, in_progress, completed
            public string conclusion;  // success, failure, cancelled
            public long runId;
        }

        static RunResult CheckLatestRun()
        {
            var (code, output) = PrerequisiteChecker.RunGh(
                $"run list --repo {_repo} --limit 1 --json databaseId,status,conclusion --jq \".[0]\"");

            if (code != 0 || string.IsNullOrEmpty(output))
                return new RunResult { status = "unknown" };

            // 간단 JSON 파싱 (Newtonsoft 의존 없이)
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
            if (result.status != "completed") return; // 아직 진행 중

            EditorApplication.update -= Poll;

            if (result.conclusion == "success")
            {
                CurrentStatus = Status.Success;
                FetchCloudRunUrl();
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
            System.Threading.Tasks.Task.Run(() =>
            {
                var (code, output) = PrerequisiteChecker.RunGh(
                    $"run view {runId} --repo {_repo} --log-failed");

                EditorApplication.delayCall += () =>
                {
                    if (code == 0 && !string.IsNullOrEmpty(output))
                    {
                        var lines = output.Split('\n');
                        if (lines.Length > 50)
                        {
                            // 마지막 50줄
                            output = string.Join("\n",
                                new System.ArraySegment<string>(lines, lines.Length - 50, 50));
                        }
                        FailedLog = output;
                    }
                    else
                    {
                        FailedLog = "로그를 가져올 수 없습니다.";
                    }
                };
            });
        }

        // ── Cloud Run URL ──

        static void FetchCloudRunUrl()
        {
            var settings = SupaRunSettings.Instance;

            // 메인 스레드에서 gcloud 경로 확보
            var gcloud = PrerequisiteChecker.CheckGcloud();
            if (!gcloud.Installed)
            {
                Debug.LogWarning("[SupaRun] gcloud 미설치 — Cloud Run URL 자동 저장 불가");
                return;
            }

            var gcloudPath = gcloud.FullPath ?? "gcloud";
            var serviceName = settings.gcpServiceName?.ToLower();
            var region = settings.gcpRegion;
            var projectId = settings.gcpProjectId;

            System.Threading.Tasks.Task.Run(() =>
            {
                var (code, output) = PrerequisiteChecker.Run(gcloudPath,
                    $"run services describe {serviceName} " +
                    $"--region {region} --project {projectId} " +
                    $"--format=value(status.url)");

                EditorApplication.delayCall += () =>
                {
                    if (code == 0 && !string.IsNullOrEmpty(output?.Trim()))
                    {
                        CloudRunUrl = output.Trim();
                        settings.cloudRunUrl = CloudRunUrl;
                        settings.Save();
                        Debug.Log($"[SupaRun] Cloud Run URL 저장: {CloudRunUrl}");
                    }
                    else
                    {
                        Debug.LogWarning($"[SupaRun] Cloud Run URL 조회 실패 (code={code})");
                    }
                };
            });
        }

        // ── 유틸 ──

        public static string GetActionsUrl(string repo) =>
            $"https://github.com/{repo}/actions";
    }
}
