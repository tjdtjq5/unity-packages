#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>CI/CD 탭: Release + 빌드 상태 + 히스토리 + 워크플로우</summary>
    public class CICDTab
    {
        readonly BuildAutomationWindow _window;

        string _nextVersion;
        string _currentBranch;
        string[] _remoteBranches;
        int? _unpushedCount;
        Vector2 _logScroll;
        bool _showHistory;

        public CICDTab(BuildAutomationWindow window)
        {
            _window = window;
            _nextVersion = ReleaseManager.SuggestNextVersion();
            _currentBranch = GitHelper.RunGit("branch --show-current");
        }

        public void OnDraw()
        {
            DrawCacheStatusSection();
            GUILayout.Space(4);
            DrawReleaseSection();
            GUILayout.Space(4);
            DrawBuildStatusSection();
            GUILayout.Space(4);
            DrawHistorySection();
            GUILayout.Space(4);
            DrawLinksSection();
        }

        // ── 캐시 상태 ──

        bool _showCacheDropdown;

        void DrawCacheStatusSection()
        {
            EditorUI.DrawSectionHeader("캐시", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            var s = BuildAutomationSettings.Instance;
            var alerts = CacheHealthChecker.GetAlerts();

            // 활성 캐시 목록 (X 버튼으로 개별 제거)
            string toRemove = null;
            if (s.enabledCaches.Count == 0)
            {
                EditorUI.DrawCellLabel("  캐시 없음 = 클린 빌드", 0, EditorUI.COL_WARN);
            }
            else
            {
                foreach (var cacheId in s.enabledCaches)
                {
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel($"  {CacheTypes.GetLabel(cacheId)}", 120, EditorUI.COL_SUCCESS);

                    // 해당 캐시에 경고가 있는지
                    bool hasWarning = false;
                    foreach (var alert in alerts)
                    {
                        if (alert.AffectedCaches == null) continue;
                        foreach (var ac in alert.AffectedCaches)
                        {
                            if (ac == cacheId && alert.Level <= CacheHealthChecker.Severity.Warning)
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
                s.enabledCaches.Remove(toRemove);
                s.Save();
                CacheHealthChecker.Invalidate();
            }

            // 캐시 추가 드롭다운
            GUILayout.Space(2);
            if (_showCacheDropdown)
            {
                bool hasAvailable = false;
                foreach (var c in CacheTypes.All)
                {
                    if (s.enabledCaches.Contains(c.Id)) continue;
                    hasAvailable = true;
                    if (EditorUI.DrawMiniButton($"{c.Label} — {c.Description}"))
                    {
                        s.enabledCaches.Add(c.Id);
                        s.Save();
                        _showCacheDropdown = false;
                        CacheHealthChecker.Invalidate();
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

            // 마지막 빌드 정보
            GUILayout.Space(4);
            var lastTag = CacheHealthChecker.LastBuildTag;
            var lastDate = CacheHealthChecker.LastBuildDate;
            if (lastTag != null && lastDate != null)
            {
                var ago = System.DateTime.UtcNow - lastDate.Value;
                string agoText;
                if (ago.TotalMinutes < 60) agoText = $"{(int)ago.TotalMinutes}분 전";
                else if (ago.TotalHours < 24) agoText = $"{(int)ago.TotalHours}시간 전";
                else agoText = $"{(int)ago.TotalDays}일 전";
                EditorUI.DrawCellLabel($"  마지막 빌드: v{lastTag} ({agoText})", 0, EditorUI.COL_MUTED);
            }

            // 경고 표시 — 활성 캐시에 영향 있는 것만
            bool hasAny = false;
            var recommendRemove = new System.Collections.Generic.HashSet<string>();

            foreach (var alert in alerts)
            {
                // 영향받는 캐시 중 활성화된 것만 필터
                var affected = new System.Collections.Generic.List<string>();
                if (alert.AffectedCaches != null)
                {
                    foreach (var ac in alert.AffectedCaches)
                    {
                        if (s.enabledCaches.Contains(ac))
                            affected.Add(ac);
                    }
                }

                // 영향 캐시가 없으면 일반 경고로만 표시
                string icon;
                Color color;
                switch (alert.Level)
                {
                    case CacheHealthChecker.Severity.Error:
                        icon = "🚫"; color = EditorUI.COL_ERROR; break;
                    case CacheHealthChecker.Severity.Warning:
                        icon = "⚠"; color = EditorUI.COL_WARN; break;
                    default:
                        icon = "ℹ"; color = EditorUI.COL_INFO; break;
                }

                if (affected.Count > 0)
                {
                    var cacheNames = string.Join(", ", affected.ConvertAll(CacheTypes.GetLabel));
                    EditorUI.DrawCellLabel($"  {icon} {alert.Message} → {cacheNames} 해제 권장", 0, color);
                    foreach (var ac in affected) recommendRemove.Add(ac);
                    hasAny = true;
                }
                else if (alert.AffectedCaches == null || alert.AffectedCaches.Length == 0)
                {
                    // 캐시와 무관한 일반 경고 (예: manifest file: 경로)
                    EditorUI.DrawCellLabel($"  {icon} {alert.Message}", 0, color);
                    hasAny = true;
                }
            }

            // 경고 없으면 정상
            if (!hasAny && s.enabledCaches.Count > 0 && lastTag != null)
                EditorUI.DrawCellLabel("  ✅ 모든 캐시 정상", 0, EditorUI.COL_SUCCESS);

            // 권장 해제 버튼
            if (recommendRemove.Count > 0)
            {
                GUILayout.Space(4);
                var removeLabels = string.Join(", ",
                    new System.Collections.Generic.List<string>(recommendRemove).ConvertAll(CacheTypes.GetLabel));
                if (EditorUI.DrawColorButton($"권장: {removeLabels} 해제", EditorUI.COL_WARN, 24))
                {
                    foreach (var id in recommendRemove)
                        s.enabledCaches.Remove(id);
                    s.Save();
                    CacheHealthChecker.Invalidate();
                }
            }

            EditorUI.EndBody();
        }

        // ── Release ──

        void DrawReleaseSection()
        {
            EditorUI.DrawSectionHeader("Release", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            var gh = GhChecker.Check();

            if (!gh.LoggedIn)
            {
                EditorUI.DrawCellLabel("  ⚠ gh CLI 로그인이 필요합니다", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                EditorUI.BeginRow();
                if (gh.Installed)
                {
                    if (EditorUI.DrawColorButton("GitHub 로그인", BuildAutomationWindow.COL_PRIMARY, 24))
                        GhChecker.RunGhLogin();
                }
                else
                {
                    if (EditorUI.DrawLinkButton("gh CLI 설치하기"))
                        Application.OpenURL("https://cli.github.com");
                }
                EditorUI.FlexSpace();
                EditorUI.EndRow();
                EditorUI.EndBody();
                return;
            }

            // yml 존재 확인
            bool ymlExists = WorkflowGenerator.WorkflowExists();
            if (!ymlExists)
            {
                EditorUI.DrawDescription(
                    "⚠ 워크플로우 파일이 없습니다. Settings → 워크플로우 재생성을 먼저 실행하세요.",
                    EditorUI.COL_WARN);
                EditorUI.EndBody();
                return;
            }

            // 릴리스 브랜치 + 현재 브랜치 표시
            var s = BuildAutomationSettings.Instance;
            if (string.IsNullOrEmpty(_currentBranch))
                _currentBranch = GitHelper.RunGit("branch --show-current");
            var currentBranch = _currentBranch;

            // 원격 브랜치 목록 (캐싱)
            if (_remoteBranches == null)
            {
                var raw = GitHelper.RunGit("branch -r --format=%(refname:short)");
                if (!string.IsNullOrEmpty(raw))
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var line in raw.Split('\n'))
                    {
                        var b = line.Trim();
                        if (string.IsNullOrEmpty(b) || b.Contains("HEAD")) continue;
                        // "origin/main" → "main"
                        if (b.StartsWith("origin/"))
                            b = b.Substring(7);
                        if (!list.Contains(b)) list.Add(b);
                    }
                    _remoteBranches = list.ToArray();
                }
                else
                {
                    _remoteBranches = new[] { "main" };
                }
            }

            // 드롭다운
            int selectedIdx = System.Array.IndexOf(_remoteBranches, s.releaseBranch);
            if (selectedIdx < 0) selectedIdx = 0;

            int newIdx = EditorUI.DrawPopup("릴리스 브랜치", selectedIdx, _remoteBranches);
            if (newIdx != selectedIdx && newIdx >= 0 && newIdx < _remoteBranches.Length)
            {
                s.releaseBranch = _remoteBranches[newIdx];
                s.Save();
            }

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel($"  현재 브랜치: {currentBranch}", 0,
                currentBranch == s.releaseBranch ? EditorUI.COL_SUCCESS : EditorUI.COL_WARN);
            EditorUI.EndRow();

            if (currentBranch != s.releaseBranch)
            {
                EditorUI.DrawDescription(
                    $"  ⚠ 릴리스 브랜치({s.releaseBranch})가 아닌 {currentBranch}에서 작업 중입니다.\n" +
                    $"     {s.releaseBranch}에 merge 후 Release하세요.",
                    EditorUI.COL_WARN);
            }

            GUILayout.Space(4);

            // 현재 버전
            var currentVersion = GitVersionResolver.GetVersion();
            EditorUI.DrawCellLabel($"  현재 버전: v{currentVersion}", 0, EditorUI.COL_MUTED);

            GUILayout.Space(4);

            // 새 버전 입력 + Release 버튼
            EditorUI.BeginRow();
            _nextVersion = EditorUI.DrawTextField("새 버전", _nextVersion);
            GUILayout.Space(4);

            // 미push 커밋 경고 (캐싱)
            if (_unpushedCount == null)
            {
                var ahead = GitHelper.RunGit($"rev-list origin/{s.releaseBranch}..HEAD --count");
                int.TryParse(ahead.Trim(), out int count);
                _unpushedCount = count;
            }

            var isPolling = BuildTracker.CurrentStatus == BuildTracker.Status.Polling;
            bool onReleaseBranch = currentBranch == s.releaseBranch;
            bool canRelease = !isPolling && !string.IsNullOrEmpty(_nextVersion)
                && ymlExists && onReleaseBranch;
            EditorUI.BeginDisabled(!canRelease);
            if (EditorUI.DrawColorButton("Release", EditorUI.COL_SUCCESS, 24))
            {
                // 사전 검증 (CreateRelease 내부에서도 하지만 UI 피드백용)
                var (success, error) = ReleaseManager.CreateRelease(_nextVersion);
                if (success)
                {
                    _window.ShowNotification(
                        $"v{_nextVersion.TrimStart('v')} Release 완료! 빌드가 시작됩니다.",
                        EditorUI.NotificationType.Success);
                    BuildTracker.StartTracking(_nextVersion);
                    BuildTracker.InvalidateHistory();
                    _currentBranch = null;
                    _unpushedCount = null;
                    _nextVersion = ReleaseManager.SuggestNextVersion();
                }
                else
                {
                    _window.ShowNotification($"Release 실패:\n{error}",
                        EditorUI.NotificationType.Error);
                }
            }
            EditorUI.EndDisabled();

            EditorUI.EndRow();

            // 경고 표시
            if (_unpushedCount > 0)
            {
                EditorUI.DrawDescription(
                    $"  ⚠ push되지 않은 커밋 {_unpushedCount}개가 있습니다. Release 시 함께 push됩니다.",
                    EditorUI.COL_WARN);
            }

            if (!canRelease)
            {
                if (!onReleaseBranch)
                    EditorUI.DrawDescription(
                        $"  {s.releaseBranch} 브랜치에서만 Release할 수 있습니다.", EditorUI.COL_WARN);
                else if (isPolling)
                    EditorUI.DrawDescription("  빌드가 진행 중입니다.", EditorUI.COL_MUTED);
                else if (string.IsNullOrEmpty(_nextVersion))
                    EditorUI.DrawDescription("  버전을 입력하세요.", EditorUI.COL_WARN);
            }

            // 빠른 버전 제안 버튼
            GUILayout.Space(2);
            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("  제안:", 40, EditorUI.COL_MUTED);
            if (EditorUI.DrawMiniButton($"patch ({ReleaseManager.SuggestNextVersion()})"))
                _nextVersion = ReleaseManager.SuggestNextVersion();
            if (EditorUI.DrawMiniButton($"minor ({ReleaseManager.SuggestNextMinor()})"))
                _nextVersion = ReleaseManager.SuggestNextMinor();
            EditorUI.FlexSpace();
            EditorUI.EndRow();

            EditorUI.EndBody();
        }

        // ── 빌드 상태 ──

        void DrawBuildStatusSection()
        {
            EditorUI.DrawSectionHeader("빌드 상태", BuildAutomationWindow.COL_PRIMARY);
            EditorUI.BeginBody();

            switch (BuildTracker.CurrentStatus)
            {
                case BuildTracker.Status.Idle:
                    EditorUI.DrawDescription("대기 중. Release 버튼을 누르면 빌드가 시작됩니다.",
                        EditorUI.COL_MUTED);
                    break;

                case BuildTracker.Status.Polling:
                    var elapsed = BuildTracker.ElapsedSeconds;
                    var min = (int)(elapsed / 60);
                    var sec = (int)(elapsed % 60);
                    EditorUI.DrawCellLabel(
                        $"  ● {BuildTracker.CurrentVersion} 빌드 중... ({min}분 {sec}초)",
                        0, EditorUI.COL_INFO);
                    EditorUI.DrawLoading(true, "GitHub Actions에서 빌드 진행 중");

                    EditorUI.BeginRow();
                    EditorUI.FlexSpace();
                    var repo = GitHelper.GetGitHubRepo();
                    if (!string.IsNullOrEmpty(repo))
                    {
                        if (EditorUI.DrawLinkButton("Actions에서 확인"))
                            Application.OpenURL($"https://github.com/{repo}/actions");
                    }
                    if (EditorUI.DrawColorButton("중지", EditorUI.COL_MUTED))
                        BuildTracker.Stop();
                    EditorUI.EndRow();

                    _window.Repaint(); // 타이머 업데이트용
                    break;

                case BuildTracker.Status.Success:
                    EditorUI.DrawCellLabel(
                        $"  ✓ {BuildTracker.CurrentVersion} 빌드 성공!",
                        0, EditorUI.COL_SUCCESS);
                    GUILayout.Space(4);
                    if (!string.IsNullOrEmpty(BuildTracker.ReleaseUrl))
                    {
                        if (EditorUI.DrawLinkButton("다운로드 (GitHub Releases)"))
                            Application.OpenURL(BuildTracker.ReleaseUrl);
                    }
                    break;

                case BuildTracker.Status.Failed:
                    EditorUI.DrawCellLabel(
                        $"  ✗ {BuildTracker.CurrentVersion} 빌드 실패",
                        0, EditorUI.COL_ERROR);
                    GUILayout.Space(4);

                    EditorUI.BeginRow();
                    repo = GitHelper.GetGitHubRepo();
                    if (!string.IsNullOrEmpty(repo))
                    {
                        if (EditorUI.DrawLinkButton("Actions에서 로그 확인"))
                            Application.OpenURL($"https://github.com/{repo}/actions");
                    }
                    EditorUI.EndRow();

                    if (!string.IsNullOrEmpty(BuildTracker.FailedLog))
                    {
                        GUILayout.Space(4);
                        _logScroll = EditorUI.DrawLogArea(
                            BuildTracker.FailedLog, _logScroll, 200, EditorUI.COL_ERROR);
                    }
                    break;

                case BuildTracker.Status.Timeout:
                    EditorUI.DrawCellLabel(
                        $"  ⏱ {BuildTracker.CurrentVersion} 타임아웃 (10분 초과)",
                        0, EditorUI.COL_WARN);
                    EditorUI.DrawDescription(
                        "GitHub Actions에서 직접 확인하세요.", EditorUI.COL_MUTED);
                    break;
            }

            EditorUI.EndBody();
        }

        // ── 히스토리 ──

        void DrawHistorySection()
        {
            if (EditorUI.DrawSectionFoldout(ref _showHistory, "최근 빌드",
                BuildAutomationWindow.COL_PRIMARY))
            {
                var history = BuildTracker.GetHistory(5);

                if (history.Length == 0)
                {
                    EditorUI.BeginBody();
                    EditorUI.DrawDescription("빌드 기록이 없습니다.", EditorUI.COL_MUTED);

                    if (!GhChecker.Check().LoggedIn)
                        EditorUI.DrawDescription("gh CLI 로그인 후 확인할 수 있습니다.", EditorUI.COL_WARN);

                    EditorUI.EndBody();
                }
                else
                {
                    EditorUI.BeginRow();
                    EditorUI.FlexSpace();
                    if (EditorUI.DrawMiniButton("새로고침"))
                        BuildTracker.InvalidateHistory();
                    EditorUI.EndRow();

                    foreach (var entry in history)
                    {
                        EditorUI.BeginBody();
                        EditorUI.BeginRow();

                        bool ok = entry.Status == "success";
                        string icon = ok ? "✓" : "✗";
                        var color = ok ? EditorUI.COL_SUCCESS : EditorUI.COL_ERROR;

                        EditorUI.DrawCellLabel($"  {icon} {entry.Version}", 0, color);
                        EditorUI.DrawCellLabel(entry.Date ?? "", 90, EditorUI.COL_MUTED);

                        var repo = GitHelper.GetGitHubRepo();
                        if (!string.IsNullOrEmpty(repo))
                        {
                            if (ok)
                            {
                                if (EditorUI.DrawMiniButton("다운로드"))
                                    Application.OpenURL(
                                        $"https://github.com/{repo}/releases");
                            }
                            else
                            {
                                if (EditorUI.DrawMiniButton("로그"))
                                    Application.OpenURL(
                                        $"https://github.com/{repo}/actions/runs/{entry.RunId}");
                            }
                        }

                        EditorUI.EndRow();
                        EditorUI.EndBody();
                    }
                }
            }
        }

        // ── 링크 ──

        void DrawLinksSection()
        {
            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo)) return;

            EditorUI.BeginBody();
            EditorUI.BeginRow();
            if (EditorUI.DrawLinkButton("GitHub Actions"))
                Application.OpenURL($"https://github.com/{repo}/actions");
            if (EditorUI.DrawLinkButton("Releases"))
                Application.OpenURL($"https://github.com/{repo}/releases");
            EditorUI.FlexSpace();
            EditorUI.EndRow();
            EditorUI.EndBody();
        }
    }
}
#endif
