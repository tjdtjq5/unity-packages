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
    /// Leaderboards 탭. 리더보드 목록/편집/생성/리셋/Deploy.
    /// </summary>
    public class LeaderboardsTab : UGSTabBase
    {
        public override string TabName => "LB";
        public override Color TabColor => new(0.90f, 0.45f, 0.55f);
        protected override string DashboardPath => "leaderboards/overview";

        // ─── 데이터 ──────────────────────────────────
        List<LBEntry> _entries = new();
        string _lbDir = "";

        // ─── UI 상태 ────────────────────────────────
        ResizableColumns _columns;
        const int COL_STATUS = 0, COL_ID = 1, COL_NAME = 2, COL_SORT = 3, COL_RESET = 4, COL_ACT = 5;

        bool _foldList = true;
        bool _foldCreate;
        bool _foldManage;

        // 새 리더보드
        string _newId = "";
        string _newName = "";
        int _newSortIdx; // 0=desc, 1=asc
        int _newUpdateIdx; // 0=keepBest, 1=keepLatest, 2=aggregate
        bool _newHasReset;
        int _newResetPresetIdx;
        string _newResetCron = "0 0 * * 1";

        // 관리 + 순위
        int _manageIdx;
        List<ScoreEntry> _scores = new();
        int _scorePage;
        int _scoreTotal;
        bool _scoresLoaded;
        const int PAGE_SIZE = 10;

        struct ScoreEntry
        {
            public string PlayerId;
            public double Score;
            public int Rank;
            public string UpdatedTime;
        }

        static readonly string[] SORT_VALUES = { "desc", "asc" };
        static readonly string[] SORT_LABELS = { "높은순 (desc)", "낮은순 (asc)" };
        static readonly string[] UPDATE_VALUES = { "keepBest", "keepLatest", "aggregate" };
        static readonly string[] UPDATE_LABELS = { "최고 기록 유지", "최신 기록", "누적 합산" };
        static readonly string[] STRATEGY_VALUES = { "score", "percent" };
        static readonly string[] STRATEGY_LABELS = { "점수 기준", "퍼센트 기준" };
        static readonly string[] RESET_PRESETS = { "매일", "매주 월요일", "매월 1일", "직접 입력" };
        static readonly string[] RESET_CRONS = { "0 0 * * *", "0 0 * * 1", "0 0 1 * *", "" };

        // ─── 데이터 모델 ─────────────────────────────

        enum SyncState { Synced, LocalOnly, ServerOnly }

        class LBEntry
        {
            public string Id;
            public string Name;
            public string SortOrder = "desc";
            public string UpdateType = "keepBest";
            public SyncState Status;
            public string FilePath;
            public bool IsExpanded;
            public bool IsDirty;

            // 리셋
            public bool HasReset;
            public string ResetStart = "";
            public string ResetSchedule = "";

            // 티어
            public bool HasTiers;
            public string TierStrategy = "score";
            public List<TierEntry> Tiers = new();
        }

        struct TierEntry
        {
            public string Id;
            public float Cutoff;
        }

        // ─── 데이터 로드 ─────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            _columns ??= new ResizableColumns("UGS_LB", new[]
            {
                new ColDef("상태", 36f),
                new ColDef("ID", 130f, resizable: true),
                new ColDef("이름", 0f),
                new ColDef("정렬", 50f),
                new ColDef("리셋", 50f),
                new ColDef("", 44f),
            });

            ResolveLBDir();
            ScanLocalFiles();

            _isLoading = true;
            UGSCliRunner.RunAsync("leaderboards list -j -q", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastError = null;
                    _lastRefreshTime = DateTime.Now;
                    MergeServerData(result.Output);
                }
                else
                    Debug.LogWarning($"[UGS] leaderboards list 실패: {result.Error}");
            });
        }

        void ResolveLBDir()
        {
            string projectRoot = Application.dataPath.Replace("/Assets", "");
            foreach (var dir in Directory.GetDirectories(Application.dataPath, "UGS", SearchOption.AllDirectories))
            {
                if (dir.Contains("Library") || dir.Contains("Temp") || dir.Contains("PackageCache")) continue;
                string lbDir = Path.Combine(dir, "Leaderboards");
                if (Directory.Exists(lbDir) || File.Exists(Path.Combine(dir, "RemoteConfig.rc")))
                {
                    _lbDir = Path.Combine(dir, "Leaderboards");
                    return;
                }
            }
            _lbDir = Path.Combine(projectRoot, "Assets/UGS/Leaderboards");
        }

        // ─── 로컬 스캔 ──────────────────────────────

        void ScanLocalFiles()
        {
            _entries.Clear();
            if (!Directory.Exists(_lbDir)) return;

            foreach (var file in Directory.GetFiles(_lbDir, "*.lb"))
                _entries.Add(ParseLBFile(file));
        }

        LBEntry ParseLBFile(string filePath)
        {
            var entry = new LBEntry
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Status = SyncState.LocalOnly
            };

            try
            {
                string json = File.ReadAllText(filePath);
                entry.Name = ExtractStr(json, "Name");
                entry.SortOrder = ExtractStr(json, "SortOrder");
                entry.UpdateType = ExtractStr(json, "UpdateType");

                // 리셋 설정
                if (json.Contains("\"ResetConfig\""))
                {
                    entry.HasReset = true;
                    string resetBlock = ExtractObject(json, "ResetConfig");
                    entry.ResetStart = ExtractStr(resetBlock, "Start");
                    entry.ResetSchedule = ExtractStr(resetBlock, "Schedule");
                }

                // 티어 설정
                if (json.Contains("\"TieringConfig\""))
                {
                    entry.HasTiers = true;
                    string tierBlock = ExtractObject(json, "TieringConfig");
                    entry.TierStrategy = ExtractStr(tierBlock, "Strategy");

                    string tiersArr = ExtractArray(tierBlock, "Tiers");
                    if (!string.IsNullOrEmpty(tiersArr))
                    {
                        int sf = 0;
                        while (true)
                        {
                            int os = tiersArr.IndexOf('{', sf); if (os < 0) break;
                            int oe = tiersArr.IndexOf('}', os); if (oe < 0) break;
                            string obj = tiersArr.Substring(os, oe - os + 1);
                            entry.Tiers.Add(new TierEntry
                            {
                                Id = ExtractStr(obj, "Id"),
                                Cutoff = ExtractFloat(obj, "Cutoff")
                            });
                            sf = oe + 1;
                        }
                    }
                }

                if (string.IsNullOrEmpty(entry.SortOrder)) entry.SortOrder = "desc";
                if (string.IsNullOrEmpty(entry.UpdateType)) entry.UpdateType = "keepBest";
            }
            catch { /* ignore */ }

            return entry;
        }

        // ─── 서버 병합 ──────────────────────────────

        void MergeServerData(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            // 배열 직접 파싱 (서버 응답이 [{...},{...}] 형태)
            string block = json.Trim();
            if (block.StartsWith("[")) block = block.Substring(1, block.Length - 2);

            var serverIds = new HashSet<string>();
            int searchFrom = 0;
            while (true)
            {
                int objStart = block.IndexOf('{', searchFrom);
                if (objStart < 0) break;
                int objEnd = JsonFindBrace(block, objStart);
                if (objEnd < 0) break;
                string obj = block.Substring(objStart, objEnd - objStart + 1);

                // 서버는 camelCase (id, name, sortOrder 등)
                string id = ExtractStr(obj, "id");
                if (string.IsNullOrEmpty(id)) id = ExtractStr(obj, "Id");
                if (string.IsNullOrEmpty(id)) { searchFrom = objEnd + 1; continue; }

                serverIds.Add(id);

                var existing = _entries.FirstOrDefault(e => e.Id == id);
                if (existing != null)
                {
                    existing.Status = SyncState.Synced;
                    if (string.IsNullOrEmpty(existing.Name))
                    {
                        string name = ExtractStr(obj, "name");
                        if (string.IsNullOrEmpty(name)) name = ExtractStr(obj, "Name");
                        existing.Name = name;
                    }
                }
                else
                {
                    string name = ExtractStr(obj, "name");
                    if (string.IsNullOrEmpty(name)) name = ExtractStr(obj, "Name");
                    _entries.Add(new LBEntry
                    {
                        Id = id,
                        Name = name,
                        Status = SyncState.ServerOnly
                    });
                }

                searchFrom = objEnd + 1;
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
            DrawManageSection();

            if (!string.IsNullOrEmpty(_lbDir))
                DrawEnvCopySection("leaderboards", _lbDir, onComplete: () => FetchData());
        }

        // ─── 툴바 ──────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawColorBtn("Refresh", COL_INFO, 22)) FetchData();

            bool hasDirty = _entries.Any(e => e.IsDirty);
            GUI.enabled = hasDirty;
            if (DrawColorBtn("Save All", COL_WARN, 22)) SaveAllDirty();
            GUI.enabled = true;

            if (DrawColorBtn("Deploy ↑", COL_SUCCESS, 22)) DeployLB();

            GUILayout.Space(8);
            int lc = _entries.Count(e => e.Status != SyncState.ServerOnly);
            int sc = _entries.Count(e => e.Status != SyncState.LocalOnly);
            EditorGUILayout.LabelField($"로컬: {lc} / 서버: {sc}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleLeft },
                GUILayout.Width(110));

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

            if (_lastRefreshTime != default)
            {
                var el = DateTime.Now - _lastRefreshTime;
                string t = el.TotalSeconds < 60 ? $"{el.Seconds}초 전" : $"{(int)el.TotalMinutes}분 전";
                EditorGUILayout.LabelField(t, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleRight }, GUILayout.Width(50));
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── 리더보드 목록 ──────────────────────────

        void DrawList()
        {
            if (!DrawSectionFoldout(ref _foldList, $"Leaderboards ({_entries.Count})", TabColor)) return;
            BeginBody();

            if (_entries.Count == 0)
            {
                EditorGUILayout.LabelField("리더보드 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
            else
            {
                _columns.DrawHeader();
                for (int i = 0; i < _entries.Count; i++)
                    DrawRow(_entries[i], i);
            }

            EndBody();
        }

        void DrawRow(LBEntry entry, int index)
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

            // ID
            DrawCellLabel(entry.Id, _columns.GetWidth(COL_ID));

            // 이름 (편집)
            if (entry.Status != SyncState.ServerOnly)
            {
                string nn = EditorGUILayout.TextField(entry.Name ?? "");
                if (nn != (entry.Name ?? "")) { entry.Name = nn; entry.IsDirty = true; }
            }
            else
                DrawCellLabel(entry.Name ?? "", 0);

            // 정렬
            DrawCellLabel(entry.SortOrder, _columns.GetWidth(COL_SORT),
                entry.SortOrder == "desc" ? new Color(0.90f, 0.45f, 0.55f) : COL_INFO);

            // 리셋
            string resetLabel = entry.HasReset ? CronToLabel(entry.ResetSchedule) : "—";
            DrawCellLabel(resetLabel, _columns.GetWidth(COL_RESET), entry.HasReset ? COL_WARN : COL_MUTED);

            // 액션
            bool hasDetail = entry.Status != SyncState.ServerOnly;
            if (hasDetail)
            {
                if (GUILayout.Button(entry.IsExpanded ? "▾" : "▸", EditorStyles.miniButton,
                    GUILayout.Width(18), GUILayout.Height(16)))
                    entry.IsExpanded = !entry.IsExpanded;
            }

            if (entry.Status == SyncState.ServerOnly)
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                    if (EditorUtility.DisplayDialog("삭제", $"서버에서 '{entry.Id}'를 삭제하시겠습니까?", "삭제", "취소"))
                        DeleteServer(entry.Id);
            }
            else
            {
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                    if (EditorUtility.DisplayDialog("삭제", $"'{entry.Id}.lb' 파일을 삭제하시겠습니까?", "삭제", "취소"))
                        DeleteLocal(entry);
            }

            EditorGUILayout.EndHorizontal();

            // 인라인 편집
            if (entry.IsExpanded && entry.Status != SyncState.ServerOnly)
                DrawInlineEdit(entry);

            EditorGUILayout.EndVertical();
        }

        // ─── 인라인 편집 ────────────────────────────

        void DrawInlineEdit(LBEntry entry)
        {
            // 업데이트 방식
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("업데이트:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(52));
            int utIdx = Array.IndexOf(UPDATE_VALUES, entry.UpdateType);
            if (utIdx < 0) utIdx = 0;
            int newUt = EditorGUILayout.Popup(utIdx, UPDATE_LABELS, GUILayout.Width(110));
            if (newUt != utIdx) { entry.UpdateType = UPDATE_VALUES[newUt]; entry.IsDirty = true; }

            GUILayout.Space(16);
            EditorGUILayout.LabelField("정렬:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(30));
            int soIdx = Array.IndexOf(SORT_VALUES, entry.SortOrder);
            if (soIdx < 0) soIdx = 0;
            int newSo = EditorGUILayout.Popup(soIdx, SORT_LABELS, GUILayout.Width(110));
            if (newSo != soIdx) { entry.SortOrder = SORT_VALUES[newSo]; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // 리셋 설정
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            bool newHasReset = EditorGUILayout.ToggleLeft("리셋", entry.HasReset, GUILayout.Width(50));
            if (newHasReset != entry.HasReset) { entry.HasReset = newHasReset; entry.IsDirty = true; }

            EditorGUILayout.EndHorizontal();

            if (entry.HasReset)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(94);
                EditorGUILayout.LabelField("시작일:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(40));
                string ns = EditorGUILayout.TextField(entry.ResetStart, GUILayout.Width(180));
                if (ns != entry.ResetStart) { entry.ResetStart = ns; entry.IsDirty = true; }

                GUILayout.Space(8);
                EditorGUILayout.LabelField("주기:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(28));
                string nc = EditorGUILayout.TextField(entry.ResetSchedule, GUILayout.Width(100));
                if (nc != entry.ResetSchedule) { entry.ResetSchedule = nc; entry.IsDirty = true; }
                EditorGUILayout.EndHorizontal();
            }

            // 티어 설정
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            bool newHasTiers = EditorGUILayout.ToggleLeft("티어", entry.HasTiers, GUILayout.Width(50));
            if (newHasTiers != entry.HasTiers) { entry.HasTiers = newHasTiers; entry.IsDirty = true; }
            if (entry.HasTiers)
            {
                EditorGUILayout.LabelField("전략:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(30));
                int stIdx = Array.IndexOf(STRATEGY_VALUES, entry.TierStrategy);
                if (stIdx < 0) stIdx = 0;
                int newSt = EditorGUILayout.Popup(stIdx, STRATEGY_LABELS, GUILayout.Width(90));
                if (newSt != stIdx) { entry.TierStrategy = STRATEGY_VALUES[newSt]; entry.IsDirty = true; }
            }
            EditorGUILayout.EndHorizontal();

            if (entry.HasTiers)
            {
                // 도움말
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(94);
                string helpText = entry.TierStrategy == "score"
                    ? "점수 기준: Cutoff 이상이면 해당 등급. 마지막 등급은 Cutoff 0 (나머지 전부)"
                    : "퍼센트 기준: 상위 N% 이내면 해당 등급. 예) Gold Cutoff=10 → 상위 10%";
                EditorGUILayout.LabelField(helpText, new GUIStyle(EditorStyles.helpBox)
                    { fontSize = 10, wordWrap = true });
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
                for (int i = 0; i < entry.Tiers.Count; i++)
                {
                    var tier = entry.Tiers[i];
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(94);

                    string newTid = EditorGUILayout.TextField(tier.Id, GUILayout.Width(80));
                    if (newTid != tier.Id) { tier.Id = newTid; entry.IsDirty = true; }

                    EditorGUILayout.LabelField("cutoff:", new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = COL_MUTED } }, GUILayout.Width(42));
                    float newCut = EditorGUILayout.FloatField(tier.Cutoff, GUILayout.Width(60));
                    if (Math.Abs(newCut - tier.Cutoff) > 0.001f) { tier.Cutoff = newCut; entry.IsDirty = true; }

                    entry.Tiers[i] = tier;

                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(14)))
                    { entry.Tiers.RemoveAt(i); entry.IsDirty = true; i--; }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(94);
                if (GUILayout.Button("+ tier", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(14)))
                {
                    entry.Tiers.Add(new TierEntry { Id = $"Tier{entry.Tiers.Count + 1}", Cutoff = 0 });
                    entry.IsDirty = true;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ─── 저장 ──────────────────────────────────

        void SaveAllDirty()
        {
            foreach (var e in _entries.Where(e => e.IsDirty && e.FilePath != null))
                SaveFile(e);
            _lastSuccess = "저장 완료";
        }

        void SaveFile(LBEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/leaderboards.schema.json\",");
            sb.AppendLine($"  \"SortOrder\": \"{entry.SortOrder}\",");
            sb.AppendLine($"  \"UpdateType\": \"{entry.UpdateType}\",");
            sb.AppendLine($"  \"Name\": \"{entry.Name ?? entry.Id}\",");

            if (entry.HasReset)
            {
                sb.AppendLine("  \"ResetConfig\": {");
                sb.AppendLine($"    \"Start\": \"{entry.ResetStart}\",");
                sb.AppendLine($"    \"Schedule\": \"{entry.ResetSchedule}\"");
                sb.Append("  }");
                sb.AppendLine(entry.HasTiers ? "," : "");
            }

            if (entry.HasTiers && entry.Tiers.Count > 0)
            {
                sb.AppendLine("  \"TieringConfig\": {");
                sb.AppendLine($"    \"Strategy\": \"{entry.TierStrategy}\",");
                sb.AppendLine("    \"Tiers\": [");
                for (int i = 0; i < entry.Tiers.Count; i++)
                {
                    var t = entry.Tiers[i];
                    string cutoffPart = t.Cutoff > 0 ? $", \"Cutoff\": {t.Cutoff}" : "";
                    string comma = i < entry.Tiers.Count - 1 ? "," : "";
                    sb.AppendLine($"      {{\"Id\": \"{t.Id}\"{cutoffPart}}}{comma}");
                }
                sb.AppendLine("    ]");
                sb.AppendLine("  }");
            }

            sb.AppendLine("}");
            File.WriteAllText(entry.FilePath, sb.ToString());
            entry.IsDirty = false;
        }

        // ─── 새 리더보드 ────────────────────────────

        void DrawCreateSection()
        {
            if (!DrawSectionFoldout(ref _foldCreate, "새 리더보드", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("ID:", GUILayout.Width(35));
            _newId = EditorGUILayout.TextField(_newId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("이름:", GUILayout.Width(35));
            _newName = EditorGUILayout.TextField(_newName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("정렬:", GUILayout.Width(35));
            _newSortIdx = EditorGUILayout.Popup(_newSortIdx, SORT_LABELS, GUILayout.Width(110));
            GUILayout.Space(8);
            EditorGUILayout.LabelField("업데이트:", GUILayout.Width(50));
            _newUpdateIdx = EditorGUILayout.Popup(_newUpdateIdx, UPDATE_LABELS, GUILayout.Width(110));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            _newHasReset = EditorGUILayout.ToggleLeft("리셋 주기", _newHasReset, GUILayout.Width(70));
            if (_newHasReset)
            {
                _newResetPresetIdx = EditorGUILayout.Popup(_newResetPresetIdx, RESET_PRESETS, GUILayout.Width(100));
                if (_newResetPresetIdx < 3) _newResetCron = RESET_CRONS[_newResetPresetIdx];
                else _newResetCron = EditorGUILayout.TextField(_newResetCron, GUILayout.Width(100));
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool isDup = _entries.Any(e => e.Id.Equals(_newId?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
            GUI.enabled = !string.IsNullOrWhiteSpace(_newId) && !isDup;
            if (GUILayout.Button("+ 생성", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(18)))
                CreateLB();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDup && !string.IsNullOrWhiteSpace(_newId))
                EditorGUILayout.LabelField("이미 존재하는 ID입니다",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_ERROR } });

            EndBody();
        }

        void CreateLB()
        {
            if (!Directory.Exists(_lbDir)) Directory.CreateDirectory(_lbDir);

            string id = _newId.Trim().ToUpper();
            string filePath = Path.Combine(_lbDir, $"{id}.lb");

            var entry = new LBEntry
            {
                Id = id,
                Name = string.IsNullOrEmpty(_newName) ? id : _newName.Trim(),
                SortOrder = SORT_VALUES[_newSortIdx],
                UpdateType = UPDATE_VALUES[_newUpdateIdx],
                Status = SyncState.LocalOnly,
                FilePath = filePath,
                HasReset = _newHasReset,
                ResetStart = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddT00:00:00Z"),
                ResetSchedule = _newHasReset ? _newResetCron : ""
            };

            SaveFile(entry);
            AssetDatabase.Refresh();
            _newId = "";
            _newName = "";
            ScanLocalFiles();
        }

        // ─── 관리 (리셋/Dashboard) ───────────────────

        void DrawManageSection()
        {
            if (!DrawSectionFoldout(ref _foldManage, "순위 / 관리", TabColor)) return;
            BeginBody();

            var serverEntries = _entries.Where(e => e.Status is SyncState.Synced or SyncState.ServerOnly).ToList();
            if (serverEntries.Count == 0)
            {
                EditorGUILayout.LabelField("서버에 리더보드 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                EndBody();
                return;
            }

            string[] names = serverEntries.Select(e => e.Id).ToArray();
            if (_manageIdx >= names.Length) _manageIdx = 0;

            // 선택 + 버튼
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("리더보드:", GUILayout.Width(60));
            int prevIdx = _manageIdx;
            _manageIdx = EditorGUILayout.Popup(_manageIdx, names);
            if (_manageIdx != prevIdx) { _scoresLoaded = false; _scores.Clear(); }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (DrawColorBtn("순위 조회", COL_INFO, 22))
            { _scorePage = 0; FetchScores(names[_manageIdx]); }
            if (DrawColorBtn("리셋", COL_ERROR, 22))
            {
                if (EditorUtility.DisplayDialog("리더보드 리셋",
                    $"'{names[_manageIdx]}'의 모든 점수를 초기화하시겠습니까?", "리셋", "취소"))
                    ResetLB(names[_manageIdx]);
            }
            GUILayout.FlexibleSpace();
            if (DrawLinkBtn($"{names[_manageIdx]} Dashboard"))
            {
                if (UGSConfig.IsConfigured)
                {
                    var pid = UGSCliRunner.GetProjectId();
                    var eid = UGSCliRunner.GetEnvironmentId();
                    if (!string.IsNullOrEmpty(pid))
                    {
                        string url = UGSConfig.GetDashboardUrl(pid, eid, $"leaderboards/leaderboard/{names[_manageIdx]}");
                        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 순위 테이블
            if (_scoresLoaded)
                DrawScoreTable(names[_manageIdx]);

            EndBody();
        }

        // ─── 순위 조회 ──────────────────────────────

        void FetchScores(string lbId)
        {
            int offset = _scorePage * PAGE_SIZE;
            string path = $"/leaderboards/v1/projects/{{pid}}/environments/{{eid}}/leaderboards/{lbId}/scores?limit={PAGE_SIZE}&offset={offset}";

            _isLoading = true;
            UGSCliRunner.RestGet(path, (ok, body) =>
            {
                _isLoading = false;
                if (!ok) { _lastError = $"순위 조회 실패: {body}"; return; }

                ParseScores(body);
                _scoresLoaded = true;
            });
        }

        void ParseScores(string json)
        {
            _scores.Clear();
            _scoreTotal = ExtractIntVal(json, "total");

            int arrStart = json.IndexOf('[');
            int arrEnd = arrStart >= 0 ? JsonFindBracket(json, arrStart) : -1;
            if (arrStart < 0 || arrEnd < 0) return;
            string arr = json.Substring(arrStart, arrEnd - arrStart + 1);

            int sf = 0;
            while (true)
            {
                int os = arr.IndexOf('{', sf); if (os < 0) break;
                int oe = JsonFindBrace(arr, os); if (oe < 0) break;
                string obj = arr.Substring(os, oe - os + 1);

                string pid = ExtractStr(obj, "playerId");
                if (string.IsNullOrEmpty(pid)) { sf = oe + 1; continue; }

                _scores.Add(new ScoreEntry
                {
                    PlayerId = pid,
                    Score = ExtractDouble(obj, "score"),
                    Rank = ExtractIntVal(obj, "rank"),
                    UpdatedTime = ExtractStr(obj, "updatedTime")
                });
                sf = oe + 1;
            }
        }

        void DrawScoreTable(string lbId)
        {
            GUILayout.Space(6);

            if (_scores.Count == 0)
            {
                EditorGUILayout.LabelField(_scoreTotal == 0 ? "등록된 점수 없음" : "해당 페이지에 데이터 없음",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
            else
            {
                // 헤더
                EditorGUILayout.BeginHorizontal();
                DrawHeaderLabel("순위", 36);
                DrawHeaderLabel("Player ID");
                DrawHeaderLabel("점수", 80);
                DrawHeaderLabel("시간", 60);
                EditorGUILayout.EndHorizontal();

                for (int i = 0; i < _scores.Count; i++)
                    DrawScoreRow(_scores[i], i);
            }

            // 페이징
            if (_scoreTotal > PAGE_SIZE)
            {
                GUILayout.Space(4);
                int totalPages = Mathf.CeilToInt((float)_scoreTotal / PAGE_SIZE);
                int startNum = _scorePage * PAGE_SIZE + 1;
                int endNum = Mathf.Min(startNum + PAGE_SIZE - 1, _scoreTotal);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _scorePage > 0;
                if (GUILayout.Button("◀ 이전", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(16)))
                { _scorePage--; FetchScores(lbId); }
                GUI.enabled = true;

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"{startNum}-{endNum} / {_scoreTotal}",
                    new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = COL_MUTED } });
                GUILayout.FlexibleSpace();

                GUI.enabled = _scorePage < totalPages - 1;
                if (GUILayout.Button("다음 ▶", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(16)))
                { _scorePage++; FetchScores(lbId); }
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawScoreRow(ScoreEntry entry, int index)
        {
            var bg = index % 2 == 0 ? BG_CARD : BG_SECTION;
            EditorGUILayout.BeginHorizontal(GetBgStyle(bg), GUILayout.Height(18));

            // 순위 (1~3위 메달)
            string rankText; Color rankColor;
            switch (entry.Rank)
            {
                case 1: rankText = "1st"; rankColor = new Color(1f, 0.84f, 0f); break;
                case 2: rankText = "2nd"; rankColor = new Color(0.75f, 0.75f, 0.75f); break;
                case 3: rankText = "3rd"; rankColor = new Color(0.80f, 0.50f, 0.20f); break;
                default: rankText = $"{entry.Rank}"; rankColor = COL_MUTED; break;
            }
            EditorGUILayout.LabelField(rankText, new GUIStyle(EditorStyles.label)
            {
                fontStyle = entry.Rank <= 3 ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = rankColor }
            }, GUILayout.Width(36));

            // Player ID (우클릭 복사)
            string shortId = entry.PlayerId.Length > 20 ? entry.PlayerId[..20] + "..." : entry.PlayerId;
            var idRect = GUILayoutUtility.GetRect(new GUIContent(shortId), EditorStyles.miniLabel);
            EditorGUI.LabelField(idRect, shortId, new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_LINK } });

            // 밑줄 + 커서
            EditorGUI.DrawRect(new Rect(idRect.x, idRect.yMax - 1, idRect.width, 1),
                new Color(COL_LINK.r, COL_LINK.g, COL_LINK.b, 0.3f));
            EditorGUIUtility.AddCursorRect(idRect, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && idRect.Contains(Event.current.mousePosition))
            {
                var menu = new GenericMenu();
                string fullId = entry.PlayerId;
                menu.AddItem(new GUIContent("ID 복사"), false, () => EditorGUIUtility.systemCopyBuffer = fullId);
                menu.ShowAsContext();
                Event.current.Use();
            }

            // 점수 (천 단위 쉼표)
            string scoreText = entry.Score % 1 == 0
                ? ((long)entry.Score).ToString("N0")
                : entry.Score.ToString("N2");
            EditorGUILayout.LabelField(scoreText, new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = entry.Rank <= 3 ? Color.white : COL_MUTED }
            }, GUILayout.Width(80));

            // 시간
            string timeLabel = "";
            if (!string.IsNullOrEmpty(entry.UpdatedTime) && entry.UpdatedTime.Length >= 10)
                timeLabel = entry.UpdatedTime[5..10]; // MM-DD
            DrawCellLabel(timeLabel, 60, COL_MUTED);

            EditorGUILayout.EndHorizontal();
        }

        static double ExtractDouble(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return 0;
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return 0;
            int s = ci + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            int e = s;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-' || json[e] == 'E' || json[e] == 'e' || json[e] == '+')) e++;
            return double.TryParse(json.Substring(s, e - s), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        static int ExtractIntVal(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return 0;
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return 0;
            int s = ci + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            int e = s;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '-')) e++;
            return int.TryParse(json.Substring(s, e - s), out int v) ? v : 0;
        }

        // ─── Deploy / Delete / Reset ─────────────────

        void DeployLB()
        {
            if (!Directory.Exists(_lbDir)) { _lastError = "Leaderboards 폴더가 없습니다."; return; }
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;
            string dir = _lbDir.Replace('\\', '/');

            UGSCliRunner.RunAsync($"deploy \"{dir}\" -s leaderboards", result =>
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

        void DeleteServer(string id)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"leaderboards delete {id}", result =>
            {
                _isLoading = false;
                if (result.Success) { _lastSuccess = $"'{id}' 삭제 완료"; FetchData(); }
                else _lastError = $"삭제 실패: {result.Error}";
            });
        }

        void DeleteLocal(LBEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath))
            {
                File.Delete(entry.FilePath);
                string meta = entry.FilePath + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
            AssetDatabase.Refresh();
            ScanLocalFiles();
        }

        void ResetLB(string id)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"leaderboards reset {id}", result =>
            {
                _isLoading = false;
                if (result.Success) _lastSuccess = $"'{id}' 리셋 완료 (점수 초기화)";
                else _lastError = $"리셋 실패: {result.Error}";
            });
        }

        // ─── 유틸 ──────────────────────────────────

        static string CronToLabel(string cron)
        {
            if (string.IsNullOrEmpty(cron)) return "—";
            if (cron == "0 0 * * *") return "매일";
            if (cron.StartsWith("0 0 * * ")) return "매주";
            if (cron.StartsWith("0 0 1 * *")) return "매월";
            return "커스텀";
        }

        static string ExtractStr(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return "";
            int qs = json.IndexOf('"', ci + 1);
            if (qs < 0) return "";
            int qe = json.IndexOf('"', qs + 1);
            return qe > qs ? json.Substring(qs + 1, qe - qs - 1) : "";
        }

        static float ExtractFloat(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return 0;
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return 0;
            int s = ci + 1;
            while (s < json.Length && (json[s] == ' ' || json[s] == '\t')) s++;
            int e = s;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '.' || json[e] == '-')) e++;
            return float.TryParse(json.Substring(s, e - s), out float v) ? v : 0;
        }

        static string ExtractObject(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int bs = json.IndexOf('{', ki + key.Length);
            if (bs < 0) return "";
            int be = JsonFindBrace(json, bs);
            return json.Substring(bs, be - bs + 1);
        }

        static string ExtractArray(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int as_ = json.IndexOf('[', ki);
            if (as_ < 0) return "";
            int ae = JsonFindBracket(json, as_);
            return json.Substring(as_, ae - as_ + 1);
        }

    }
}
#endif
