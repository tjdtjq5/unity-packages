#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor.Update
{
    /// <summary>Content Update 탭. 워크플로우 가이드 + 해시 비교 + 버전 라우팅.</summary>
    internal class UpdateTab : AddrXTabBase
    {
        static readonly string[] SubNames = { "Workflow", "Hash Compare", "Version Route" };
        static readonly Color COL_UPDATE = new(0.4f, 0.75f, 0.95f);

        readonly Action _repaint;
        int _subTab;
        Vector2 _scroll;

        // Workflow
        string _contentStatePath = "";
        string _autoDetectedPath;
        bool _autoDetectDone;
        string _cachedProfile;
        int _cachedGroupCount = -1;

        // Hash Compare
        string _oldCatalogPath = "";
        string _newCatalogPath = "";
        CompareReport? _compareReport;

        // Version Route
        string _routeFilePath = "";
        VersionRoute _route;
        string _newAppVersion = "";
        string _newCatalogFile = "";

        public UpdateTab(Action repaint) => _repaint = repaint;

        public override string TabName => "Update";
        public override Color TabColor => COL_UPDATE;

        public override void OnDraw()
        {
            AddrXGui.DrawNotificationBar(ref _notification, _notificationType);

            if (AddressableAssetSettingsDefaultObject.Settings == null)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField("Addressables Settings가 필요합니다",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _subTab = GUILayout.Toolbar(_subTab, SubNames);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_subTab)
            {
                case 0: DrawWorkflow(); break;
                case 1: DrawHashCompare(); break;
                case 2: DrawVersionRoute(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        // ═══════════════════════════════════════════════════════
        // ─── Workflow
        // ═══════════════════════════════════════════════════════

        void DrawWorkflow()
        {
            EditorGUILayout.LabelField("Content Update Workflow", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            // 현재 상태 (캐싱)
            if (_cachedGroupCount < 0)
            {
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                _cachedProfile = settings.profileSettings.GetProfileName(settings.activeProfileId);
                _cachedGroupCount = settings.groups.Count(g =>
                    g != null && g.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas
                        .BundledAssetGroupSchema>() != null);
            }

            EditorGUILayout.BeginHorizontal();
            AddrXGui.DrawStatCard("Profile", _cachedProfile ?? "?");
            AddrXGui.DrawStatCard("Groups", _cachedGroupCount.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(12);

            // Step 1: Content State 파일 선택
            EditorGUILayout.LabelField("Step 1: 이전 빌드 상태 파일", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                "Full Build 시 생성된 addressables_content_state.bin 파일을 선택합니다.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            _contentStatePath = EditorGUILayout.TextField(_contentStatePath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFilePanel(
                    "Content State 파일 선택", "Assets", "bin");
                if (!string.IsNullOrEmpty(path))
                    _contentStatePath = path;
            }
            EditorGUILayout.EndHorizontal();

            // 자동 감지 (1회만 실행)
            if (string.IsNullOrEmpty(_contentStatePath))
            {
                if (!_autoDetectDone)
                {
                    _autoDetectedPath = FindContentStateFile();
                    _autoDetectDone = true;
                }

                if (_autoDetectedPath != null)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField($"자동 감지: {_autoDetectedPath}",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("사용", EditorStyles.miniButton))
                        _contentStatePath = _autoDetectedPath;
                }
            }

            EditorGUILayout.Space(12);

            // Step 2: 변경 감지
            EditorGUILayout.LabelField("Step 2: 변경 감지 + 빌드", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                "이전 빌드 이후 변경된 에셋을 감지하고 업데이트 빌드를 실행합니다.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_contentStatePath));
            if (GUILayout.Button("Check & Build Content Update", GUILayout.Height(32)))
            {
                BuildContentUpdate();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(12);

            // Step 3: 업로드 안내
            EditorGUILayout.LabelField("Step 3: 서버 업로드", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            var buildPath = GetRemoteBuildPath(AddressableAssetSettingsDefaultObject.Settings);
            if (!string.IsNullOrEmpty(buildPath))
            {
                EditorGUILayout.LabelField($"빌드 출력 경로: {buildPath}",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(4);
                if (GUILayout.Button("폴더 열기", EditorStyles.miniButton))
                    EditorUtility.RevealInFinder(buildPath);
            }
            else
            {
                EditorGUILayout.LabelField("Remote Build Path가 설정되지 않았습니다.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                "빌드된 번들 + 카탈로그 파일을 CDN/서버에 업로드하세요.\n" +
                "Hash Compare 탭에서 변경된 번들을 확인할 수 있습니다.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void BuildContentUpdate()
        {
            if (!File.Exists(_contentStatePath))
            {
                _notification = "Content State 파일을 찾을 수 없습니다.";
                _notificationType = NotificationType.Error;
                return;
            }

            AddrXLog.Info("Update", "Content Update 빌드 시작...");
            _notification = "Content Update 빌드 실행 중...";
            _notificationType = NotificationType.Info;
            _repaint?.Invoke();

            var result = UnityEditor.AddressableAssets.Build.ContentUpdateScript
                .BuildContentUpdate(
                    AddressableAssetSettingsDefaultObject.Settings,
                    _contentStatePath);

            if (result != null && string.IsNullOrEmpty(result.Error))
            {
                _notification = "Content Update 빌드 완료!";
                _notificationType = NotificationType.Success;
                AddrXLog.Info("Update", "Content Update 빌드 성공");
            }
            else
            {
                _notification = $"빌드 실패: {result?.Error ?? "알 수 없는 오류"}";
                _notificationType = NotificationType.Error;
                AddrXLog.Error("Update", $"Content Update 빌드 실패: {result?.Error}");
            }
        }

        string FindContentStateFile()
        {
            // 우선순위: Addressables 기본 경로 → Assets 전체 → 프로젝트 루트
            var searchPaths = new[]
            {
                "Assets/AddressableAssetsData",
                "Assets",
                "."
            };

            foreach (var dir in searchPaths)
            {
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir, "addressables_content_state.bin",
                    SearchOption.AllDirectories);
                if (files.Length == 0) continue;

                if (files.Length > 1)
                    AddrXLog.Warning("Update",
                        $"Content State 파일이 {files.Length}개 발견됨. 첫 번째 사용: {files[0]}");

                return files[0].Replace('\\', '/');
            }
            return null;
        }

        string GetRemoteBuildPath(AddressableAssetSettings settings)
        {
            var value = settings.profileSettings.GetValueByName(
                settings.activeProfileId, "RemoteBuildPath");
            if (string.IsNullOrEmpty(value)) return null;
            return settings.profileSettings.EvaluateString(settings.activeProfileId, value);
        }

        // ═══════════════════════════════════════════════════════
        // ─── Hash Compare
        // ═══════════════════════════════════════════════════════

        void DrawHashCompare()
        {
            EditorGUILayout.LabelField("Build Hash Compare", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                "두 빌드의 카탈로그 JSON을 비교하여 변경된 번들을 추적합니다.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);

            // 이전 카탈로그
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Old Catalog", GUILayout.Width(80));
            _oldCatalogPath = EditorGUILayout.TextField(_oldCatalogPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var path = EditorUtility.OpenFilePanel("이전 카탈로그", "", "json");
                if (!string.IsNullOrEmpty(path)) _oldCatalogPath = path;
            }
            EditorGUILayout.EndHorizontal();

            // 새 카탈로그
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("New Catalog", GUILayout.Width(80));
            _newCatalogPath = EditorGUILayout.TextField(_newCatalogPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var path = EditorUtility.OpenFilePanel("새 카탈로그", "", "json");
                if (!string.IsNullOrEmpty(path)) _newCatalogPath = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUI.BeginDisabledGroup(
                string.IsNullOrEmpty(_oldCatalogPath) || string.IsNullOrEmpty(_newCatalogPath));
            if (GUILayout.Button("Compare", GUILayout.Height(30)))
            {
                _compareReport = BuildHashComparer.Compare(_oldCatalogPath, _newCatalogPath);
                var r = _compareReport.Value;
                _notification = r.IsEmpty
                    ? "변경 없음 — 동일한 빌드"
                    : $"변경 발견: 추가 {r.Added.Count}, 변경 {r.Changed.Count}, 제거 {r.Removed.Count}";
                _notificationType = r.IsEmpty
                    ? NotificationType.Success
                    : NotificationType.Info;
            }
            EditorGUI.EndDisabledGroup();

            if (!_compareReport.HasValue) return;
            if (_compareReport.Value.IsEmpty)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("변경된 번들이 없습니다", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.Space(8);
            var report = _compareReport.Value;

            // 요약 카드
            EditorGUILayout.BeginHorizontal();
            AddrXGui.DrawStatCard("Added", report.Added.Count.ToString());
            AddrXGui.DrawStatCard("Changed", report.Changed.Count.ToString());
            AddrXGui.DrawStatCard("Removed", report.Removed.Count.ToString());
            AddrXGui.DrawStatCard("Unchanged", report.Unchanged.Count.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // 변경 목록
            if (report.Changed.Count > 0)
            {
                EditorGUILayout.LabelField("Changed Bundles", EditorStyles.miniLabel);
                foreach (var c in report.Changed)
                    EditorGUILayout.LabelField($"  {c.BundleName}  ({c.OldHash} → {c.NewHash})");
            }
            if (report.Added.Count > 0)
            {
                EditorGUILayout.LabelField("Added Bundles", EditorStyles.miniLabel);
                foreach (var c in report.Added)
                    EditorGUILayout.LabelField($"  {c.BundleName}");
            }
            if (report.Removed.Count > 0)
            {
                EditorGUILayout.LabelField("Removed Bundles", EditorStyles.miniLabel);
                foreach (var c in report.Removed)
                    EditorGUILayout.LabelField($"  {c.BundleName}");
            }
        }

        // ═══════════════════════════════════════════════════════
        // ─── Version Route
        // ═══════════════════════════════════════════════════════

        void DrawVersionRoute()
        {
            EditorGUILayout.LabelField("Version Route Manager", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                "앱 버전별로 어떤 카탈로그를 사용할지 매핑합니다.\n" +
                "고정 이름 방식이면 이 기능은 불필요합니다.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);

            // 파일 선택
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Route File", GUILayout.Width(70));
            _routeFilePath = EditorGUILayout.TextField(_routeFilePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var path = EditorUtility.OpenFilePanel("Version Route 파일", "", "json");
                if (!string.IsNullOrEmpty(path)) _routeFilePath = path;
            }
            if (GUILayout.Button("New", GUILayout.Width(40)))
            {
                var path = EditorUtility.SaveFilePanel(
                    "Version Route 파일 생성", "", "version_route", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    _routeFilePath = path;
                    _route = new VersionRoute();
                    VersionRouteManager.Save(_route, path);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 로드
            if (!string.IsNullOrEmpty(_routeFilePath) && _route == null)
                _route = VersionRouteManager.Load(_routeFilePath);

            if (_route == null)
            {
                EditorGUILayout.LabelField("Route 파일을 선택하거나 새로 생성하세요",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUILayout.Space(8);

            // 최소 버전
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Minimum Version", GUILayout.Width(110));
            var newMin = EditorGUILayout.TextField(_route.minimum);
            if (newMin != _route.minimum)
            {
                _route.minimum = newMin;
                VersionRouteManager.Save(_route, _routeFilePath);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // 요약
            var cleanable = VersionRouteManager.GetCleanableEntries(_route);
            int uniqueCatalogs = VersionRouteManager.GetUniqueCatalogCount(_route);

            EditorGUILayout.BeginHorizontal();
            AddrXGui.DrawStatCard("Routes", _route.routes.Count.ToString());
            AddrXGui.DrawStatCard("Catalogs", uniqueCatalogs.ToString());
            AddrXGui.DrawStatCard("Cleanable", cleanable.Count.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // 라우트 목록
            EditorGUILayout.LabelField("Routes", EditorStyles.miniLabel);
            for (int i = 0; i < _route.routes.Count; i++)
            {
                var entry = _route.routes[i];
                bool isCleanable = cleanable.Contains(entry);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  v{entry.appVersion}", GUILayout.Width(80));
                EditorGUILayout.LabelField($"→ {entry.catalogFile}");
                GUILayout.FlexibleSpace();
                if (isCleanable)
                    EditorGUILayout.LabelField("cleanable", GUILayout.Width(60));
                if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    _route.routes.RemoveAt(i);
                    VersionRouteManager.Save(_route, _routeFilePath);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);

            // 새 라우트 추가
            EditorGUILayout.LabelField("Add Route", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("App Version", GUILayout.Width(80));
            _newAppVersion = EditorGUILayout.TextField(_newAppVersion, GUILayout.Width(80));
            GUILayout.Label("Catalog", GUILayout.Width(50));
            _newCatalogFile = EditorGUILayout.TextField(_newCatalogFile);
            if (GUILayout.Button("+", GUILayout.Width(24)))
            {
                if (!string.IsNullOrWhiteSpace(_newAppVersion) &&
                    !string.IsNullOrWhiteSpace(_newCatalogFile))
                {
                    _route.routes.Add(new RouteEntry
                    {
                        appVersion = _newAppVersion,
                        catalogFile = _newCatalogFile
                    });
                    VersionRouteManager.Save(_route, _routeFilePath);
                    _newAppVersion = "";
                    _newCatalogFile = "";
                }
            }
            EditorGUILayout.EndHorizontal();

            // 정리 버튼
            if (cleanable.Count > 0)
            {
                EditorGUILayout.Space(8);
                if (GUILayout.Button($"최소 버전 미만 {cleanable.Count}건 정리", GUILayout.Height(24)))
                {
                    foreach (var entry in cleanable)
                        _route.routes.Remove(entry);
                    VersionRouteManager.Save(_route, _routeFilePath);
                    _notification = $"{cleanable.Count}건 정리 완료";
                    _notificationType = NotificationType.Success;
                }
            }
        }
    }
}
#endif
