using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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

        // Id 상수 생성 결과
        IdGenResult? _idGenResult;

        // 스키마 변경 요약 (ADR-0004)
        string _schemaSummary;

        public DeployTab(SupaRunDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            EditorGUILayout.LabelField("Deploy", EditorStyles.boldLabel);
            GUILayout.Space(8);

            var settings = SupaRunSettings.Instance;

            // 스키마 동기화 — GitHub 설정과 무관 (Supabase만 필요)
            DrawSchemaSection(settings);
            GUILayout.Space(8);

            // Id 상수 생성 — GitHub 설정과 무관 (Supabase만 필요)
            DrawIdConstantsSection();
            GUILayout.Space(8);

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

        // ── 스키마 동기화 (ADR-0004) ──

        void DrawSchemaSection(SupaRunSettings settings)
        {
            EditorGUILayout.LabelField("스키마 동기화", EditorStyles.miniLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "테이블·RLS 정책·어드민 메타데이터를 Supabase에 직접 반영합니다.\n" +
                "서버 재배포 없이 [SpecData] 변경이 어드민과 게임에 즉시 반영됩니다.",
                EditorStyles.wordWrappedMiniLabel);

            if (!settings.IsSupabaseConfigured)
            {
                EditorGUILayout.HelpBox("Supabase 설정이 필요합니다.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4);

            var auto = SchemaAutoSync.Enabled;
            var newAuto = EditorGUILayout.ToggleLeft("컴파일 후 자동 반영", auto);
            if (newAuto != auto) SchemaAutoSync.Enabled = newAuto;

            if (!newAuto)
            {
                // 처음 켜는 순간 [UserData] 테이블에 RLS 정책이 새로 생긴다.
                // 지금은 정책이 없어 anon 이 완전 차단인데, 그 문을 여는 변경이라 한 번은 사람이 눌러야 한다.
                EditorGUILayout.HelpBox(
                    "꺼져 있습니다. 처음 반영하면 [UserData] 테이블에 RLS 정책이 새로 생깁니다 " +
                    "— 게임 클라이언트도 같은 anon key를 쓰므로 여기서 연 문은 플레이어에게도 열립니다.\n" +
                    "\"지금 반영\"으로 한 번 적용하고 동작을 확인한 뒤 켜세요.",
                    MessageType.Warning);
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("변경 요약", GUILayout.Height(24)))
                _schemaSummary = SchemaAutoSync.Summarize();
            if (GUILayout.Button("지금 반영", GUILayout.Height(24)))
            {
                _schemaSummary = null;
                SchemaAutoSync.SyncNow().Forget();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_schemaSummary))
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField(_schemaSummary, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        // ── Id 상수 생성 ──

        void DrawIdConstantsSection()
        {
            EditorGUILayout.LabelField("Id 상수 생성", EditorStyles.miniLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "[SpecData] PK 값을 DB에서 읽어 {Name}Ids 상수로 생성합니다.\n" +
                "손 브리지 테이블([SkipIdConstants], 예: 스탯)은 제외됩니다.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);

            if (GUILayout.Button("Generate Id Constants", GUILayout.Height(28)))
            {
                var r = IdConstantGenerator.Generate();
                _idGenResult = r;
                _dashboard.ShowNotification(
                    r.Ok ? $"Id 상수 {r.FileCount}개 파일 생성"
                         : $"생성 {r.FileCount}개 · 에러 {r.Errors.Count}건",
                    r.Ok ? SupaRunUI.NotificationType.Success : SupaRunUI.NotificationType.Info);
            }

            if (_idGenResult.HasValue)
            {
                var r = _idGenResult.Value;
                GUILayout.Space(4);
                if (r.FileCount > 0)
                    EditorGUILayout.LabelField($"  ✅ {r.FileCount}개 생성 → {r.OutputDir}");
                foreach (var g in r.Generated)
                    EditorGUILayout.LabelField($"    • {g}");
                foreach (var s in r.Skipped)
                    EditorGUILayout.LabelField($"  ⏭ {s}");
                foreach (var e in r.Errors)
                    EditorGUILayout.LabelField($"  ● {e}");
            }

            EditorGUILayout.EndVertical();
        }

        // ── Tracker 동기화 ──

        void SyncTrackerState(SupaRunSettings settings)
        {
            switch (ActionsTracker.CurrentStatus)
            {
                case ActionsTracker.Status.Success:
                    _state = DeployState.BuildSuccess;
                    _dashboard.ShowNotification("서버 배포 완료!",
                        SupaRunUI.NotificationType.Success);
                    _ = DeployManager.RegisterCronJobs();
                    break;

                case ActionsTracker.Status.Failed:
                    _state = DeployState.BuildFailed;
                    _errorMessage = ActionsTracker.FailedLog;
                    _dashboard.ShowNotification("서버 빌드 실패",
                        SupaRunUI.NotificationType.Error);
                    break;

                case ActionsTracker.Status.Timeout:
                    _state = DeployState.BuildFailed;
                    _errorMessage = "10분 초과 — GitHub Actions에서 직접 확인하세요.";
                    break;
            }
        }

        // ── 캐시 ──

        void DrawCacheSection(SupaRunSettings settings)
        {
            EditorGUILayout.LabelField("캐시", EditorStyles.miniLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var alerts = ServerCacheHealthChecker.GetAlerts();

            // 활성 캐시 목록
            string toRemove = null;
            if (settings.enabledServerCaches.Count == 0)
            {
                EditorGUILayout.LabelField("  캐시 없음 = 클린 빌드");
            }
            else
            {
                foreach (var cacheId in settings.enabledServerCaches)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  {ServerCacheTypes.GetLabel(cacheId)}", GUILayout.Width(120));

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
                        EditorGUILayout.LabelField("⚠", GUILayout.Width(20));

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
                        toRemove = cacheId;
                    EditorGUILayout.EndHorizontal();
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
                    if (GUILayout.Button($"{c.Label} — {c.Description}", EditorStyles.miniButton))
                    {
                        settings.enabledServerCaches.Add(c.Id);
                        settings.Save();
                        _showCacheDropdown = false;
                        ServerCacheHealthChecker.Invalidate();
                    }
                }
                if (!hasAvailable)
                    EditorGUILayout.LabelField("모든 캐시가 활성화되어 있습니다.", EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(2);
                if (GUILayout.Button("닫기"))
                    _showCacheDropdown = false;
            }
            else
            {
                if (GUILayout.Button("+ 캐시 추가"))
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
                EditorGUILayout.LabelField($"  마지막 배포: {agoText}");
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
                switch (alert.Level)
                {
                    case ServerCacheHealthChecker.Severity.Error:
                        icon = "●"; break;
                    case ServerCacheHealthChecker.Severity.Warning:
                        icon = "⚠"; break;
                    default:
                        icon = "ℹ"; break;
                }

                if (affected.Count > 0)
                {
                    var cacheNames = string.Join(", ", affected.ConvertAll(ServerCacheTypes.GetLabel));
                    EditorGUILayout.LabelField($"  {icon} {alert.Message} → {cacheNames} 해제 권장");
                    foreach (var ac in affected) recommendRemove.Add(ac);
                    hasAny = true;
                }
                else if (alert.AffectedCaches == null || alert.AffectedCaches.Length == 0)
                {
                    EditorGUILayout.LabelField($"  {icon} {alert.Message}");
                    hasAny = true;
                }
            }

            // 정상 표시
            if (!hasAny && settings.enabledServerCaches.Count > 0 && lastDate != null)
                EditorGUILayout.LabelField("  ✅ 모든 캐시 정상");

            // 권장 해제 버튼
            if (recommendRemove.Count > 0)
            {
                GUILayout.Space(4);
                var removeLabels = string.Join(", ",
                    new List<string>(recommendRemove).ConvertAll(ServerCacheTypes.GetLabel));
                if (GUILayout.Button($"권장: {removeLabels} 해제", GUILayout.Height(24)))
                {
                    foreach (var id in recommendRemove)
                        settings.enabledServerCaches.Remove(id);
                    settings.Save();
                    ServerCacheHealthChecker.Invalidate();
                }
            }

            EditorGUILayout.EndVertical();
        }

        // ── 미설정 ──

        void DrawNotConfigured()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "배포하려면 GitHub + GCP 설정이 필요합니다.\n\n" +
                "GitHub: 서버 코드 저장 + CI/CD\n" +
                "GCP: Cloud Run에 서버 배포",
                EditorStyles.wordWrappedMiniLabel);

            SupaRunUI.DrawInfoBox(
                new[] { "서버를 인터넷에 배포 가능", "다른 사람이 게임에 접속 가능", "테스트 단계 무료" },
                new[] { "Unity Play에서 LocalGameDB로 개발 가능", "설정에서 언제든 설정 가능" });

            GUILayout.Space(4);
            if (GUILayout.Button("지금 설정하기", GUILayout.Height(28)))
                _dashboard.OpenSettings();

            EditorGUILayout.EndVertical();

            GUILayout.Space(8);
            EditorGUILayout.LabelField(
                "배포 전에도 Unity Play 모드에서\nLocalGameDB로 모든 기능을 테스트할 수 있습니다.",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ── 배포 영역 ──

        void DrawDeployArea(SupaRunSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            switch (_state)
            {
                case DeployState.Idle:
                    DrawIdle(settings);
                    break;

                case DeployState.BuildVerifying:
                    EditorGUILayout.HelpBox("빌드 검증 중...", MessageType.Info);
                    break;

                case DeployState.Deploying:
                    EditorGUILayout.HelpBox(_progressMessage ?? "배포 중...", MessageType.Info);
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

            EditorGUILayout.EndVertical();
        }

        void DrawIdle(SupaRunSettings settings)
        {
            EditorGUILayout.LabelField(
                "[배포] 클릭 시:\n" +
                "1. [UserData]/[SpecData]/[Service] 스캔\n" +
                "2. ASP.NET 서버 코드 자동 생성\n" +
                "3. 빌드 검증 (.NET SDK 설치 시)\n" +
                "4. GitHub에 push -> GitHub Actions -> Cloud Run",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);

            // dotnet 미설치 경고 팝업
            if (_showDotnetWarning)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    ".NET SDK가 설치되어 있지 않습니다.\n" +
                    "빌드 검증 없이 배포합니다.\n" +
                    "서버 빌드 실패 시 Actions 로그를 확인하세요.",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (EditorGUILayout.LinkButton(".NET SDK 설치하기"))
                        Application.OpenURL("https://dotnet.microsoft.com/download");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("빌드 검증 없이 배포", GUILayout.Height(28)))
                        RunDeploy(settings);
                    GUILayout.Space(4);
                    if (GUILayout.Button("취소", GUILayout.Height(28)))
                        _showDotnetWarning = false;
                }
                EditorGUILayout.EndVertical();
                GUILayout.Space(8);
            }

            if (GUILayout.Button("배포", GUILayout.Height(32)))
                RunDeploy(settings);
        }

        void DrawTracking(SupaRunSettings settings)
        {
            var elapsed = ActionsTracker.ElapsedSeconds;
            var min = (int)(elapsed / 60);
            var sec = (int)(elapsed % 60);

            EditorGUILayout.HelpBox($"GitHub Actions 빌드 중... {min}:{sec:D2}", MessageType.Info);
            GUILayout.Space(4);

            var gh = PrerequisiteChecker.CheckGh();
            if (gh.LoggedIn)
            {
                var repo = $"{gh.Account}/{settings.githubRepoName}";
                if (EditorGUILayout.LinkButton("GitHub Actions 열기"))
                    Application.OpenURL(ActionsTracker.GetActionsUrl(repo));
            }

            _dashboard.Repaint();
        }

        void DrawBuildSuccess(SupaRunSettings settings)
        {
            var url = ActionsTracker.CloudRunUrl ?? settings.cloudRunUrl;

            EditorGUILayout.LabelField("✓ 배포 성공!", EditorStyles.wordWrappedMiniLabel);

            if (!string.IsNullOrEmpty(url))
            {
                GUILayout.Space(4);
                EditorGUILayout.LabelField($"  {url}");
                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (EditorGUILayout.LinkButton("Health 체크"))
                        Application.OpenURL($"{url}/health");
                    if (EditorGUILayout.LinkButton("Cloud Run 콘솔"))
                        Application.OpenURL("https://console.cloud.google.com/run");
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button("다시 배포", GUILayout.Height(28)))
                _state = DeployState.Idle;
        }

        void DrawBuildFailed(SupaRunSettings settings)
        {
            EditorGUILayout.LabelField("✗ 빌드 실패", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);

            // 에러 로그 스크롤 영역
            if (!string.IsNullOrEmpty(_errorMessage))
            {
                var lines = _errorMessage.Split('\n').Length;
                var height = Mathf.Min(200, 14 * lines + 20);
                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(height));
                EditorGUILayout.SelectableLabel(_errorMessage, EditorStyles.wordWrappedMiniLabel,
                    GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(_errorMessage) && GUILayout.Button("로그 복사", GUILayout.Height(28)))
                {
                    GUIUtility.systemCopyBuffer = _errorMessage;
                    _dashboard.ShowNotification("클립보드에 복사됨", SupaRunUI.NotificationType.Info);
                }

                var gh = PrerequisiteChecker.CheckGh();
                if (gh.LoggedIn)
                {
                    var repo = $"{gh.Account}/{settings.githubRepoName}";
                    if (EditorGUILayout.LinkButton("전체 로그 보기"))
                        Application.OpenURL(ActionsTracker.GetActionsUrl(repo));
                }

                if (GUILayout.Button("다시 배포", GUILayout.Height(28)))
                    _state = DeployState.Idle;
            }
        }

        void DrawPushFailed()
        {
            EditorGUILayout.LabelField($"✗ Push 실패: {_errorMessage}", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(_errorMessage) && GUILayout.Button("로그 복사", GUILayout.Height(28)))
                {
                    GUIUtility.systemCopyBuffer = _errorMessage;
                    _dashboard.ShowNotification("클립보드에 복사됨", SupaRunUI.NotificationType.Info);
                }

                if (GUILayout.Button("다시 시도", GUILayout.Height(28)))
                    _state = DeployState.Idle;
            }
        }

        void DrawSkipped()
        {
            EditorGUILayout.LabelField("코드 변경 없음 — 배포 스킵됨", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("강제 배포", GUILayout.Height(28)))
                {
                    // 변경 감지 캐시를 임시 해제하고 배포
                    var settings = SupaRunSettings.Instance;
                    settings.enabledServerCaches.Remove(ServerCacheTypes.Skip);
                    _state = DeployState.Idle;
                    RunDeploy(settings);
                    settings.enabledServerCaches.Add(ServerCacheTypes.Skip);
                    settings.Save();
                }

                if (GUILayout.Button("확인", GUILayout.Height(28)))
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
                                SupaRunUI.NotificationType.Error);
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
                    ActionsTracker.StartTracking(repo, GitHubPusher.LastPushedSha);
                    _state = DeployState.Tracking;
                    _dashboard.ShowNotification("Push 완료! 빌드 추적 중...",
                        SupaRunUI.NotificationType.Info);
                    _dashboard.Repaint();
                },
                onFailed: error =>
                {
                    _state = DeployState.PushFailed;
                    _errorMessage = error;
                    _dashboard.ShowNotification($"Push 실패: {error}",
                        SupaRunUI.NotificationType.Error);
                    _dashboard.Repaint();
                },
                onSkipped: () =>
                {
                    _state = DeployState.Skipped;
                    _dashboard.ShowNotification("코드 변경 없음 — 배포 스킵됨",
                        SupaRunUI.NotificationType.Info);
                    _dashboard.Repaint();
                });
        }
    }
}
