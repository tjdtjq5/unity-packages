#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.Codemagic.Editor.Codemagic;
using Tjdtjq5.Codemagic.Editor.Git;
using Tjdtjq5.Codemagic.Editor.Manifest;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Dashboard
{
    /// <summary>
    /// Phase 1 MVP 빌드 다이얼로그.
    /// 빌드 옵션 선택 → yaml 생성 → manifest swap → commit → push → Codemagic 트리거를 한 흐름으로 처리한다.
    /// finally에서 manifest 워킹트리는 100% file:로 복원해 로컬 개발 모드를 유지한다.
    /// </summary>
    public sealed class BuildDialog : EditorWindow
    {
        // ── UI 색 ──────────────────────────────────────────────────────────
        static readonly Color COL_PRIMARY = new(0.20f, 0.65f, 1f);

        // ── 인스턴스 / 캐시 옵션 ───────────────────────────────────────────
        static readonly string[] InstanceOptions =
        {
            "linux_x2", "linux_x4", "mac_mini", "mac_pro",
        };

        // ── 트리거 모드 ────────────────────────────────────────────────────
        enum TriggerMode { Tag, Api }
        static readonly string[] TriggerOptions =
        {
            "Git tag (yaml triggering 매칭)",
            "Codemagic API (즉시 트리거)",
        };

        // ── 화면 상태 ──────────────────────────────────────────────────────
        Vector2 _scroll;
        bool _isBuilding;
        string _statusMessage;
        EditorUI.NotificationType _statusType = EditorUI.NotificationType.Info;

        // ── 옵션 (yaml + 다이얼로그용 추가 필드) ──────────────────────────
        BuildYamlOptions _yamlOptions;
        int _instanceIndex;
        string _commitMessage = "ci(codemagic): update build config";
        TriggerMode _triggerMode = TriggerMode.Tag;
        int _triggerIndex;
        string _tagName = "";

        // ── 사전 점검 캐시 (OnEnable + 수동 새로고침) ──────────────────────
        bool _hasToken;
        string _appId;
        string _githubRepo;
        string _repoRoot;
        List<string> _dirtyFiles = new();
        List<ManifestModeSwapper.LocalPackage> _localPackages = new();

        // ── 메뉴 등록 ──────────────────────────────────────────────────────

        [MenuItem("Tjdtjq/Codemagic/Build Dialog", priority = 110)]
        public static void Open()
        {
            var win = GetWindow<BuildDialog>("Codemagic Build");
            win.minSize = new Vector2(520, 720);
            win.Show();
        }

        // ── 라이프사이클 ───────────────────────────────────────────────────

        void OnEnable()
        {
            GitHelpers.InvalidateCache();
            InitializeOptions();
            RefreshPreflight();
        }

        void InitializeOptions()
        {
            _yamlOptions = new BuildYamlOptions
            {
                BuildAndroid = true,
                BuildIOS = false,
                InstanceType = InstanceOptions[0],
                MaxBuildDuration = 90,
                ClearLibraryCache = false,
                ClearGradleCache = false,
                CacheReason = "",
                UnityVersion = ExtractCleanUnityVersion(Application.unityVersion),
                BuildName = SanitizeBuildName(PlayerSettings.productName),
                TagPattern = "v*",
                BuilderVersion = "v4",
                BuildMethod = "Tjdtjq5.Codemagic.Editor.Build.CodemagicBuildScript.PerformAndroidBuild",
            };
            _yamlOptions.ImageTag = $"ubuntu-{_yamlOptions.UnityVersion}-android-3";

            // 알림 수신자: ProjectSettings에서 복사 (yaml 생성 시점에 사용).
            var settings = CodemagicProjectSettings.Instance;
            _yamlOptions.NotificationRecipients = settings.NotificationRecipients != null
                ? new List<string>(settings.NotificationRecipients)
                : new List<string>();
            _yamlOptions.NotifyOnSuccess = settings.NotifyOnSuccess;
            _yamlOptions.NotifyOnFailure = settings.NotifyOnFailure;

            _instanceIndex = 0;
            _triggerMode = TriggerMode.Tag;
            _triggerIndex = 0;
            _tagName = "";
        }

        void RefreshPreflight()
        {
            _hasToken = !string.IsNullOrEmpty(SecretStore.CodemagicToken);
            _appId = CodemagicProjectSettings.Instance.CodemagicAppId;
            _githubRepo = GitHelpers.GetGitHubRepo();
            _repoRoot = GitHelpers.GetRepoRoot();
            _dirtyFiles = GitHelpers.GetDirtyFiles("Packages/manifest.json");
            _localPackages = ManifestModeSwapper.DetectLocalPackages();
        }

        // ── GUI ────────────────────────────────────────────────────────────

        void OnGUI()
        {
            EditorUI.DrawWindowBackground(position);
            EditorUI.DrawWindowHeader("Codemagic Build", "v0.1.0", COL_PRIMARY);

            EditorUI.DrawNotificationBar(ref _statusMessage, _statusType);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawTargetSection();
            DrawInstanceSection();
            DrawCacheSection();
            DrawBuildOptionsSection();
            DrawNotificationSection();
            DrawPreflightSection();
            DrawTriggerSection();
            DrawPreviewSection();

            EditorGUILayout.EndScrollView();

            DrawActionRow();
        }

        // ── 1. 빌드 타겟 ───────────────────────────────────────────────────

        void DrawTargetSection()
        {
            EditorUI.DrawSectionHeader("빌드 타겟", COL_PRIMARY);
            EditorUI.BeginBody();

            _yamlOptions.BuildAndroid = EditorGUILayout.ToggleLeft(
                new GUIContent("Android", "Android 빌드 — Phase 1 기본"),
                _yamlOptions.BuildAndroid);

            EditorUI.BeginDisabled(true);
            _yamlOptions.BuildIOS = EditorGUILayout.ToggleLeft(
                new GUIContent("iOS  (v0.2.0+ 예정)", "Phase 1에서 비활성화"),
                _yamlOptions.BuildIOS);
            EditorUI.EndDisabled();

            EditorUI.EndBody();
        }

        // ── 2. 인스턴스 ────────────────────────────────────────────────────

        void DrawInstanceSection()
        {
            EditorUI.DrawSectionHeader("인스턴스", COL_PRIMARY);
            EditorUI.BeginBody();

            _instanceIndex = EditorUI.DrawPopup("Instance Type", _instanceIndex, InstanceOptions,
                "linux_x2 = 16GB, linux_x4 = 32GB. mac_*는 iOS 빌드용");
            _yamlOptions.InstanceType = InstanceOptions[_instanceIndex];

            _yamlOptions.MaxBuildDuration = EditorGUILayout.IntSlider(
                new GUIContent("Max Build Duration (min)", "최대 빌드 시간(분). 초과 시 강제 종료"),
                _yamlOptions.MaxBuildDuration, 15, 180);

            EditorUI.EndBody();
        }

        // ── 3. 캐시 (수동) ─────────────────────────────────────────────────

        void DrawCacheSection()
        {
            EditorUI.DrawSectionHeader("캐시 (Phase 1 — 수동)", COL_PRIMARY);
            EditorUI.BeginBody();

            _yamlOptions.ClearLibraryCache = EditorGUILayout.ToggleLeft(
                new GUIContent("Library 클린", "Library 폴더 삭제 — 큰 변경 후 사용"),
                _yamlOptions.ClearLibraryCache);

            _yamlOptions.ClearGradleCache = EditorGUILayout.ToggleLeft(
                new GUIContent("Gradle 클린  (~/.gradle)", "Gradle 캐시 삭제 — Android plugin 변경 후"),
                _yamlOptions.ClearGradleCache);

            var reason = EditorUI.DrawTextField("사유 (선택, 200자)", _yamlOptions.CacheReason ?? "",
                "캐시를 클린하는 이유를 빌드 로그에 남깁니다");
            if (reason != null && reason.Length > 200) reason = reason.Substring(0, 200);
            _yamlOptions.CacheReason = reason;

            EditorUI.DrawDescription("v0.2.0에서 자동 결정 룰 도입 예정", EditorUI.COL_MUTED);

            EditorUI.EndBody();
        }

        // ── 4. 빌드 옵션 ───────────────────────────────────────────────────

        void DrawBuildOptionsSection()
        {
            EditorUI.DrawSectionHeader("빌드 옵션", COL_PRIMARY);
            EditorUI.BeginBody();

            _yamlOptions.BuildName = EditorUI.DrawTextField("BuildName", _yamlOptions.BuildName,
                "빌드 산출물 파일명 (예: SurvivorsDuo)");
            _yamlOptions.TagPattern = EditorUI.DrawTextField("TagPattern", _yamlOptions.TagPattern,
                "어떤 git tag에 빌드를 트리거할지 (예: v*)");
            _yamlOptions.UnityVersion = EditorUI.DrawTextField("UnityVersion", _yamlOptions.UnityVersion,
                "Codemagic 빌드 이미지가 사용할 Unity 버전");
            _yamlOptions.ImageTag = EditorUI.DrawTextField("ImageTag", _yamlOptions.ImageTag,
                "unityci/editor 도커 이미지 태그");
            _yamlOptions.BuilderVersion = EditorUI.DrawTextField("BuilderVersion", _yamlOptions.BuilderVersion,
                "GameCI unity-builder 브랜치/태그 (예: v4)");
            _yamlOptions.BuildMethod = EditorUI.DrawTextField("BuildMethod", _yamlOptions.BuildMethod,
                "Unity 빌드 메서드 (FullyQualifiedName.Method)");

            EditorUI.EndBody();
        }

        // ── 5. 알림 (읽기) ─────────────────────────────────────────────────

        void DrawNotificationSection()
        {
            EditorUI.DrawSectionHeader("알림", COL_PRIMARY);
            EditorUI.BeginBody();

            var settings = CodemagicProjectSettings.Instance;
            var recipients = settings.NotificationRecipients;
            if (recipients == null || recipients.Count == 0)
            {
                EditorUI.DrawDescription("등록된 수신자 없음. CodemagicProjectSettings.asset에서 추가",
                    EditorUI.COL_MUTED);
            }
            else
            {
                foreach (var r in recipients)
                {
                    if (string.IsNullOrWhiteSpace(r)) continue;
                    EditorUI.DrawCellLabel($"• {r}", color: EditorUI.COL_INFO);
                }
                EditorUI.DrawDescription(
                    $"성공 알림: {(settings.NotifyOnSuccess ? "ON" : "OFF")}, " +
                    $"실패 알림: {(settings.NotifyOnFailure ? "ON" : "OFF")}",
                    EditorUI.COL_MUTED);
            }

            EditorUI.DrawDescription("수신자 변경은 CodemagicProjectSettings.asset에서",
                EditorUI.COL_MUTED);

            EditorUI.EndBody();
        }

        // ── 6. 사전 점검 ───────────────────────────────────────────────────

        void DrawPreflightSection()
        {
            EditorUI.DrawSectionHeader("사전 점검", COL_PRIMARY);
            EditorUI.BeginBody();

            DrawCheck(_hasToken, "Codemagic 토큰 등록됨", "Codemagic 토큰 미등록 — Setup 필요");
            DrawCheck(!string.IsNullOrEmpty(_appId),
                $"App ID: {Truncate(_appId, 32)}",
                "Codemagic App ID 미등록 — ProjectSettings 확인");
            DrawCheck(!string.IsNullOrEmpty(_repoRoot), "Git 리포지토리 감지됨", "Git 리포지토리가 아님");
            DrawCheck(!string.IsNullOrEmpty(_githubRepo),
                $"Git remote: {_githubRepo}",
                "Git remote 미설정");

            // 미커밋 변경
            if (_dirtyFiles.Count > 0)
            {
                EditorUI.DrawCellLabel($"! 미커밋 변경: {_dirtyFiles.Count}개 (자동 commit 진행)",
                    color: EditorUI.COL_WARN);
            }
            else
            {
                EditorUI.DrawCellLabel("✓ 워킹트리 클린", color: EditorUI.COL_SUCCESS);
            }

            // manifest.json file: 패키지
            var swappable = _localPackages.Where(p => p.HasBackup).ToList();
            var unswappable = _localPackages.Where(p => !p.HasBackup).ToList();
            if (unswappable.Count > 0)
            {
                EditorUI.DrawCellLabel(
                    $"x manifest.json file:: {unswappable.Count}개 (백업 URL 없음 → 자동 처리 불가)",
                    color: EditorUI.COL_ERROR);
                foreach (var p in unswappable)
                    EditorUI.DrawCellLabel($"   • {p.PackageName}", color: EditorUI.COL_ERROR);
            }
            if (swappable.Count > 0)
            {
                EditorUI.DrawCellLabel(
                    $"! manifest.json file:: {swappable.Count}개 (자동 swap 진행)",
                    color: EditorUI.COL_WARN);
            }

            EditorUI.BeginRow();
            GUILayout.FlexibleSpace();
            if (EditorUI.DrawMiniButton("새로고침"))
            {
                GitHelpers.InvalidateCache();
                RefreshPreflight();
            }
            EditorUI.EndRow();

            EditorUI.EndBody();
        }

        static void DrawCheck(bool ok, string okText, string failText)
        {
            EditorUI.DrawCellLabel(ok ? $"✓ {okText}" : $"x {failText}",
                color: ok ? EditorUI.COL_SUCCESS : EditorUI.COL_ERROR);
        }

        // ── 7. 트리거 ──────────────────────────────────────────────────────

        void DrawTriggerSection()
        {
            EditorUI.DrawSectionHeader("트리거 방식", COL_PRIMARY);
            EditorUI.BeginBody();

            _triggerIndex = EditorUI.DrawPopup("Trigger Mode", _triggerIndex, TriggerOptions,
                "Tag = git tag push로 자동 트리거 (yaml triggering 매칭). API = 즉시 트리거");
            _triggerMode = _triggerIndex == 0 ? TriggerMode.Tag : TriggerMode.Api;

            if (_triggerMode == TriggerMode.Tag)
            {
                _tagName = EditorUI.DrawTextField("Tag Name (필수)", _tagName,
                    "예: v0.1.0 — TagPattern과 매칭되어야 빌드 트리거됨");
                EditorUI.DrawDescription(
                    "이 태그를 push하면 Codemagic이 yaml의 triggering.tag_patterns 매칭으로 자동 빌드",
                    EditorUI.COL_MUTED);
            }
            else
            {
                EditorUI.DrawDescription(
                    "현재 브랜치 + workflow 'android-build'에 즉시 빌드 요청을 보냅니다 (태그 생성 없음)",
                    EditorUI.COL_MUTED);
            }

            EditorUI.EndBody();
        }

        // ── 8. 빌드 미리보기 ──────────────────────────────────────────────

        void DrawPreviewSection()
        {
            EditorUI.DrawSectionHeader("빌드 미리보기", COL_PRIMARY);
            EditorUI.BeginBody();

            var (newYaml, hint) = TryGenerateYamlPreview();
            EditorUI.DrawDescription(hint, EditorUI.COL_MUTED);

            _commitMessage = EditorUI.DrawTextField("Commit Message", _commitMessage,
                "yaml + manifest.json을 함께 커밋할 때 사용할 메시지");

            EditorUI.EndBody();
        }

        (string yaml, string hint) TryGenerateYamlPreview()
        {
            try
            {
                var yaml = new CodemagicYamlGenerator().Generate(_yamlOptions);
                var path = Path.Combine(_repoRoot ?? "", "codemagic.yaml");
                if (string.IsNullOrEmpty(_repoRoot) || !File.Exists(path))
                    return (yaml, "기존 yaml 없음 — 신규 생성");

                var existing = File.ReadAllText(path);
                if (existing == yaml)
                    return (yaml, "기존 yaml과 동일 — 변경 없음 (yaml 커밋 생략)");

                var diff = CountChangedLines(existing, yaml);
                return (yaml, $"기존 yaml 대비 약 {diff} 라인 변경 (full diff는 v0.2.0)");
            }
            catch (Exception ex)
            {
                return (null, $"yaml 미리보기 실패: {ex.Message}");
            }
        }

        static int CountChangedLines(string a, string b)
        {
            var la = (a ?? "").Split('\n');
            var lb = (b ?? "").Split('\n');
            var setA = new HashSet<string>(la);
            int changed = 0;
            foreach (var line in lb)
                if (!setA.Contains(line)) changed++;
            // 두 방향 합산 (대략적인 dist)
            var setB = new HashSet<string>(lb);
            foreach (var line in la)
                if (!setB.Contains(line)) changed++;
            return changed;
        }

        // ── 9. 액션 행 ─────────────────────────────────────────────────────

        void DrawActionRow()
        {
            GUILayout.Space(4);

            EditorUI.BeginRow();

            EditorUI.BeginDisabled(_isBuilding || !CanStartBuild());
            if (EditorUI.DrawColorButton(_isBuilding ? "진행 중..." : "빌드 시작",
                    EditorUI.COL_INFO, height: 32))
            {
                StartBuildAsync().Forget();
            }
            EditorUI.EndDisabled();

            if (EditorUI.DrawColorButton("취소", EditorUI.COL_MUTED, height: 32))
            {
                if (!_isBuilding)
                    Close();
            }

            EditorUI.EndRow();
        }

        bool CanStartBuild()
        {
            if (!_hasToken) return false;
            if (string.IsNullOrEmpty(_appId)) return false;
            if (string.IsNullOrEmpty(_githubRepo)) return false;
            if (string.IsNullOrEmpty(_repoRoot)) return false;
            if (_localPackages.Any(p => !p.HasBackup)) return false;
            if (_triggerMode == TriggerMode.Tag && string.IsNullOrWhiteSpace(_tagName)) return false;
            return true;
        }

        // ── 10. 빌드 흐름 ──────────────────────────────────────────────────

        async UniTaskVoid StartBuildAsync()
        {
            _isBuilding = true;
            _statusMessage = null;
            Repaint();

            // 사전 점검 (강제 차단)
            var blocker = ValidateHardRequirements();
            if (blocker != null)
            {
                EditorUtility.DisplayDialog("Codemagic 빌드", blocker, "확인");
                _isBuilding = false;
                Repaint();
                return;
            }

            // manifest 분류
            var swappable = _localPackages.Where(p => p.HasBackup).ToList();
            var unswappable = _localPackages.Where(p => !p.HasBackup).ToList();
            if (unswappable.Count > 0)
            {
                EditorUtility.DisplayDialog(
                    "Codemagic 빌드",
                    $"{unswappable.Count}개 패키지가 file: 경로지만 백업 URL이 없어 자동 처리할 수 없습니다.\n" +
                    "수동으로 git URL로 복원한 뒤 다시 시도하세요.",
                    "확인");
                _isBuilding = false;
                Repaint();
                return;
            }

            // 1) 워킹트리 swap + 빌드 흐름 — 모두 try로 감싸 finally가 SwapToLocal 보장
            try
            {
                if (swappable.Count > 0)
                    ManifestModeSwapper.SwapToRemote(swappable);

                await ExecuteBuildAsync(swappable);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Codemagic] 빌드 흐름 예외: {ex.Message}");
                ShowError($"빌드 흐름 예외: {ex.Message}");
            }
            finally
            {
                // 100% 워킹트리 복원 — 로컬 개발 모드 유지
                if (swappable.Count > 0)
                {
                    try { ManifestModeSwapper.SwapToLocal(swappable); }
                    catch (Exception ex) { Debug.LogError($"[Codemagic] manifest 복원 실패: {ex.Message}"); }
                }

                _isBuilding = false;
                RefreshPreflight();
                Repaint();
            }
        }

        string ValidateHardRequirements()
        {
            if (string.IsNullOrEmpty(SecretStore.CodemagicToken))
                return "Codemagic 토큰이 등록되지 않았습니다. Setup을 먼저 진행하세요.";
            if (string.IsNullOrEmpty(CodemagicProjectSettings.Instance.CodemagicAppId))
                return "Codemagic App ID가 설정되지 않았습니다. CodemagicProjectSettings.asset 확인.";
            if (string.IsNullOrEmpty(GitHelpers.GetRepoRoot()))
                return "Git 리포지토리를 찾을 수 없습니다. git init 후 재시도.";
            if (string.IsNullOrEmpty(GitHelpers.GetGitHubRepo()))
                return "Git remote 'origin'에 GitHub URL이 없습니다.";
            if (_triggerMode == TriggerMode.Tag && string.IsNullOrWhiteSpace(_tagName))
                return "Tag 모드에서는 Tag Name이 필수입니다.";
            return null;
        }

        async UniTask ExecuteBuildAsync(List<ManifestModeSwapper.LocalPackage> swappable)
        {
            // 2) 미커밋 변경 자동 commit (manifest.json은 swap 결과라 제외)
            var dirty = GitHelpers.GetDirtyFiles("Packages/manifest.json");
            if (dirty.Count > 0)
            {
                const int previewMax = 10;
                var preview = string.Join("\n  ", dirty.Take(previewMax));
                if (dirty.Count > previewMax)
                    preview += $"\n  ... 외 {dirty.Count - previewMax}개";

                bool ok = EditorUtility.DisplayDialog(
                    "미커밋 변경",
                    $"커밋되지 않은 변경 {dirty.Count}개가 있습니다:\n\n  {preview}\n\n" +
                    "자동으로 커밋한 뒤 빌드를 진행할까요?",
                    "커밋 후 진행",
                    "취소");
                if (!ok)
                {
                    ShowInfo("사용자가 빌드를 취소했습니다.");
                    return;
                }

                GitHelpers.RunGit("add -A");
                var (code, output) = GitHelpers.RunGitWithCode("commit -m \"WIP: pre-build\"");
                if (code != 0)
                {
                    ShowError($"자동 커밋 실패: {output}");
                    return;
                }
            }

            // 3) 원격 최신화 (실패해도 계속)
            var branch = GitHelpers.RunGit("rev-parse --abbrev-ref HEAD");
            if (string.IsNullOrEmpty(branch))
            {
                ShowError("현재 브랜치를 확인할 수 없습니다.");
                return;
            }
            GitHelpers.RunGitWithCode($"pull origin {branch} --rebase");

            // 4) yaml 생성
            string yaml;
            try
            {
                yaml = new CodemagicYamlGenerator().Generate(_yamlOptions);
            }
            catch (Exception ex)
            {
                ShowError($"yaml 생성 실패: {ex.Message}");
                return;
            }

            var repoRoot = GitHelpers.GetRepoRoot();
            var yamlPath = Path.Combine(repoRoot, "codemagic.yaml");
            bool yamlChanged = true;
            if (File.Exists(yamlPath))
                yamlChanged = File.ReadAllText(yamlPath) != yaml;
            if (yamlChanged)
                File.WriteAllText(yamlPath, yaml);

            // 5) commit + push (yaml 변경 또는 manifest swap된 경우)
            bool needsCommit = yamlChanged || swappable.Count > 0;
            if (needsCommit)
            {
                if (yamlChanged) GitHelpers.RunGit("add codemagic.yaml");
                if (swappable.Count > 0) GitHelpers.RunGit("add Packages/manifest.json");

                var msg = string.IsNullOrWhiteSpace(_commitMessage)
                    ? "ci(codemagic): update build config"
                    : _commitMessage.Trim();
                var quoted = msg.Replace("\"", "\\\"");
                GitHelpers.RunGitWithCode($"commit -m \"{quoted}\" --allow-empty");

                var (pushCode, pushOutput) = GitHelpers.RunGitWithCode($"push origin {branch}");
                if (pushCode != 0)
                {
                    ShowError($"git push 실패: {pushOutput}");
                    return;
                }
            }

            // 6) 트리거
            if (_triggerMode == TriggerMode.Tag)
            {
                await TriggerByTagAsync(branch);
            }
            else
            {
                await TriggerByApiAsync(branch);
            }
        }

        async UniTask TriggerByTagAsync(string branch)
        {
            // 동일 태그 존재 시 차단
            var tag = _tagName.Trim();
            var existing = GitHelpers.RunGit($"tag -l {tag}");
            if (!string.IsNullOrEmpty(existing))
            {
                ShowError($"태그 '{tag}'이 이미 존재합니다. 다른 이름을 사용하세요.");
                return;
            }

            var (tagCode, tagOutput) = GitHelpers.RunGitWithCode($"tag {tag}");
            if (tagCode != 0)
            {
                ShowError($"태그 생성 실패: {tagOutput}");
                return;
            }

            var (pushCode, pushOutput) = GitHelpers.RunGitWithCode($"push origin {tag}");
            if (pushCode != 0)
            {
                ShowError($"태그 push 실패: {pushOutput}");
                return;
            }

            await UniTask.Yield();

            var appId = CodemagicProjectSettings.Instance.CodemagicAppId;
            EditorUtility.DisplayDialog(
                "Codemagic 빌드",
                $"태그 '{tag}' push 완료.\n" +
                $"yaml의 triggering.tag_patterns 매칭으로 Codemagic이 자동 빌드를 시작합니다.\n\n" +
                $"https://codemagic.io/apps/{appId}",
                "확인");
            ShowSuccess($"태그 '{tag}' push 완료. 곧 Codemagic 빌드가 시작됩니다.");
        }

        async UniTask TriggerByApiAsync(string branch)
        {
            var token = SecretStore.CodemagicToken;
            var appId = CodemagicProjectSettings.Instance.CodemagicAppId;
            var client = new CodemagicApiClient(token);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var (ok, buildId, error) = await client.StartBuildAsync(appId, "android-build", branch, cts.Token);
            if (!ok)
            {
                ShowError($"빌드 트리거 실패: {error}");
                return;
            }

            // LocalUserState에 저장
            try
            {
                var state = LocalUserStateStore.Load();
                state.LastSuccessBuildId = buildId;
                LocalUserStateStore.Save(state);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Codemagic] LocalUserState 저장 실패 (무시): {ex.Message}");
            }

            EditorUtility.DisplayDialog(
                "Codemagic 빌드",
                $"빌드 트리거 완료.\nBuild ID: {buildId}\n\n" +
                $"https://codemagic.io/app/{appId}/build/{buildId}",
                "확인");
            ShowSuccess($"빌드 트리거 완료. Build ID: {buildId}");
        }

        // ── 헬퍼 ───────────────────────────────────────────────────────────

        void ShowError(string message)
        {
            _statusMessage = message;
            _statusType = EditorUI.NotificationType.Error;
            Repaint();
        }

        void ShowInfo(string message)
        {
            _statusMessage = message;
            _statusType = EditorUI.NotificationType.Info;
            Repaint();
        }

        void ShowSuccess(string message)
        {
            _statusMessage = message;
            _statusType = EditorUI.NotificationType.Success;
            Repaint();
        }

        static string ExtractCleanUnityVersion(string raw)
        {
            // "6000.3.10f1 (abcdef)" 형태에서 공백 앞만 남김
            if (string.IsNullOrEmpty(raw)) return raw;
            var space = raw.IndexOf(' ');
            return space > 0 ? raw.Substring(0, space) : raw;
        }

        static string SanitizeBuildName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "App";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            }
            return sb.Length == 0 ? "App" : sb.ToString();
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(none)";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
#endif
