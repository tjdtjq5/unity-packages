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
        static string _headSha;       // null이면 head_sha 필터 없이 latest run 조회 (구버전 호환)
        static int _foundAttempts;    // head_sha에 해당하는 run을 못 찾은 횟수
        static bool _polling;

        const double POLL_INTERVAL = 5;       // 폴링 주기 (초)
        const double TIMEOUT = 600;            // 10분
        const int MAX_FOUND_ATTEMPTS = 12;     // 5초 × 12 = 60초 동안 새 run 등록 대기

        // 기존 호출 호환 (head_sha 필터 비활성, latest run 사용)
        public static void StartTracking(string repo) => StartTracking(repo, null);

        public static void StartTracking(string repo, string headSha)
        {
            _repo = repo;
            _headSha = string.IsNullOrEmpty(headSha) ? null : headSha;
            _startTime = EditorApplication.timeSinceStartup;
            _lastPollTime = 0;
            _foundAttempts = 0;
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
            public string status;     // queued, in_progress, completed, not_found
            public string conclusion;  // success, failure, cancelled
            public long runId;
        }

        static RunResult CheckLatestRun()
        {
            // head_sha가 있으면 GitHub REST API로 정확히 필터링.
            // 없으면 (구버전 호환) 기존 동작인 latest run 1건 조회.
            string args;
            if (!string.IsNullOrEmpty(_headSha))
            {
                args = $"api repos/{_repo}/actions/runs?head_sha={_headSha}&per_page=1 " +
                       "--jq \".workflow_runs[0]\"";
            }
            else
            {
                args = $"run list --repo {_repo} --limit 1 --json databaseId,status,conclusion --jq \".[0]\"";
            }

            var (code, output) = PrerequisiteChecker.RunGh(args);

            // gh api는 매칭 없으면 "null"을 반환. gh run list는 빈 출력.
            if (code != 0 || string.IsNullOrEmpty(output) || output.Trim() == "null")
                return new RunResult { status = "not_found" };

            // 간단 JSON 파싱 (Newtonsoft 의존 없이)
            // gh api는 databaseId가 아니라 "id" 필드를 반환하므로 둘 다 매칭.
            var result = new RunResult();
            var idMatch = Regex.Match(output, "\"(?:databaseId|id)\":\\s*(\\d+)");
            var statusMatch = Regex.Match(output, "\"status\":\\s*\"(\\w+)\"");
            var conclusionMatch = Regex.Match(output, "\"conclusion\":\\s*\"(\\w+)\"");

            if (idMatch.Success) result.runId = long.Parse(idMatch.Groups[1].Value);
            if (statusMatch.Success) result.status = statusMatch.Groups[1].Value;
            if (conclusionMatch.Success) result.conclusion = conclusionMatch.Groups[1].Value;

            return result;
        }

        static void HandleResult(RunResult result)
        {
            // head_sha에 해당하는 run이 아직 등록 안 됨 → 일정 시간 대기.
            if (result.status == "not_found")
            {
                if (string.IsNullOrEmpty(_headSha))
                {
                    // head_sha 필터 미사용인데 not_found면 그냥 무시하고 다음 폴링 대기.
                    return;
                }

                _foundAttempts++;
                if (_foundAttempts > MAX_FOUND_ATTEMPTS)
                {
                    Debug.LogWarning(
                        $"[SupaRun:Deploy] head_sha={_headSha[..7]}에 대한 workflow run을 60초 동안 찾지 못함. " +
                        "Actions가 설정되지 않은 repo이거나 trigger 조건 불일치 가능성. push는 성공으로 간주.");
                    CurrentStatus = Status.Success;
                    FetchCloudRunUrl();   // 워크플로우는 못 찾았어도 Cloud Run URL은 시도
                    EditorApplication.update -= Poll;
                }
                return;
            }

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
