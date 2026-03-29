using System;
using System.Threading.Tasks;
using Tjdtjq5.EditorToolkit.Editor;
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
            EditorUI.DrawSectionHeader("Status", EditorUI.COL_SUCCESS);
            GUILayout.Space(8);

            var settings = SupaRunSettings.Instance;

            if (!settings.IsSupabaseConfigured)
            {
                EditorUI.DrawDescription("Supabase 설정을 먼저 완료하세요.", EditorUI.COL_WARN);
                return;
            }

            if (_state == FetchState.Idle)
                FetchAll(settings);

            // 툴바
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_lastFetchTime))
                GUILayout.Label(_lastFetchTime, EditorStyles.miniLabel);
            using (new EditorGUI.DisabledGroupScope(_state == FetchState.Loading))
            {
                if (GUILayout.Button("\u21bb", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18)))
                    FetchAll(settings);
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
            EditorUI.BeginSubBox();

            // 헤더 + 링크
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("서버", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(settings.cloudRunUrl))
            {
                if (EditorUI.DrawLinkButton("Cloud Run"))
                    Application.OpenURL("https://console.cloud.google.com/run");
            }
            EditorGUILayout.EndHorizontal();

            var url = settings.cloudRunUrl;
            if (string.IsNullOrEmpty(url))
            {
                EditorUI.DrawCellLabel("  아직 배포되지 않음", 0, EditorUI.COL_MUTED);
            }
            else
            {
                EditorUI.DrawCellLabel($"  {url}", 0, EditorUI.COL_MUTED);

                if (_state == FetchState.Loading)
                    EditorUI.DrawCellLabel("  조회 중...", 0, EditorUI.COL_MUTED);
                else if (_serverOnline)
                    EditorUI.DrawCellLabel($"  \u2713 온라인 ({_healthMs}ms)", 0, EditorUI.COL_SUCCESS);
                else if (_state == FetchState.Loaded)
                    EditorUI.DrawCellLabel("  \u2717 응답 없음", 0, EditorUI.COL_ERROR);
            }

            EditorUI.EndSubBox();
        }

        // ── DB 연결 ──

        void DrawDbConnectionSection()
        {
            EditorUI.BeginSubBox();
            EditorGUILayout.LabelField("DB 연결", EditorStyles.boldLabel);

            if (_dbMaxConnections <= 0)
            {
                if (_state == FetchState.Loading)
                    EditorUI.DrawCellLabel("  조회 중...", 0, EditorUI.COL_MUTED);
                else if (_state == FetchState.Loaded)
                    EditorUI.DrawCellLabel("  Access Token이 없거나 조회 실패", 0, EditorUI.COL_WARN);
                EditorUI.EndSubBox();
                return;
            }

            EditorUI.DrawCellLabel($"  max_connections: {_dbMaxConnections}", 0, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel($"  안전 마진 80%: {_safeMaxConnections}", 0, EditorUI.COL_MUTED);

            GUILayout.Space(2);

            var totalConn = _maxInstances * _poolSize;
            var safe = totalConn <= _safeMaxConnections;
            var prevColor = GUI.color;
            GUI.color = safe ? EditorUI.COL_SUCCESS : EditorUI.COL_ERROR;
            EditorGUILayout.LabelField(safe
                ? $"  \u2713 Pool {_poolSize} \u00d7 Max {_maxInstances} = {totalConn} \u2014 배포 시 자동 적용"
                : $"  \u2717 Pool {_poolSize} \u00d7 Max {_maxInstances} = {totalConn} \u2014 한도 초과");
            GUI.color = prevColor;

            EditorUI.EndSubBox();
        }

        // ── Supabase ──

        void DrawSupabaseSection(SupaRunSettings settings)
        {
            EditorUI.BeginSubBox();

            // 헤더 + 링크
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Supabase", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorUI.DrawLinkButton("대시보드"))
                    Application.OpenURL(settings.SupabaseDashboardUrl);
                if (EditorUI.DrawLinkButton("데이터"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{settings.SupabaseProjectId}/editor");
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_projectName))
            {
                EditorUI.DrawCellLabel($"  {_projectName} ({_projectRegion})", 0, EditorUI.COL_MUTED);
                if (!string.IsNullOrEmpty(_dbVersion))
                    EditorUI.DrawCellLabel($"  PostgreSQL {_dbVersion}", 0, EditorUI.COL_MUTED);

                var statusColor = _projectStatus == "ACTIVE_HEALTHY" ? EditorUI.COL_SUCCESS : EditorUI.COL_WARN;
                EditorUI.DrawCellLabel($"  {_projectStatus}", 0, statusColor);
            }
            else if (_state == FetchState.Loading)
            {
                EditorUI.DrawCellLabel("  조회 중...", 0, EditorUI.COL_MUTED);
            }
            else if (_state == FetchState.Loaded)
            {
                EditorUI.DrawCellLabel("  Access Token이 없거나 조회 실패", 0, EditorUI.COL_WARN);
            }

            EditorUI.EndSubBox();
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
                if (EditorUI.DrawLinkButton("Supabase 요금"))
                    Application.OpenURL($"https://supabase.com/dashboard/project/{projectId}/settings/billing/usage");
            }
            if (hasGcp)
            {
                if (EditorUI.DrawLinkButton("GCP 요금"))
                    Application.OpenURL($"https://console.cloud.google.com/billing?project={settings.gcpProjectId}");
            }
            if (hasGh)
            {
                if (EditorUI.DrawLinkButton("GitHub 요금"))
                    Application.OpenURL($"https://github.com/{gh.Account}/{settings.githubRepoName}/settings/billing");
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── 데이터 조회 ──

        async void FetchAll(SupaRunSettings settings)
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
