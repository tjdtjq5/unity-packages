using System;
using System.Collections.Generic;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class DeployTab
    {
        readonly SupaRunDashboard _dashboard;

        enum DeployState { Idle, BuildVerifying, Deploying, Tracking, BuildSuccess, BuildFailed, PushFailed, Skipped }
        DeployState _state;
        string _progressMessage;
        string _errorMessage;
        Vector2 _logScroll;

        // dotnet 미설치 경고
        bool _showDotnetWarning;

        // 캐시 UI
        bool _showCacheDropdown;

        public DeployTab(SupaRunDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            EditorUI.DrawSectionHeader("Deploy", EditorUI.COL_WARN);
            GUILayout.Space(8);

            var settings = SupaRunSettings.Instance;

            if (!settings.IsGitHubConfigured)
            {
                DrawNotConfigured();
                return;
            }

            // Tracking 상태면 ActionsTracker 결과 반영
            if (_state == DeployState.Tracking)
                SyncTrackerState(settings);

            DrawCacheSection(settings);
            GUILayout.Space(8);
            DrawDeployArea(settings);
        }

        // ── Tracker 동기화 ──

        void SyncTrackerState(SupaRunSettings settings)
        {
            switch (ActionsTracker.CurrentStatus)
            {
                case ActionsTracker.Status.Success:
                    _state = DeployState.BuildSuccess;
                    _dashboard.ShowNotification("서버 배포 완료!",
                        EditorUI.NotificationType.Success);
                    DeployManager.RegisterCronJobs();
                    break;

                case ActionsTracker.Status.Failed:
                    _state = DeployState.BuildFailed;
                    _errorMessage = ActionsTracker.FailedLog;
                    _dashboard.ShowNotification("서버 빌드 실패",
                        EditorUI.NotificationType.Error);
                    break;

                case ActionsTracker.Status.Timeout:
                    _state = DeployState.BuildFailed;
                    _errorMessage = "5분 초과 — GitHub Actions에서 직접 확인하세요.";
                    break;
            }
        }

        // ── 캐시 ──

        void DrawCacheSection(SupaRunSettings settings)
        {
            EditorUI.DrawSubLabel("캐시");
            EditorUI.BeginBody();

            var alerts = ServerCacheHealthChecker.GetAlerts();

            // 활성 캐시 목록
            string toRemove = null;
            if (settings.enabledServerCaches.Count == 0)
            {
                EditorUI.DrawCellLabel("  캐시 없음 = 클린 빌드", 0, EditorUI.COL_WARN);
            }
            else
            {
                foreach (var cacheId in settings.enabledServerCaches)
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel($"  {ServerCacheTypes.GetLabel(cacheId)}", 120, EditorUI.COL_SUCCESS);

                    bool hasWarning = false;
                    foreach (var alert in alerts)
                    {
                        if (alert.AffectedCaches == null) continue;
                        foreach (var ac in alert.AffectedCaches)
                        {
                            if (ac == cacheId && alert.Level <= ServerCacheHealthChecker.Severity.Warning)
                            { hasWarning = true; break; }
                        }
                        if (hasWarning) break;
                    }

                    if (hasWarning)
                        EditorUI.DrawCellLabel("⚠", 20, EditorUI.COL_WARN);

                    EditorUI.FlexSpace();
                    if (EditorUI.DrawRemoveButton())
                        toRemove = cacheId;
                    EditorUI.EndRow();
                }
            }

            if (toRemove != null)
            {
                settings.enabledServerCaches.Remove(toRemove);
                settings.Save();
                ServerCacheHealthChecker.Invalidate();
            }

            // 캐시 추가 드롭다운
            GUILayout.Space(2);
            if (_showCacheDropdown)
            {
                bool hasAvailable = false;
                foreach (var c in ServerCacheTypes.All)
                {
                    if (settings.enabledServerCaches.Contains(c.Id)) continue;
                    hasAvailable = true;
                    if (EditorUI.DrawMiniButton($"{c.Label} — {c.Description}"))
                    {
                        settings.enabledServerCaches.Add(c.Id);
                        settings.Save();
                        _showCacheDropdown = false;
                        ServerCacheHealthChecker.Invalidate();
                    }
                }
                if (!hasAvailable)
                    EditorUI.DrawDescription("모든 캐시가 활성화되어 있습니다.", EditorUI.COL_MUTED);
                GUILayout.Space(2);
                if (EditorUI.DrawColorButton("닫기", EditorUI.COL_MUTED))
                    _showCacheDropdown = false;
            }
            else
            {
                if (EditorUI.DrawColorButton("+ 캐시 추가", EditorUI.COL_MUTED))
                    _showCacheDropdown = true;
            }

            // 마지막 배포 정보
            GUILayout.Space(4);
            var lastDate = ServerCacheHealthChecker.LastDeployDate;
            if (lastDate != null)
            {
                var ago = DateTime.UtcNow - lastDate.Value;
                string agoText;
                if (ago.TotalMinutes < 60) agoText = $"{(int)ago.TotalMinutes}분 전";
                else if (ago.TotalHours < 24) agoText = $"{(int)ago.TotalHours}시간 전";
                else agoText = $"{(int)ago.TotalDays}일 전";
                EditorUI.DrawCellLabel($"  마지막 배포: {agoText}", 0, EditorUI.COL_MUTED);
            }

            // 경고 표시
            bool hasAny = false;
            var recommendRemove = new HashSet<string>();

            foreach (var alert in alerts)
            {
                var affected = new List<string>();
                if (alert.AffectedCaches != null)
                {
                    foreach (var ac in alert.AffectedCaches)
                    {
                        if (settings.enabledServerCaches.Contains(ac))
                            affected.Add(ac);
                    }
                }

                string icon;
                Color color;
                switch (alert.Level)
                {
                    case ServerCacheHealthChecker.Severity.Error:
                        icon = "●"; color = EditorUI.COL_ERROR; break;
                    case ServerCacheHealthChecker.Severity.Warning:
                        icon = "⚠"; color = EditorUI.COL_WARN; break;
                    default:
                        icon = "ℹ"; color = EditorUI.COL_INFO; break;
                }

                if (affected.Count > 0)
                {
                    var cacheNames = string.Join(", ", affected.ConvertAll(ServerCacheTypes.GetLabel));
                    EditorUI.DrawCellLabel($"  {icon} {alert.Message} → {cacheNames} 해제 권장", 0, color);
                    foreach (var ac in affected) recommendRemove.Add(ac);
                    hasAny = true;
                }
                else if (alert.AffectedCaches == null || alert.AffectedCaches.Length == 0)
                {
                    EditorUI.DrawCellLabel($"  {icon} {alert.Message}", 0, color);
                    hasAny = true;
                }
            }

            // 정상 표시
            if (!hasAny && settings.enabledServerCaches.Count > 0 && lastDate != null)
                EditorUI.DrawCellLabel("  ✅ 모든 캐시 정상", 0, EditorUI.COL_SUCCESS);

            // 권장 해제 버튼
            if (recommendRemove.Count > 0)
            {
                GUILayout.Space(4);
                var removeLabels = string.Join(", ",
                    new List<string>(recommendRemove).ConvertAll(ServerCacheTypes.GetLabel));
                if (EditorUI.DrawColorButton($"권장: {removeLabels} 해제", EditorUI.COL_WARN, 24))
                {
                    foreach (var id in recommendRemove)
                        settings.enabledServerCaches.Remove(id);
                    settings.Save();
                    ServerCacheHealthChecker.Invalidate();
                }
            }

            EditorUI.EndBody();
        }

        // ── 미설정 ──

        void DrawNotConfigured()
        {
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "배포하려면 GitHub + GCP 설정이 필요합니다.\n\n" +
                "GitHub: 서버 코드 저장 + CI/CD\n" +
                "GCP: Cloud Run에 서버 배포");

            EditorUI.DrawInfoBox(
                new[] { "서버를 인터넷에 배포 가능", "다른 사람이 게임에 접속 가능", "테스트 단계 무료" },
                new[] { "Unity Play에서 LocalGameDB로 개발 가능", "설정에서 언제든 설정 가능" });

            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("지금 설정하기", SupaRunDashboard.COL_PRIMARY, 28))
                _dashboard.OpenSettings();

            EditorUI.EndBody();

            GUILayout.Space(8);
            EditorUI.DrawDescription(
                "배포 전에도 Unity Play 모드에서\nLocalGameDB로 모든 기능을 테스트할 수 있습니다.");
        }

        // ── 배포 영역 ──

        void DrawDeployArea(SupaRunSettings settings)
        {
            EditorUI.BeginBody();

            switch (_state)
            {
                case DeployState.Idle:
                    DrawIdle(settings);
                    break;

                case DeployState.BuildVerifying:
                    EditorUI.DrawLoading(true, "빌드 검증 중...");
                    break;

                case DeployState.Deploying:
                    EditorUI.DrawLoading(true, _progressMessage ?? "배포 중...");
                    break;

                case DeployState.Tracking:
                    DrawTracking(settings);
                    break;

                case DeployState.BuildSuccess:
                    DrawBuildSuccess(settings);
                    break;

                case DeployState.BuildFailed:
                    DrawBuildFailed(settings);
                    break;

                case DeployState.PushFailed:
                    DrawPushFailed();
                    break;

                case DeployState.Skipped:
                    DrawSkipped();
                    break;
            }

            EditorUI.EndBody();
        }

        void DrawIdle(SupaRunSettings settings)
        {
            EditorUI.DrawDescription(
                "[배포] 클릭 시:\n" +
                "1. [Table]/[Config]/[Service] 스캔\n" +
                "2. ASP.NET 서버 코드 자동 생성\n" +
                "3. 빌드 검증 (.NET SDK 설치 시)\n" +
                "4. GitHub에 push -> GitHub Actions -> Cloud Run");
            GUILayout.Space(8);

            // dotnet 미설치 경고 팝업
            if (_showDotnetWarning)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription(
                    ".NET SDK가 설치되어 있지 않습니다.\n" +
                    "빌드 검증 없이 배포합니다.\n" +
                    "서버 빌드 실패 시 Actions 로그를 확인하세요.", EditorUI.COL_WARN);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (EditorUI.DrawLinkButton(".NET SDK 설치하기"))
                        Application.OpenURL("https://dotnet.microsoft.com/download");
                    EditorUI.FlexSpace();
                    if (EditorUI.DrawColorButton("빌드 검증 없이 배포", EditorUI.COL_WARN, 28))
                        RunDeploy(settings);
                    GUILayout.Space(4);
                    if (EditorUI.DrawColorButton("취소", EditorUI.COL_MUTED, 28))
                        _showDotnetWarning = false;
                }
                EditorUI.EndBody();
                GUILayout.Space(8);
            }

            if (EditorUI.DrawColorButton("배포", EditorUI.COL_WARN, 32))
                RunDeploy(settings);
        }

        void DrawTracking(SupaRunSettings settings)
        {
            var elapsed = ActionsTracker.ElapsedSeconds;
            var min = (int)(elapsed / 60);
            var sec = (int)(elapsed % 60);

            EditorUI.DrawLoading(true, $"GitHub Actions 빌드 중... {min}:{sec:D2}");
            GUILayout.Space(4);

            var gh = PrerequisiteChecker.CheckGh();
            if (gh.LoggedIn)
            {
                var repo = $"{gh.Account}/{settings.githubRepoName}";
                if (EditorUI.DrawLinkButton("GitHub Actions 열기"))
                    Application.OpenURL(ActionsTracker.GetActionsUrl(repo));
            }

            _dashboard.Repaint();
        }

        void DrawBuildSuccess(SupaRunSettings settings)
        {
            var url = ActionsTracker.CloudRunUrl ?? settings.cloudRunUrl;

            EditorUI.DrawDescription("배포 성공!", EditorUI.COL_SUCCESS);

            if (!string.IsNullOrEmpty(url))
            {
                GUILayout.Space(4);
                EditorUI.DrawCellLabel($"  {url}", 0, EditorUI.COL_INFO);
                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (EditorUI.DrawLinkButton("Health 체크"))
                        Application.OpenURL($"{url}/health");
                    if (EditorUI.DrawLinkButton("Cloud Run 콘솔"))
                        Application.OpenURL("https://console.cloud.google.com/run");
                }
            }

            GUILayout.Space(8);
            if (EditorUI.DrawColorButton("다시 배포", EditorUI.COL_WARN, 28))
                _state = DeployState.Idle;
        }

        void DrawBuildFailed(SupaRunSettings settings)
        {
            EditorUI.DrawDescription("빌드 실패", EditorUI.COL_ERROR);
            GUILayout.Space(4);

            // 에러 로그 스크롤 영역
            if (!string.IsNullOrEmpty(_errorMessage))
                _logScroll = EditorUI.DrawLogArea(_errorMessage, _logScroll, 200, new Color(1f, 0.6f, 0.6f));

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(_errorMessage) && EditorUI.DrawColorButton("로그 복사", EditorUI.COL_MUTED, 28))
                {
                    GUIUtility.systemCopyBuffer = _errorMessage;
                    _dashboard.ShowNotification("클립보드에 복사됨", EditorUI.NotificationType.Info);
                }

                var gh = PrerequisiteChecker.CheckGh();
                if (gh.LoggedIn)
                {
                    var repo = $"{gh.Account}/{settings.githubRepoName}";
                    if (EditorUI.DrawLinkButton("전체 로그 보기"))
                        Application.OpenURL(ActionsTracker.GetActionsUrl(repo));
                }

                if (EditorUI.DrawColorButton("다시 배포", EditorUI.COL_WARN, 28))
                    _state = DeployState.Idle;
            }
        }

        void DrawPushFailed()
        {
            EditorUI.DrawDescription($"Push 실패: {_errorMessage}", EditorUI.COL_ERROR);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(_errorMessage) && EditorUI.DrawColorButton("로그 복사", EditorUI.COL_MUTED, 28))
                {
                    GUIUtility.systemCopyBuffer = _errorMessage;
                    _dashboard.ShowNotification("클립보드에 복사됨", EditorUI.NotificationType.Info);
                }

                if (EditorUI.DrawColorButton("다시 시도", EditorUI.COL_ERROR, 28))
                    _state = DeployState.Idle;
            }
        }

        void DrawSkipped()
        {
            EditorUI.DrawDescription("코드 변경 없음 — 배포 스킵됨", EditorUI.COL_INFO);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (EditorUI.DrawColorButton("강제 배포", EditorUI.COL_WARN, 28))
                {
                    // 변경 감지 캐시를 임시 해제하고 배포
                    var settings = SupaRunSettings.Instance;
                    settings.enabledServerCaches.Remove(ServerCacheTypes.Skip);
                    _state = DeployState.Idle;
                    RunDeploy(settings);
                    settings.enabledServerCaches.Add(ServerCacheTypes.Skip);
                    settings.Save();
                }

                if (EditorUI.DrawColorButton("확인", EditorUI.COL_MUTED, 28))
                    _state = DeployState.Idle;
            }
        }

        // ── 배포 실행 ──

        void RunDeploy(SupaRunSettings settings)
        {
            // dotnet 미설치 경고
            if (!DeployManager.IsDotnetAvailable() && !_showDotnetWarning)
            {
                _showDotnetWarning = true;
                _dashboard.Repaint();
                return;
            }
            _showDotnetWarning = false;

            // dotnet 있으면 빌드 검증 먼저 (비동기)
            if (DeployManager.IsDotnetAvailable())
            {
                _state = DeployState.BuildVerifying;
                _logScroll = Vector2.zero;
                _dashboard.Repaint();

                // 메인 스레드: 코드 생성 + 파일 쓰기
                var (tempDir, prepError) = DeployManager.PrepareBuildTest(settings);
                if (tempDir == null)
                {
                    _state = DeployState.PushFailed;
                    _errorMessage = prepError;
                    _dashboard.Repaint();
                    return;
                }

                // 백그라운드: dotnet build
                System.Threading.Tasks.Task.Run(() =>
                {
                    var (buildOk, buildOutput) = DeployManager.RunDotnetBuild(tempDir);
                    EditorApplication.delayCall += () =>
                    {
                        if (buildOk)
                            DoDeploy(settings);
                        else
                        {
                            _state = DeployState.PushFailed;
                            _errorMessage = "빌드 검증 실패:\n" + buildOutput;
                            _dashboard.ShowNotification("빌드 에러 - 배포 중단",
                                EditorUI.NotificationType.Error);
                            _dashboard.Repaint();
                        }
                    };
                });
                return;
            }

            // dotnet 없으면 바로 배포
            DoDeploy(settings);
        }

        void DoDeploy(SupaRunSettings settings)
        {
            _state = DeployState.Deploying;
            _progressMessage = "코드 스캔 중...";
            _logScroll = Vector2.zero;
            _dashboard.Repaint();

            DeployManager.Deploy(settings,
                onProgress: msg =>
                {
                    _progressMessage = msg;
                    _dashboard.Repaint();
                },
                onSuccess: () =>
                {
                    var gh = PrerequisiteChecker.CheckGh();
                    var repo = $"{gh.Account}/{settings.githubRepoName}";
                    ActionsTracker.StartTracking(repo);
                    _state = DeployState.Tracking;
                    _dashboard.ShowNotification("Push 완료! 빌드 추적 중...",
                        EditorUI.NotificationType.Info);
                    _dashboard.Repaint();
                },
                onFailed: error =>
                {
                    _state = DeployState.PushFailed;
                    _errorMessage = error;
                    _dashboard.ShowNotification($"Push 실패: {error}",
                        EditorUI.NotificationType.Error);
                    _dashboard.Repaint();
                },
                onSkipped: () =>
                {
                    _state = DeployState.Skipped;
                    _dashboard.ShowNotification("코드 변경 없음 — 배포 스킵됨",
                        EditorUI.NotificationType.Info);
                    _dashboard.Repaint();
                });
        }
    }
}
