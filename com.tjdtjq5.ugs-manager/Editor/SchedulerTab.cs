#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// Scheduler 탭. 스케줄 목록/편집/생성/Deploy. 단일 .sched 파일로 관리.
    /// </summary>
    public class SchedulerTab : UGSTabBase
    {
        public override string TabName => "Sched";
        public override Color TabColor => new(0.75f, 0.60f, 0.85f);
        protected override string DashboardPath => "scheduler";

        // ─── 데이터 ──────────────────────────────────
        List<ScheduleEntry> _entries = new();
        List<string> _serverIds = new();
        string _schedDir = "";
        string _schedFilePath = "";

        // ─── UI 상태 ────────────────────────────────
        ResizableColumns _columns;
        const int COL_STATUS = 0, COL_ID = 1, COL_EVENT = 2, COL_TYPE = 3, COL_CYCLE = 4, COL_ACT = 5;

        bool _foldList = true;
        bool _foldCreate;

        // 새 스케줄
        string _newId = "";
        string _newEvent = "";
        int _newTypeIdx;     // 0=recurring, 1=one-time
        int _newCronPresetIdx;
        string _newCron = "0 0 * * *";
        string _newOneTimeDate = "";
        string _newPayload = "{}";

        static readonly string[] TYPE_VALUES = { "recurring", "one-time" };
        static readonly string[] TYPE_LABELS = { "반복 (recurring)", "1회 (one-time)" };
        static readonly string[] CRON_PRESETS = { "매시", "매일", "매주 월요일", "매월 1일", "직접 입력" };
        static readonly string[] CRON_VALUES = { "0 * * * *", "0 0 * * *", "0 0 * * 1", "0 0 1 * *", "" };

        // ─── 데이터 모델 ─────────────────────────────

        enum SyncState { Synced, LocalOnly, ServerOnly }

        class ScheduleEntry
        {
            public string Id;
            public string EventName = "";
            public string Type = "recurring";
            public string Schedule = "0 0 * * *";
            public string Payload = "{}";
            public int PayloadVersion = 1;
            public SyncState Status;
            public bool IsExpanded;
            public bool IsDirty;
        }

        // ─── 데이터 로드 ─────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            _columns ??= new ResizableColumns("UGS_SC", new[]
            {
                new ColDef("상태", 36f),
                new ColDef("ID", 120f, resizable: true),
                new ColDef("이벤트", 0f),
                new ColDef("타입", 50f),
                new ColDef("주기", 50f),
                new ColDef("", 26f),
            });

            ResolveDir();
            ScanLocalFile();

            _isLoading = true;
            UGSCliRunner.RunAsync("scheduler list -j -q", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastRefreshTime = DateTime.Now;
                    ParseServerList(result.Output);
                    MergeStatus();
                }
                else
                    Debug.LogWarning($"[UGS] scheduler list 실패: {result.Error}");
            });
        }

        void ResolveDir()
        {
            string projectRoot = Application.dataPath.Replace("/Assets", "");
            foreach (var dir in Directory.GetDirectories(Application.dataPath, "UGS", SearchOption.AllDirectories))
            {
                if (dir.Contains("Library") || dir.Contains("Temp") || dir.Contains("PackageCache")) continue;
                string schedDir = Path.Combine(dir, "Scheduler");
                if (Directory.Exists(schedDir) || File.Exists(Path.Combine(dir, "RemoteConfig.rc")))
                {
                    _schedDir = Path.Combine(dir, "Scheduler");
                    return;
                }
            }
            _schedDir = Path.Combine(projectRoot, "Assets/UGS/Scheduler");
        }

        // ─── 로컬 파일 파싱 ─────────────────────────

        void ScanLocalFile()
        {
            _entries.Clear();
            if (!Directory.Exists(_schedDir)) return;

            var files = Directory.GetFiles(_schedDir, "*.sched");
            if (files.Length == 0) return;

            _schedFilePath = files[0];
            string json = File.ReadAllText(_schedFilePath);

            // "Configs" 블록 파싱
            string configsBlock = ExtractObject(json, "Configs");
            if (string.IsNullOrEmpty(configsBlock)) return;

            // 각 스케줄 ID를 키로 파싱
            int sf = 0;
            while (sf < configsBlock.Length)
            {
                int ks = configsBlock.IndexOf('"', sf);
                if (ks < 0) break;
                int ke = configsBlock.IndexOf('"', ks + 1);
                if (ke < 0) break;
                string id = configsBlock.Substring(ks + 1, ke - ks - 1);

                // "Configs" 내부의 키 중 실제 스케줄만 ($ 등 제외)
                if (id.StartsWith("$") || id == "Configs") { sf = ke + 1; continue; }

                int objStart = configsBlock.IndexOf('{', ke);
                if (objStart < 0) break;
                int objEnd = JsonFindBrace(configsBlock, objStart);
                string obj = configsBlock.Substring(objStart, objEnd - objStart + 1);

                _entries.Add(new ScheduleEntry
                {
                    Id = id,
                    EventName = ExtractStr(obj, "EventName"),
                    Type = ExtractStr(obj, "Type"),
                    Schedule = ExtractStr(obj, "Schedule"),
                    Payload = ExtractStr(obj, "Payload"),
                    PayloadVersion = ExtractInt(obj, "PayloadVersion"),
                    Status = SyncState.LocalOnly
                });

                sf = objEnd + 1;
            }
        }

        // ─── 서버 데이터 ────────────────────────────

        void ParseServerList(string json)
        {
            _serverIds.Clear();
            if (string.IsNullOrEmpty(json) || json.Trim() == "[]") return;

            // 배열에서 ID/Name 추출
            int sf = 0;
            while (true)
            {
                int os = json.IndexOf('{', sf); if (os < 0) break;
                int oe = JsonFindBrace(json, os); if (oe < 0) break;
                string obj = json.Substring(os, oe - os + 1);

                string id = ExtractStr(obj, "id");
                if (string.IsNullOrEmpty(id)) id = ExtractStr(obj, "Id");
                if (string.IsNullOrEmpty(id)) id = ExtractStr(obj, "name");
                if (string.IsNullOrEmpty(id)) id = ExtractStr(obj, "Name");
                if (!string.IsNullOrEmpty(id)) _serverIds.Add(id);

                sf = oe + 1;
            }
        }

        void MergeStatus()
        {
            var serverSet = new HashSet<string>(_serverIds);
            var localSet = new HashSet<string>(_entries.Select(e => e.Id));

            foreach (var entry in _entries)
                entry.Status = serverSet.Contains(entry.Id) ? SyncState.Synced : SyncState.LocalOnly;

            foreach (var sid in _serverIds)
            {
                if (!localSet.Contains(sid))
                    _entries.Add(new ScheduleEntry { Id = sid, Status = SyncState.ServerOnly });
            }
        }

        // ─── 메인 UI ────────────────────────────────

        public override void OnDraw()
        {
            DrawMainToolbar();
            DrawError();
            DrawSuccess();
            DrawLoading();
            if (_isLoading) return;

            GUILayout.Space(4);
            DrawList();
            GUILayout.Space(8);
            DrawCreateSection();

            if (!string.IsNullOrEmpty(_schedDir))
                DrawEnvCopySection("scheduler", _schedDir, onComplete: () => FetchData());
        }

        // ─── 툴바 ──────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (DrawColorBtn("Refresh", COL_INFO, 22)) FetchData();

            bool hasDirty = _entries.Any(e => e.IsDirty);
            GUI.enabled = hasDirty;
            if (DrawColorBtn("Save", COL_WARN, 22)) SaveFile();
            GUI.enabled = true;

            if (DrawColorBtn("Deploy ↑", COL_SUCCESS, 22)) DeploySchedules();

            GUILayout.Space(8);
            int lc = _entries.Count(e => e.Status != SyncState.ServerOnly);
            int sc = _entries.Count(e => e.Status != SyncState.LocalOnly);
            EditorGUILayout.LabelField($"로컬: {lc} / 서버: {sc}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED } }, GUILayout.Width(110));

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(DashboardPath) && DrawLinkBtn("Dashboard"))
            {
                if (UGSConfig.IsConfigured)
                {
                    var pid = UGSCliRunner.GetProjectId();
                    var eid = UGSCliRunner.GetEnvironmentId();
                    if (!string.IsNullOrEmpty(pid))
                    {
                        string url = UGSConfig.GetDashboardUrl(pid, eid, DashboardPath);
                        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── 스케줄 목록 ────────────────────────────

        void DrawList()
        {
            if (!DrawSectionFoldout(ref _foldList, $"Schedules ({_entries.Count})", TabColor)) return;
            BeginBody();

            if (_entries.Count == 0)
                EditorGUILayout.LabelField("스케줄 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            else
            {
                _columns.DrawHeader();
                for (int i = 0; i < _entries.Count; i++)
                    DrawRow(_entries[i], i);
            }

            EndBody();
        }

        void DrawRow(ScheduleEntry entry, int index)
        {
            var bg = entry.IsDirty ? new Color(0.25f, 0.22f, 0.12f) : (index % 2 == 0 ? BG_CARD : BG_SECTION);
            EditorGUILayout.BeginVertical(GetBgStyle(bg));
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

            // 상태
            string icon; Color iconColor;
            switch (entry.Status)
            {
                case SyncState.Synced: icon = "●"; iconColor = COL_SUCCESS; break;
                case SyncState.LocalOnly: icon = "○"; iconColor = COL_WARN; break;
                default: icon = "☁"; iconColor = COL_MUTED; break;
            }
            EditorGUILayout.LabelField(icon, new GUIStyle(EditorStyles.label)
                { normal = { textColor = iconColor }, alignment = TextAnchor.MiddleCenter, fontSize = 13 },
                GUILayout.Width(_columns.GetWidth(COL_STATUS)));

            DrawCellLabel(entry.Id, _columns.GetWidth(COL_ID));
            DrawCellLabel(entry.EventName, 0, string.IsNullOrEmpty(entry.EventName) ? COL_MUTED : (Color?)null);

            // 타입
            string typeLabel = entry.Type == "one-time" ? "1회" : "반복";
            DrawCellLabel(typeLabel, _columns.GetWidth(COL_TYPE),
                entry.Type == "one-time" ? COL_INFO : TabColor);

            // 주기
            string cycleLabel = entry.Type == "one-time" ? "—" : CronToLabel(entry.Schedule);
            DrawCellLabel(cycleLabel, _columns.GetWidth(COL_CYCLE), COL_MUTED);

            // 펼침/삭제
            if (entry.Status != SyncState.ServerOnly)
            {
                if (GUILayout.Button(entry.IsExpanded ? "▾" : "▸", EditorStyles.miniButton,
                    GUILayout.Width(18), GUILayout.Height(16)))
                    entry.IsExpanded = !entry.IsExpanded;
            }
            else
                GUILayout.Space(26);

            EditorGUILayout.EndHorizontal();

            if (entry.IsExpanded && entry.Status != SyncState.ServerOnly)
                DrawInlineEdit(entry);

            EditorGUILayout.EndVertical();
        }

        // ─── 인라인 편집 ────────────────────────────

        void DrawInlineEdit(ScheduleEntry entry)
        {
            // 이벤트명
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("이벤트:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(45));
            string ne = EditorGUILayout.TextField(entry.EventName);
            if (ne != entry.EventName) { entry.EventName = ne; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // 타입
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("타입:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(45));
            int tIdx = Array.IndexOf(TYPE_VALUES, entry.Type);
            if (tIdx < 0) tIdx = 0;
            int newT = EditorGUILayout.Popup(tIdx, TYPE_LABELS, GUILayout.Width(140));
            if (newT != tIdx) { entry.Type = TYPE_VALUES[newT]; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // 주기/날짜
            if (entry.Type == "recurring")
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField("주기:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(45));

                // 프리셋
                int presetIdx = Array.IndexOf(CRON_VALUES, entry.Schedule);
                if (presetIdx < 0) presetIdx = 4; // 직접 입력
                int newPreset = EditorGUILayout.Popup(presetIdx, CRON_PRESETS, GUILayout.Width(90));
                if (newPreset != presetIdx && newPreset < 4)
                { entry.Schedule = CRON_VALUES[newPreset]; entry.IsDirty = true; }

                GUILayout.Space(4);
                EditorGUILayout.LabelField("cron:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(30));
                string nc = EditorGUILayout.TextField(entry.Schedule, GUILayout.Width(120));
                if (nc != entry.Schedule) { entry.Schedule = nc; entry.IsDirty = true; }

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(40);
                EditorGUILayout.LabelField("실행일:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(45));
                string nd = EditorGUILayout.TextField(entry.Schedule);
                if (nd != entry.Schedule) { entry.Schedule = nd; entry.IsDirty = true; }
                EditorGUILayout.EndHorizontal();
            }

            // 페이로드
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("페이로드:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(94);
            var payStyle = new GUIStyle(EditorStyles.textArea)
                { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            string np = EditorGUILayout.TextArea(entry.Payload ?? "{}", payStyle, GUILayout.Height(36));
            if (np != entry.Payload) { entry.Payload = np; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // 삭제 버튼
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("이 스케줄 삭제", EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(14)))
            {
                if (EditorUtility.DisplayDialog("삭제", $"'{entry.Id}'를 삭제하시겠습니까?", "삭제", "취소"))
                {
                    _entries.Remove(entry);
                    SaveFile();
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // ─── 새 스케줄 ──────────────────────────────

        void DrawCreateSection()
        {
            if (!DrawSectionFoldout(ref _foldCreate, "새 스케줄", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("ID:", GUILayout.Width(45));
            _newId = EditorGUILayout.TextField(_newId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("이벤트:", GUILayout.Width(45));
            _newEvent = EditorGUILayout.TextField(_newEvent);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("타입:", GUILayout.Width(45));
            _newTypeIdx = EditorGUILayout.Popup(_newTypeIdx, TYPE_LABELS, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            if (_newTypeIdx == 0) // recurring
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
                EditorGUILayout.LabelField("주기:", GUILayout.Width(45));
                _newCronPresetIdx = EditorGUILayout.Popup(_newCronPresetIdx, CRON_PRESETS, GUILayout.Width(90));
                if (_newCronPresetIdx < 4) _newCron = CRON_VALUES[_newCronPresetIdx];
                GUILayout.Space(4);
                _newCron = EditorGUILayout.TextField(_newCron, GUILayout.Width(120));
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
                EditorGUILayout.LabelField("실행일:", GUILayout.Width(45));
                if (string.IsNullOrEmpty(_newOneTimeDate))
                    _newOneTimeDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
                _newOneTimeDate = EditorGUILayout.TextField(_newOneTimeDate);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("페이로드:", GUILayout.Width(55));
            _newPayload = EditorGUILayout.TextField(_newPayload);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool isDup = _entries.Any(e => e.Id == _newId?.Trim());
            GUI.enabled = !string.IsNullOrWhiteSpace(_newId) && !string.IsNullOrWhiteSpace(_newEvent) && !isDup;
            if (GUILayout.Button("+ 추가", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(18)))
                AddSchedule();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDup && !string.IsNullOrWhiteSpace(_newId))
                EditorGUILayout.LabelField("이미 존재하는 ID입니다",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_ERROR } });

            EndBody();
        }

        void AddSchedule()
        {
            _entries.Add(new ScheduleEntry
            {
                Id = _newId.Trim(),
                EventName = _newEvent.Trim(),
                Type = TYPE_VALUES[_newTypeIdx],
                Schedule = _newTypeIdx == 0 ? _newCron : _newOneTimeDate,
                Payload = _newPayload,
                PayloadVersion = 1,
                Status = SyncState.LocalOnly,
                IsDirty = true
            });

            _newId = "";
            _newEvent = "";
            _newPayload = "{}";
            SaveFile();
        }

        // ─── 저장 ──────────────────────────────────

        void SaveFile()
        {
            if (!Directory.Exists(_schedDir)) Directory.CreateDirectory(_schedDir);
            if (string.IsNullOrEmpty(_schedFilePath))
                _schedFilePath = Path.Combine(_schedDir, "Schedules.sched");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/schedules.schema.json\",");
            sb.AppendLine("  \"Configs\": {");

            var localEntries = _entries.Where(e => e.Status != SyncState.ServerOnly).ToList();
            for (int i = 0; i < localEntries.Count; i++)
            {
                var e = localEntries[i];
                string payload = (e.Payload ?? "{}").Replace("\"", "\\\"");
                string comma = i < localEntries.Count - 1 ? "," : "";

                sb.AppendLine($"    \"{e.Id}\": {{");
                sb.AppendLine($"      \"EventName\": \"{e.EventName}\",");
                sb.AppendLine($"      \"Type\": \"{e.Type}\",");
                sb.AppendLine($"      \"Schedule\": \"{e.Schedule}\",");
                sb.AppendLine($"      \"PayloadVersion\": {e.PayloadVersion},");
                sb.AppendLine($"      \"Payload\": \"{payload}\"");
                sb.AppendLine($"    }}{comma}");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");

            File.WriteAllText(_schedFilePath, sb.ToString());
            foreach (var e in _entries) e.IsDirty = false;
            AssetDatabase.Refresh();

            _lastSuccess = "저장 완료";
        }

        // ─── Deploy ─────────────────────────────────

        void DeploySchedules()
        {
            if (!Directory.Exists(_schedDir)) { _lastError = "Scheduler 폴더 없음"; return; }
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            UGSCliRunner.RunAsync($"deploy \"{_schedDir.Replace('\\', '/')}\" -s scheduler", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastSuccess = "Deploy 완료" + (!string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "");
                    FetchData();
                }
                else
                {
                    var sb = new StringBuilder($"Deploy 실패 (exit {result.ExitCode})");
                    if (!string.IsNullOrEmpty(result.Error)) sb.Append($"\n{result.Error}");
                    if (!string.IsNullOrEmpty(result.Output)) sb.Append($"\n{result.Output}");
                    _lastError = sb.ToString();
                }
            });
        }

        // ─── 유틸 ──────────────────────────────────

        static string CronToLabel(string cron)
        {
            if (string.IsNullOrEmpty(cron)) return "—";
            if (cron == "0 * * * *") return "매시";
            if (cron == "0 0 * * *") return "매일";
            if (cron.StartsWith("0 0 * * ")) return "매주";
            if (cron.StartsWith("0 0 1 * *")) return "매월";
            return "커스텀";
        }

        static string ExtractStr(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal); if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length); if (ci < 0) return "";
            int s = ci + 1;
            while (s < json.Length && json[s] == ' ') s++;
            if (s >= json.Length) return "";
            if (json[s] == '"') { int qe = json.IndexOf('"', s + 1); return qe > s ? json.Substring(s + 1, qe - s - 1) : ""; }
            int e = s;
            while (e < json.Length && json[e] != ',' && json[e] != '}') e++;
            return json.Substring(s, e - s).Trim();
        }

        static int ExtractInt(string json, string field)
        {
            string val = ExtractStr(json, field);
            return int.TryParse(val, out int v) ? v : 1;
        }

        static string ExtractObject(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal); if (ki < 0) return "";
            int bs = json.IndexOf('{', ki + key.Length); if (bs < 0) return "";
            int be = JsonFindBrace(json, bs);
            return json.Substring(bs + 1, be - bs - 1);
        }

    }
}
#endif
