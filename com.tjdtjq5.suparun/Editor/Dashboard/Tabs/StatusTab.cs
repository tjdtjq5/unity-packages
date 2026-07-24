using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun.Editor
{
    public class StatusTab
    {
        readonly SupaRunDashboard _dashboard;

        enum FetchState { Idle, Loading, Loaded, Failed }
        FetchState _state = FetchState.Idle;
        string _lastFetchTime;

        // 서버
        bool _serverOnline;
        int _healthMs = -1;

        // DB 연결
        int _dbMaxConnections;
        int _safeMaxConnections;
        int _poolSize = 20;
        int _maxInstances;

        // Supabase
        string _projectName;
        string _projectRegion;
        string _dbVersion;
        string _projectStatus;

        public StatusTab(SupaRunDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            GUILayout.Space(8);

            var settings = SupaRunSettings.Instance;

            if (!settings.IsSupabaseConfigured)
            {
                EditorGUILayout.LabelField("Supabase 설정을 먼저 완료하세요.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            if (_state == FetchState.Idle)
                _ = FetchAll(settings);

            // 툴바
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_lastFetchTime))
                GUILayout.Label(_lastFetchTime, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledGroupScope(_state == FetchState.Loading))
            {
                if (GUILayout.Button("↻", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18)))
                    _ = FetchAll(settings);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            DrawServerSection(settings);
            GUILayout.Space(6);
            DrawDbConnectionSection();
            GUILayout.Space(6);
            DrawSupabaseSection(settings);
            GUILayout.Space(6);
            DrawCostSection(settings);
        }

        // ── 서버 ──

        void DrawServerSection(SupaRunSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 헤더 + 링크
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("서버", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(settings.cloudRunUrl))
            {
                if (EditorGUILayout.LinkButton("Cloud Run"))
                    Application.OpenURL("https://console.cloud.google.com/run");
            }
            EditorGUILayout.EndHorizontal();

            var url = settings.cloudRunUrl;
            if (string.IsNullOrEmpty(url))
            {
                EditorGUILayout.LabelField("  아직 배포되지 않음");
            }
            else
            {
                EditorGUILayout.LabelField($"  {url}");

                if (_state == FetchState.Loading)
                    EditorGUILayout.LabelField("  조회 중...");
                else if (_serverOnline)
                    EditorGUILayout.LabelField($"  ✓ 온라인 ({_healthMs}ms)");
                else if (_state == FetchState.Loaded)
                    EditorGUILayout.LabelField("  ✗ 응답 없음");
            }

            EditorGUILayout.EndVertical();
        }

        // ── DB 연결 ──

        void DrawDbConnectionSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("DB 연결", EditorStyles.boldLabel);

            if (_dbMaxConnections <= 0)
            {
                if (_state == FetchState.Loading)
                    EditorGUILayout.LabelField("  조회 중...");
                else if (_state == FetchState.Loaded)
                    EditorGUILayout.LabelField("  Access Token이 없거나 조회 실패");
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField($"  max_connections: {_dbMaxConnections}");
            EditorGUILayout.LabelField($"  안전 마진 80%: {_safeMaxConnections}");

            GUILayout.Space(2);

            var totalConn = _maxInstances * _poolSize;
            var safe = totalConn <= _safeMaxConnections;
            EditorGUILayout.LabelField(safe
                ? $"  ✓ Pool {_poolSize} × Max {_maxInstances} = {totalConn} — 배포 시 자동 적용"
                : $"  ✗ Pool {_poolSize} × Max {_maxInstances} = {totalConn} — 한도 초과");

            EditorGUILayout.EndVertical();
        }

        // ── Supabase ──

        void DrawSupabaseSection(SupaRunSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 헤더 + 링크
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Supabase", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorGUILayout.LinkButton("대시보드"))
                    Application.OpenURL(settings.SupabaseDashboardUrl);
                if (EditorGUILayout.LinkButton("데이터"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/editor");
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_projectName))
            {
                EditorGUILayout.LabelField($"  {_projectName} ({_projectRegion})");
                if (!string.IsNullOrEmpty(_dbVersion))
                    EditorGUILayout.LabelField($"  PostgreSQL {_dbVersion}");

                EditorGUILayout.LabelField($"  {_projectStatus}");
            }
            else if (_state == FetchState.Loading)
            {
                EditorGUILayout.LabelField("  조회 중...");
            }
            else if (_state == FetchState.Loaded)
            {
                EditorGUILayout.LabelField("  Access Token이 없거나 조회 실패");
            }

            EditorGUILayout.EndVertical();
        }

        // ── 요금 ──

        void DrawCostSection(SupaRunSettings settings)
        {
            var gh = PrerequisiteChecker.CheckGh();
            var projectId = settings.SupabaseProjectId;
            var hasGcp = !string.IsNullOrEmpty(settings.gcpProjectId);
            var hasGh = gh.LoggedIn && !string.IsNullOrEmpty(settings.githubRepoName);

            if (string.IsNullOrEmpty(projectId) && !hasGcp && !hasGh) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(projectId))
            {
                if (EditorGUILayout.LinkButton("Supabase 요금"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{projectId}/settings/billing/usage");
            }
            if (hasGcp)
            {
                if (EditorGUILayout.LinkButton("GCP 요금"))
                    Application.OpenURL($"https://console.cloud.google.com/billing?project={settings.gcpProjectId}");
            }
            if (hasGh)
            {
                if (EditorGUILayout.LinkButton("GitHub 요금"))
                    Application.OpenURL($"https://github.com/{gh.Account}/{settings.githubRepoName}/settings/billing");
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 데이터 조회 ──

        async UniTaskVoid FetchAll(SupaRunSettings settings)
        {
            _state = FetchState.Loading;
            _dashboard.Repaint();

            _poolSize = settings.dbPoolSize > 0 ? settings.dbPoolSize : 20;

            try
            {
                var healthTask = FetchHealth(settings);
                var projectTask = FetchProjectInfo(settings);
                var dbTask = FetchDbMaxConnections(settings);

                await Task.WhenAll(healthTask, projectTask, dbTask);

                if (_dbMaxConnections > 0)
                {
                    _safeMaxConnections = (int)(_dbMaxConnections * 0.8);

                    _maxInstances = _safeMaxConnections / _poolSize;
                    if (_maxInstances < 1) _maxInstances = 1;
                    _poolSize = _safeMaxConnections / _maxInstances;
                    if (_poolSize < 1) _poolSize = 1;

                    if (settings.supabaseMaxConnections != _safeMaxConnections ||
                        settings.gcpMaxInstances != _maxInstances ||
                        settings.dbPoolSize != _poolSize)
                    {
                        settings.supabaseMaxConnections = _safeMaxConnections;
                        settings.gcpMaxInstances = _maxInstances;
                        settings.dbPoolSize = _poolSize;
                        settings.Save();
                    }
                }

                _state = FetchState.Loaded;
            }
            catch (Exception ex)
            {
                _state = FetchState.Failed;
                Debug.LogWarning($"[SupaRun:Status] {ex.Message}");
            }

            _lastFetchTime = DateTime.Now.ToString("HH:mm:ss");
            _dashboard.Repaint();
        }

        async Task FetchHealth(SupaRunSettings settings)
        {
            var url = settings.cloudRunUrl;
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                using var request = new UnityWebRequest($"{url}/health", "GET");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 10;

                var startMs = Environment.TickCount;
                var op = request.SendWebRequest();
                while (!op.isDone) await Task.Yield();

                _healthMs = Environment.TickCount - startMs;
                _serverOnline = request.result == UnityWebRequest.Result.Success;
            }
            catch
            {
                _serverOnline = false;
                _healthMs = -1;
            }
        }

        async Task FetchProjectInfo(SupaRunSettings settings)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;

            var (ok, name, status, region, _) = await SupabaseManagementApi.GetProjectInfo(
                settings.SupabaseProjectId, token);

            if (ok)
            {
                _projectName = name;
                _projectStatus = status;
                _projectRegion = region;
            }

            var (qOk, result, _) = await SupabaseManagementApi.RunQuery(
                settings.SupabaseProjectId, token, "SELECT version();");
            if (qOk)
            {
                var idx = result.IndexOf("PostgreSQL", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var end = result.IndexOf(" on ", idx, StringComparison.Ordinal);
                    _dbVersion = end > idx
                        ? result.Substring(idx + 11, end - idx - 11).Trim()
                        : result.Substring(idx + 11, Math.Min(20, result.Length - idx - 11)).Trim();
                }
            }
        }

        async Task FetchDbMaxConnections(SupaRunSettings settings)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token)) return;

            var (ok, maxConn, _) = await SupabaseManagementApi.GetMaxConnections(
                settings.SupabaseProjectId, token);

            if (ok)
                _dbMaxConnections = maxConn;
        }
    }
}
