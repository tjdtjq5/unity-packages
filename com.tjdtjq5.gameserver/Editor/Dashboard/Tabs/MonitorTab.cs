using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.GameServer.Editor
{
    public class MonitorTab
    {
        readonly GameServerDashboard _dashboard;

        // 필터
        enum LevelFilter { All, Error, Warn }
        LevelFilter _filter = LevelFilter.All;

        // 데이터
        List<LogEntry> _logs = new();
        HashSet<int> _expanded = new();
        Vector2 _scrollPos;

        // 상태
        enum FetchState { Idle, Loading, Loaded, Failed }
        FetchState _state = FetchState.Idle;
        string _errorMessage;
        bool _isTableMissing;
        int _limit = 50;
        bool _hasMore;

        public MonitorTab(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            EditorUI.DrawSectionHeader("Monitor", EditorUI.COL_INFO);
            GUILayout.Space(8);

            var settings = GameServerSettings.Instance;

            if (!settings.IsSupabaseConfigured)
            {
                DrawNotConfigured();
                return;
            }

            // 첫 진입 시 자동 로드
            if (_state == FetchState.Idle)
                FetchLogs(settings);

            DrawToolbar(settings);
            GUILayout.Space(4);
            DrawLogList();
        }

        // ── 미설정 ──

        void DrawNotConfigured()
        {
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "Supabase 연결이 필요합니다.\n" +
                "Settings > Supabase에서 설정하세요.");
            GUILayout.Space(8);
            if (EditorUI.DrawColorButton("지금 설정하기", GameServerDashboard.COL_PRIMARY, 28))
                _dashboard.OpenSettings();
            EditorUI.EndBody();
        }

        // ── 툴바 (필터 + 새로고침) ──

        void DrawToolbar(GameServerSettings settings)
        {
            EditorUI.BeginBody();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawFilterButton("All", LevelFilter.All);
                DrawFilterButton("Error", LevelFilter.Error);
                DrawFilterButton("Warn", LevelFilter.Warn);

                EditorUI.FlexSpace();

                if (_state == FetchState.Loading)
                {
                    EditorUI.DrawCellLabel("조회 중...", 80, EditorUI.COL_MUTED);
                }
                else
                {
                    if (EditorUI.DrawMiniButton("새로고침"))
                    {
                        _logs.Clear();
                        _expanded.Clear();
                        _limit = 50;
                        FetchLogs(settings);
                    }
                }
            }
            EditorUI.EndBody();
        }

        void DrawFilterButton(string label, LevelFilter filter)
        {
            var isActive = _filter == filter;
            var color = isActive ? EditorUI.COL_INFO : EditorUI.COL_MUTED;
            if (EditorUI.DrawColorButton(label, color, 22))
            {
                if (_filter != filter)
                {
                    _filter = filter;
                    _logs.Clear();
                    _expanded.Clear();
                    _limit = 50;
                    FetchLogs(GameServerSettings.Instance);
                }
            }
            GUILayout.Space(2);
        }

        // ── 로그 목록 ──

        void DrawLogList()
        {
            if (_state == FetchState.Failed)
            {
                EditorUI.BeginBody();
                if (_isTableMissing)
                {
                    EditorUI.DrawDescription(
                        "serverlogs 테이블이 아직 생성되지 않았습니다.\n\n" +
                        "Deploy 탭 > DB 동기화를 실행하면\n" +
                        "테이블이 자동으로 생성됩니다.", EditorUI.COL_WARN);
                }
                else
                {
                    EditorUI.DrawDescription($"조회 실패: {_errorMessage}", EditorUI.COL_ERROR);
                }
                EditorUI.EndBody();
                return;
            }

            if (_state == FetchState.Loading && _logs.Count == 0)
            {
                EditorUI.DrawLoading(true, "서버 로그 조회 중...");
                return;
            }

            if (_logs.Count == 0)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription(
                    "기록된 로그가 없습니다.\n\n" +
                    "서버에서 에러/경고 발생 시\n" +
                    "자동으로 여기에 표시됩니다.");
                EditorUI.EndBody();
                return;
            }

            // 로그 카드들
            for (int i = 0; i < _logs.Count; i++)
            {
                DrawLogCard(i, _logs[i]);
                GUILayout.Space(2);
            }

            // 페이징
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorUI.DrawCellLabel($"  {_logs.Count}개 표시 중", 0, EditorUI.COL_MUTED);
                EditorUI.FlexSpace();
                if (_hasMore && EditorUI.DrawMiniButton("더 불러오기"))
                {
                    _limit += 50;
                    FetchLogs(GameServerSettings.Instance);
                }
            }
        }

        // ── 개별 로그 카드 ──

        void DrawLogCard(int index, LogEntry log)
        {
            EditorUI.BeginBody();

            // 1줄: 레벨 아이콘 + endpoint + 시간
            using (new EditorGUILayout.HorizontalScope())
            {
                var levelIcon = log.level == "error" ? "●" : "▲";
                var levelColor = log.level == "error" ? EditorUI.COL_ERROR : EditorUI.COL_WARN;
                EditorUI.DrawCellLabel($"  {levelIcon} {log.endpoint ?? "unknown"}", 0, levelColor);
                EditorUI.FlexSpace();
                EditorUI.DrawCellLabel(FormatRelativeTime(log.createdat), 80, EditorUI.COL_MUTED,
                    TextAnchor.MiddleRight);
            }

            // 2줄: 메시지 (한 줄로 잘라서)
            var shortMsg = log.message != null && log.message.Length > 80
                ? log.message.Substring(0, 80) + "..."
                : log.message;
            EditorUI.DrawCellLabel($"    {shortMsg}", 0, EditorUI.COL_MUTED);

            // 3줄: 메타 정보
            var meta = BuildMetaLine(log);
            if (!string.IsNullOrEmpty(meta))
                EditorUI.DrawCellLabel($"    {meta}", 0, EditorUI.COL_MUTED);

            // 접기/펼치기
            bool isExpanded = _expanded.Contains(index);
            var toggleLabel = isExpanded ? "    ▼ 접기" : "    ▶ 상세보기";
            if (EditorUI.DrawLinkButton(toggleLabel))
            {
                if (isExpanded) _expanded.Remove(index);
                else _expanded.Add(index);
            }

            // 상세 영역
            if (isExpanded)
                DrawLogDetail(log);

            EditorUI.EndBody();
        }

        void DrawLogDetail(LogEntry log)
        {
            GUILayout.Space(4);
            EditorUI.BeginSubBox();

            // Stack Trace
            if (!string.IsNullOrEmpty(log.stack))
            {
                EditorUI.DrawCellLabel("  Stack Trace", 0, EditorUI.COL_INFO);
                GUILayout.Space(2);
                var stackLines = log.stack.Length > 500
                    ? log.stack.Substring(0, 500) + "\n  ..."
                    : log.stack;
                EditorUI.DrawDescription(stackLines);
            }

            // Request Body
            if (!string.IsNullOrEmpty(log.request_body))
            {
                GUILayout.Space(4);
                EditorUI.DrawCellLabel("  Request Body", 0, EditorUI.COL_INFO);
                GUILayout.Space(2);
                EditorUI.DrawDescription(log.request_body);
            }

            EditorUI.EndSubBox();

            // 로그 복사 버튼
            GUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorUI.FlexSpace();
                if (EditorUI.DrawMiniButton("로그 복사"))
                {
                    GUIUtility.systemCopyBuffer = FormatLogForCopy(log);
                    _dashboard.ShowNotification("클립보드에 복사됨", EditorUI.NotificationType.Info);
                }
            }
        }

        // ── 데이터 조회 ──

        async void FetchLogs(GameServerSettings settings)
        {
            _state = FetchState.Loading;
            _dashboard.Repaint();

            try
            {
                var url = $"{settings.supabaseUrl}/rest/v1/serverlogs" +
                          $"?order=createdat.desc&limit={_limit + 1}"; // +1로 hasMore 판단

                if (_filter == LevelFilter.Error)
                    url += "&level=eq.error";
                else if (_filter == LevelFilter.Warn)
                    url += "&level=eq.warn";

                var anonKey = GameServerSettings.SupabaseAnonKey;

                using var request = new UnityWebRequest(url, "GET");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("apikey", anonKey);
                request.SetRequestHeader("Authorization", $"Bearer {anonKey}");
                request.timeout = 15;

                var op = request.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    _state = FetchState.Failed;
                    var responseBody = request.downloadHandler.text ?? "";
                    _isTableMissing = request.responseCode == 404
                        || responseBody.Contains("relation")
                        || responseBody.Contains("does not exist");
                    _errorMessage = $"HTTP {request.responseCode}: {responseBody}";
                    _dashboard.Repaint();
                    return;
                }

                var body = request.downloadHandler.text;
                var parsed = ParseLogArray(body);

                _hasMore = parsed.Count > _limit;
                if (_hasMore)
                    parsed.RemoveAt(parsed.Count - 1);

                _logs = parsed;
                _state = FetchState.Loaded;
            }
            catch (Exception ex)
            {
                _state = FetchState.Failed;
                _errorMessage = ex.Message;
            }

            _dashboard.Repaint();
        }

        // ── JSON 파싱 (간이) ──

        List<LogEntry> ParseLogArray(string json)
        {
            var list = new List<LogEntry>();
            if (string.IsNullOrEmpty(json) || json.TrimStart()[0] != '[')
                return list;

            var searchIdx = 0;
            while (searchIdx < json.Length)
            {
                var objStart = json.IndexOf('{', searchIdx);
                if (objStart < 0) break;

                // brace depth 추적
                var depth = 0;
                var objEnd = objStart;
                for (var i = objStart; i < json.Length; i++)
                {
                    if (json[i] == '{') depth++;
                    else if (json[i] == '}') depth--;
                    if (depth == 0) { objEnd = i; break; }
                }

                var obj = json.Substring(objStart, objEnd - objStart + 1);
                searchIdx = objEnd + 1;

                list.Add(new LogEntry
                {
                    id = GetString(obj, "id"),
                    level = GetString(obj, "level"),
                    message = GetString(obj, "message"),
                    stack = GetString(obj, "stack"),
                    endpoint = GetString(obj, "endpoint"),
                    player_id = GetString(obj, "player_id"),
                    service_name = GetString(obj, "service_name"),
                    status_code = GetInt(obj, "status_code"),
                    request_body = GetString(obj, "request_body"),
                    duration_ms = GetInt(obj, "duration_ms"),
                    createdat = GetLong(obj, "createdat"),
                });
            }

            return list;
        }

        // ── 유틸 ──

        string BuildMetaLine(LogEntry log)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(log.player_id))
                parts.Add($"player: {log.player_id}");
            if (log.status_code > 0)
                parts.Add($"{log.status_code}");
            if (log.duration_ms > 0)
                parts.Add($"{log.duration_ms}ms");
            if (parts.Count == 0 && !string.IsNullOrEmpty(log.service_name))
                parts.Add(log.service_name);
            return string.Join(" │ ", parts);
        }

        static string FormatRelativeTime(long unixMs)
        {
            if (unixMs <= 0) return "";
            var diff = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}초 전";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}시간 전";
            if (diff.TotalDays < 30) return $"{(int)diff.TotalDays}일 전";
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("MM/dd HH:mm");
        }

        static string FormatLogForCopy(LogEntry log)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{log.level?.ToUpper()}] {log.endpoint}");
            sb.AppendLine($"Message: {log.message}");
            if (!string.IsNullOrEmpty(log.player_id))
                sb.AppendLine($"Player: {log.player_id}");
            if (!string.IsNullOrEmpty(log.service_name))
                sb.AppendLine($"Service: {log.service_name}");
            if (log.status_code > 0)
                sb.AppendLine($"Status: {log.status_code}");
            if (log.duration_ms > 0)
                sb.AppendLine($"Duration: {log.duration_ms}ms");
            if (!string.IsNullOrEmpty(log.request_body))
                sb.AppendLine($"Request: {log.request_body}");
            if (!string.IsNullOrEmpty(log.stack))
            {
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(log.stack);
            }
            if (log.createdat > 0)
                sb.AppendLine($"Time: {DateTimeOffset.FromUnixTimeMilliseconds(log.createdat).LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            return sb.ToString();
        }

        // ── 간이 JSON 헬퍼 ──

        static string GetString(string json, string key)
        {
            var pattern = $"\"{key}\"";
            var idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += pattern.Length;
            idx = json.IndexOf(':', idx);
            if (idx < 0) return null;
            idx++;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
            if (idx >= json.Length) return null;
            if (json[idx] == '"')
            {
                idx++;
                // escaped quote 처리
                var sb = new StringBuilder();
                while (idx < json.Length)
                {
                    if (json[idx] == '\\' && idx + 1 < json.Length)
                    {
                        var next = json[idx + 1];
                        if (next == '"') { sb.Append('"'); idx += 2; continue; }
                        if (next == 'n') { sb.Append('\n'); idx += 2; continue; }
                        if (next == '\\') { sb.Append('\\'); idx += 2; continue; }
                    }
                    if (json[idx] == '"') break;
                    sb.Append(json[idx]);
                    idx++;
                }
                return sb.ToString();
            }
            if (json[idx] == 'n') return null;
            var valEnd = json.IndexOfAny(new[] { ',', '}', ']' }, idx);
            return valEnd < 0 ? json.Substring(idx).Trim() : json.Substring(idx, valEnd - idx).Trim();
        }

        static int GetInt(string json, string key)
        {
            var val = GetString(json, key);
            return int.TryParse(val, out var result) ? result : 0;
        }

        static long GetLong(string json, string key)
        {
            var val = GetString(json, key);
            return long.TryParse(val, out var result) ? result : 0;
        }

        // ── 데이터 모델 ──

        class LogEntry
        {
            public string id;
            public string level;
            public string message;
            public string stack;
            public string endpoint;
            public string player_id;
            public string service_name;
            public int status_code;
            public string request_body;
            public int duration_ms;
            public long createdat;
        }
    }
}
