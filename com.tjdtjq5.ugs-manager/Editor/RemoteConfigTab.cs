#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>Remote Config 탭 v2. 그룹탭, enum 스키마, 타입별 LIST, 검색, 키 관리.</summary>
    public class RemoteConfigTab : UGSTabBase
    {
        public override string TabName => "Config";
        public override Color TabColor => new(0.95f, 0.75f, 0.20f);
        protected override string DashboardPath => "remote-config/configs";

        // ─── 데이터 ──────────────────────────────────
        List<ConfigEntry> _entries = new();
        SchemaData _schema;
        string _rcFilePath = "";
        string _schemaFilePath = "";
        RemoteConfigEnumEditor _enumEditor;

        // ─── UI 상태 ────────────────────────────────
        int _activeGroupIdx;
        string _searchFilter = "";
        bool _foldAdd, _foldEnum, _foldFile;

        // 키 추가
        string _newKey = "";
        string _newValue = "";
        int _newTypeIdx;
        bool _newIsList;

        // 컬럼
        ResizableColumns _columns;
        const int COL_KEY = 0, COL_TYPE = 1, COL_LIST = 2, COL_VAL = 3, COL_ACT = 4;

        // ─── 데이터 로드 ────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            // 컬럼 너비 + 로그 복원
            _columns ??= new ResizableColumns("UGS_RC", new[]
            {
                new ColDef("키",   160f, resizable: true),
                new ColDef("타입",  60f, resizable: true),
                new ColDef("[]",   18f),
                new ColDef("값",    0f),   // flex
                new ColDef("",     60f),   // 액션
            });
            if (string.IsNullOrEmpty(_rcFilePath) || !File.Exists(_rcFilePath))
                _rcFilePath = FindFile("*.rc", "entries");
            if (string.IsNullOrEmpty(_rcFilePath)) { _lastError = ".rc 파일을 찾을 수 없습니다."; return; }

            string dir = Path.GetDirectoryName(_rcFilePath);
            string baseName = Path.GetFileNameWithoutExtension(_rcFilePath);
            _schemaFilePath = Path.Combine(dir!, $"{baseName}.schema.json");

            try
            {
                string rcJson = File.ReadAllText(_rcFilePath);
                var rawEntries = ParseRcEntries(rcJson);
                var rawTypes = ParseRcTypes(rcJson);

                _schema = File.Exists(_schemaFilePath)
                    ? RemoteConfigSchema.Parse(File.ReadAllText(_schemaFilePath))
                    : new SchemaData();

                BuildEntries(rawEntries, rawTypes);
                _enumEditor = new RemoteConfigEnumEditor(_schema, _schemaFilePath);
                _lastRefreshTime = DateTime.Now;
            }
            catch (Exception e) { _lastError = $"파일 읽기 실패: {e.Message}"; }
        }

        static string FindFile(string pattern, string check)
        {
            string root = Application.dataPath.Replace("/Assets", "");
            foreach (var f in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
            {
                if (f.Contains("Library") || f.Contains("Temp")) continue;
                if (File.ReadAllText(f).Contains(check)) return f;
            }
            return "";
        }

        // ─── .rc 파싱 ──────────────────────────────

        Dictionary<string, string> ParseRcEntries(string json)
        {
            var result = new Dictionary<string, string>();
            int ei = json.IndexOf("\"entries\"", StringComparison.Ordinal);
            if (ei < 0) return result;
            int bs = json.IndexOf('{', ei + 9);
            int be = FindBrace(json, bs);
            string block = json.Substring(bs + 1, be - bs - 1);
            ParseKVPairs(block, result);
            return result;
        }

        Dictionary<string, string> ParseRcTypes(string json)
        {
            var result = new Dictionary<string, string>();
            int ti = json.IndexOf("\"types\"", StringComparison.Ordinal);
            if (ti < 0) return result;
            int bs = json.IndexOf('{', ti + 7);
            int be = FindBrace(json, bs);
            string block = json.Substring(bs + 1, be - bs - 1);
            // types는 항상 string:string
            int idx = 0;
            while (idx < block.Length)
            {
                int ks = block.IndexOf('"', idx); if (ks < 0) break;
                int ke = block.IndexOf('"', ks + 1); if (ke < 0) break;
                string key = block.Substring(ks + 1, ke - ks - 1);
                int vs = block.IndexOf('"', ke + 1); if (vs < 0) break;
                int ve = block.IndexOf('"', vs + 1); if (ve < 0) break;
                result[key] = block.Substring(vs + 1, ve - vs - 1);
                idx = ve + 1;
            }
            return result;
        }

        void ParseKVPairs(string block, Dictionary<string, string> result)
        {
            int idx = 0;
            while (idx < block.Length)
            {
                int ks = block.IndexOf('"', idx); if (ks < 0) break;
                int ke = block.IndexOf('"', ks + 1); if (ke < 0) break;
                string key = block.Substring(ks + 1, ke - ks - 1);
                int colon = block.IndexOf(':', ke); if (colon < 0) break;
                int vs = colon + 1;
                while (vs < block.Length && char.IsWhiteSpace(block[vs])) vs++;
                string val;
                if (vs < block.Length && block[vs] == '"')
                {
                    int ve = block.IndexOf('"', vs + 1);
                    val = block.Substring(vs + 1, ve - vs - 1);
                    idx = ve + 1;
                }
                else
                {
                    int ve = vs;
                    while (ve < block.Length && block[ve] != ',' && block[ve] != '\n' && block[ve] != '}') ve++;
                    val = block.Substring(vs, ve - vs).Trim();
                    idx = ve + 1;
                }
                result[key] = val;
            }
        }

        static int FindBrace(string s, int o)
        {
            int d = 1;
            for (int i = o + 1; i < s.Length; i++)
            { if (s[i] == '{') d++; else if (s[i] == '}') { d--; if (d == 0) return i; } }
            return s.Length - 1;
        }

        // ─── 엔트리 빌드 ───────────────────────────

        void BuildEntries(Dictionary<string, string> raw, Dictionary<string, string> types)
        {
            _entries.Clear();
            foreach (var kv in raw)
            {
                var e = new ConfigEntry { Key = kv.Key, Value = kv.Value, EditValue = kv.Value };

                // 스키마 enum 체크
                if (_schema.EnumMap.TryGetValue(kv.Key, out string enumSchemaKey) &&
                    _schema.EnumDefs.TryGetValue(enumSchemaKey, out var opts))
                {
                    e.DisplayType = "ENUM";
                    e.BaseType = "STRING";
                    e.EnumSchemaKey = enumSchemaKey;
                    e.EnumOptions = opts.ToArray();
                    e.EnumIndex = Array.IndexOf(e.EnumOptions, kv.Value);
                    if (e.EnumIndex < 0) e.EnumIndex = 0;
                }
                // 스키마 list 체크
                else if (_schema.Lists.TryGetValue(kv.Key, out var listInfo))
                {
                    e.DisplayType = listInfo.ItemType; // 아이템 타입이 곧 DisplayType
                    e.BaseType = "STRING";
                    e.IsList = true;
                    e.ListItemType = listInfo.ItemType;
                    e.ListEnumSchemaKey = listInfo.EnumSchema;
                    e.ListItems = string.IsNullOrEmpty(kv.Value) ? new List<string>() : kv.Value.Split(',').Select(s => s.Trim()).ToList();
                    // ENUM 리스트면 스키마 매핑도 설정
                    if (listInfo.ItemType == "ENUM" && !string.IsNullOrEmpty(listInfo.EnumSchema))
                    {
                        e.EnumSchemaKey = listInfo.EnumSchema;
                        if (_schema.EnumDefs.TryGetValue(listInfo.EnumSchema, out var eo))
                            e.EnumOptions = eo.ToArray();
                    }
                }
                else
                {
                    if (types.TryGetValue(kv.Key, out string et)) e.BaseType = et.ToUpper();
                    else if (kv.Value == "true" || kv.Value == "false") e.BaseType = "BOOL";
                    else if (kv.Value.Contains('.')) e.BaseType = "FLOAT";
                    else if (int.TryParse(kv.Value, out _)) e.BaseType = "INT";
                    else e.BaseType = "STRING";
                    e.DisplayType = e.BaseType;
                }

                e.OrigKey = e.Key;
                e.OrigDisplayType = e.DisplayType;
                e.OrigIsList = e.IsList;
                e.OrigEnumSchemaKey = e.EnumSchemaKey;
                e.OrigListItemType = e.ListItemType;
                e.OrigListEnumSchemaKey = e.ListEnumSchemaKey;
                e.OrigListItems = e.ListItems != null ? new List<string>(e.ListItems) : null;
                _entries.Add(e);
            }
        }

        // ─── 저장 ──────────────────────────────────

        void SaveRcFile()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/remote-config.schema.json\",");
            sb.AppendLine("  \"entries\": {");
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                string val;
                if (e.IsList)
                    val = $"\"{string.Join(",", e.ListItems)}\"";
                else
                {
                    string v = string.IsNullOrEmpty(e.EditValue) ? ConfigTypeConverter.GetDefault(e.DisplayType) : e.EditValue;
                    val = e.DisplayType switch
                    {
                        "ENUM" => $"\"{v}\"",
                        "STRING" => $"\"{v}\"",
                        "BOOL" => (v == "true" ? "true" : "false"),
                        "INT" => int.TryParse(v, out var iv) ? iv.ToString() : "0",
                        "FLOAT" => float.TryParse(v, out var fv) ? fv.ToString() : "0",
                        _ => $"\"{v}\""
                    };
                }
                sb.AppendLine($"    \"{e.Key}\": {val}{(i < _entries.Count - 1 ? "," : "")}");
            }
            sb.AppendLine("  },");
            sb.AppendLine("  \"types\": {");
            var typed = _entries.Where(e => e.BaseType is "FLOAT" or "INT" or "BOOL").ToList();
            for (int i = 0; i < typed.Count; i++)
                sb.AppendLine($"    \"{typed[i].Key}\": \"{typed[i].BaseType}\"{(i < typed.Count - 1 ? "," : "")}");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(_rcFilePath, sb.ToString());
        }

        void SaveSchema()
        {
            if (!string.IsNullOrEmpty(_schemaFilePath))
                RemoteConfigSchema.Save(_schemaFilePath, _schema);
        }

        void SaveEntry(ConfigEntry entry)
        {
            // 키 이름 변경 시 중복 검사 + 스키마 업데이트
            if (entry.Key != entry.OrigKey)
            {
                if (_entries.Any(e => e != entry && e.Key == entry.Key))
                {
                    EditorUtility.DisplayDialog("중복 키", $"'{entry.Key}' 키가 이미 존재합니다.", "확인");
                    entry.Key = entry.OrigKey;
                    entry.RecalcDirty();
                    return;
                }
                RemoteConfigSchema.RenameKey(_schema, entry.OrigKey, entry.Key);
                // enumMap/lists도 키 업데이트
                if (_schema.EnumMap.ContainsKey(entry.OrigKey))
                {
                    _schema.EnumMap[entry.Key] = _schema.EnumMap[entry.OrigKey];
                    _schema.EnumMap.Remove(entry.OrigKey);
                }
                if (_schema.Lists.ContainsKey(entry.OrigKey))
                {
                    _schema.Lists[entry.Key] = _schema.Lists[entry.OrigKey];
                    _schema.Lists.Remove(entry.OrigKey);
                }
            }

            // ENUM 스키마 매핑 업데이트
            if (entry.DisplayType == "ENUM" && !string.IsNullOrEmpty(entry.EnumSchemaKey))
                _schema.EnumMap[entry.Key] = entry.EnumSchemaKey;
            else
                _schema.EnumMap.Remove(entry.Key);

            // LIST 스키마 업데이트
            if (entry.IsList)
            {
                _schema.Lists[entry.Key] = new ListSchemaInfo
                {
                    ItemType = entry.DisplayType,
                    EnumSchema = entry.DisplayType == "ENUM" ? entry.EnumSchemaKey : entry.ListEnumSchemaKey
                };
            }
            else
                _schema.Lists.Remove(entry.Key);

            entry.IsEditingKey = false;
            entry.CommitAsOriginal();
            SaveRcFile();
            SaveSchema();
        }

        void DeleteEntry(ConfigEntry entry)
        {
            if (!EditorUtility.DisplayDialog("키 삭제", $"'{entry.Key}'를 삭제하시겠습니까?", "삭제", "취소")) return;
            _entries.Remove(entry);
            RemoteConfigSchema.RemoveKey(_schema, entry.Key);
            SaveRcFile();
            SaveSchema();
        }

        void SaveAll()
        {
            foreach (var e in _entries.Where(e => e.IsDirty)) { e.IsEditingKey = false; e.CommitAsOriginal(); }
            // 스키마도 일괄 갱신
            foreach (var e in _entries)
            {
                if (e.DisplayType == "ENUM" && !string.IsNullOrEmpty(e.EnumSchemaKey))
                    _schema.EnumMap[e.Key] = e.EnumSchemaKey;
                if (e.IsList)
                    _schema.Lists[e.Key] = new ListSchemaInfo { ItemType = e.DisplayType, EnumSchema = e.DisplayType == "ENUM" ? e.EnumSchemaKey : e.ListEnumSchemaKey };
            }
            SaveRcFile();
            SaveSchema();
        }

        // ─── 메인 UI ──────────────────────────────

        public override void OnDraw()
        {
            DrawMainToolbar();
            DrawError();
            DrawSuccess();
            DrawLoading();
            if (_isLoading) return;

            GUILayout.Space(2);
            DrawGroupTabs();
            GUILayout.Space(2);

            var visible = GetVisibleEntries();
            DrawTableHeader();
            for (int i = 0; i < visible.Count; i++)
                DrawEntryRow(visible[i], i);

            if (visible.Count == 0)
            {
                GUILayout.Space(16);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(_searchFilter) ? "키가 없습니다." : "검색 결과 없음",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }

            GUILayout.Space(8);
            DrawAddKeySection();
            DrawEnumSection();
            DrawFileSection();
        }

        // ─── 툴바 ─────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawColorBtn("Refresh", COL_INFO, 22)) FetchData();

            // Save All
            int dirtyCount = _entries.Count(e => e.IsDirty);
            GUI.enabled = dirtyCount > 0;
            if (DrawColorBtn($"Save All{(dirtyCount > 0 ? $" ({dirtyCount})" : "")}", COL_SUCCESS, 22))
                SaveAll();
            GUI.enabled = true;

            if (DrawColorBtn("Deploy ↑", COL_SUCCESS, 22)) PushToServer();

            // 검색
            GUILayout.Space(4);
            EditorGUILayout.LabelField("🔍", GUILayout.Width(18));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, GUILayout.MinWidth(60));

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(DashboardPath) && DrawLinkBtn("Dashboard")) OpenDashboardFromTab();

            if (_lastRefreshTime != default)
            {
                var elapsed = DateTime.Now - _lastRefreshTime;
                string t = elapsed.TotalSeconds < 60 ? $"{elapsed.Seconds}초 전" : $"{(int)elapsed.TotalMinutes}분 전";
                EditorGUILayout.LabelField(t, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleRight }, GUILayout.Width(50));
            }

            EditorGUILayout.EndHorizontal();
        }

        void OpenDashboardFromTab()
        {
            if (!UGSConfig.IsConfigured) return;

            var projectId = UGSCliRunner.GetProjectId();
            if (string.IsNullOrEmpty(projectId)) return;

            var envId = GetEnvId();
            string url = UGSConfig.GetDashboardUrl(projectId, envId, DashboardPath);
            if (!string.IsNullOrEmpty(url))
                Application.OpenURL(url);
        }

        static string GetEnvId()
        {
            var result = UGSCliRunner.RunJson("env list");
            if (!result.Success) return "";
            string activeEnv = UGSCliRunner.GetEnvironment();
            string json = result.Output;
            int sf = 0;
            while (true)
            {
                int os = json.IndexOf('{', sf); if (os < 0) break;
                int oe = json.IndexOf('}', os); if (oe < 0) break;
                string blk = json.Substring(os, oe - os + 1);
                if (ExtractField(blk, "name") == activeEnv) return ExtractField(blk, "id");
                sf = oe + 1;
            }
            return "";
        }

        static string ExtractField(string blk, string field)
        {
            string k = $"\"{field}\"";
            int ki = blk.IndexOf(k, StringComparison.Ordinal); if (ki < 0) return "";
            int ci = blk.IndexOf(':', ki + k.Length); if (ci < 0) return "";
            int s = ci + 1;
            while (s < blk.Length && char.IsWhiteSpace(blk[s])) s++;
            if (s < blk.Length && blk[s] == '"')
            { int e = blk.IndexOf('"', s + 1); return e > s ? blk.Substring(s + 1, e - s - 1) : ""; }
            int ve = s;
            while (ve < blk.Length && blk[ve] != ',' && blk[ve] != '}') ve++;
            return blk.Substring(s, ve - s).Trim();
        }

        // ─── 그룹 탭 ──────────────────────────────

        void DrawGroupTabs()
        {
            if (_schema == null || _schema.Groups.Count == 0) return;
            if (_activeGroupIdx >= _schema.Groups.Count) _activeGroupIdx = 0;

            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);

            float tabW = rect.width / _schema.Groups.Count;
            for (int i = 0; i < _schema.Groups.Count; i++)
            {
                var g = _schema.Groups[i];
                var tr = new Rect(rect.x + tabW * i, rect.y, tabW, rect.height);
                bool active = _activeGroupIdx == i;
                if (active)
                {
                    EditorGUI.DrawRect(tr, new Color(g.Color.r, g.Color.g, g.Color.b, 0.15f));
                    EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 2, tr.width, 2), g.Color);
                }
                var st = new GUIStyle(EditorStyles.miniLabel)
                { fontSize = 11, alignment = TextAnchor.MiddleCenter, fontStyle = active ? FontStyle.Bold : FontStyle.Normal, normal = { textColor = active ? g.Color : COL_MUTED } };
                EditorGUI.LabelField(tr, g.Name, st);
                if (Event.current.type == EventType.MouseDown && tr.Contains(Event.current.mousePosition))
                { _activeGroupIdx = i; Event.current.Use(); }
            }
        }

        List<ConfigEntry> GetVisibleEntries()
        {
            IEnumerable<ConfigEntry> result = _entries;

            // 그룹 필터
            if (_schema != null && _schema.Groups.Count > 0 && _activeGroupIdx < _schema.Groups.Count)
            {
                var keys = new HashSet<string>(_schema.Groups[_activeGroupIdx].Keys);
                result = result.Where(e => keys.Contains(e.OrigKey) || keys.Contains(e.Key));
            }

            // 검색 필터
            if (!string.IsNullOrEmpty(_searchFilter))
                result = result.Where(e => e.Key.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            return result.ToList();
        }

        // ─── 테이블 헤더 ───────────────────────────

        void DrawTableHeader() => _columns.DrawHeader();

        // ─── 엔트리 행 ─────────────────────────────

        void DrawEntryRow(ConfigEntry entry, int index)
        {
            var bg = entry.IsDirty ? new Color(0.25f, 0.22f, 0.12f) : (index % 2 == 0 ? BG_CARD : BG_SECTION);

            // 메인 행 (항상 1줄)
            EditorGUILayout.BeginVertical(GetBgStyle(bg));
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

            DrawKeyColumn(entry);
            DrawTypeDropdown(entry);
            DrawListCheckbox(entry);

            if (entry.IsList)
                DrawListCollapsedValue(entry);
            else
                DrawValueEditor(entry);

            DrawActionButtons(entry);
            EditorGUILayout.EndHorizontal();

            // 리스트 펼침 (IsExpanded일 때만)
            if (entry.IsList && entry.IsExpanded)
                DrawListExpandedItems(entry);

            EditorGUILayout.EndVertical();

            // 행 구분선
            var line = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.35f, 0.35f, 0.40f, 0.3f));
        }

        // ─── 키 컬럼 (더블클릭 편집) ────────────────

        void DrawKeyColumn(ConfigEntry entry)
        {
            if (entry.IsEditingKey)
            {
                GUI.SetNextControlName($"key_{entry.GetHashCode()}");
                string newKey = EditorGUILayout.TextField(entry.Key, GUILayout.Width(_columns.GetWidth(COL_KEY)));
                if (newKey != entry.Key) { entry.Key = newKey; entry.RecalcDirty(); }

                // Enter 또는 포커스 이탈로 확정
                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                { entry.IsEditingKey = false; Event.current.Use(); GUI.FocusControl(null); }
                else if (Event.current.type == EventType.MouseDown)
                {
                    var rect = GUILayoutUtility.GetLastRect();
                    if (!rect.Contains(Event.current.mousePosition))
                        entry.IsEditingKey = false;
                }
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(_columns.GetWidth(COL_KEY), 18, GUILayout.Width(_columns.GetWidth(COL_KEY)));
                EditorGUI.LabelField(rect, entry.Key, new GUIStyle(EditorStyles.label)
                    { normal = { textColor = entry.Key != entry.OrigKey ? COL_WARN : Color.white } });

                if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && rect.Contains(Event.current.mousePosition))
                { entry.IsEditingKey = true; Event.current.Use(); }
            }
        }

        // ─── 타입 드롭다운 (LIST 제거) ──────────────

        void DrawTypeDropdown(ConfigEntry entry)
        {
            int currentIdx = Array.IndexOf(ConfigTypeConverter.BaseTypes, entry.DisplayType);
            if (currentIdx < 0) currentIdx = 3; // STRING fallback

            int newIdx = EditorGUILayout.Popup(currentIdx, ConfigTypeConverter.BaseTypes, GUILayout.Width(_columns.GetWidth(COL_TYPE)));
            if (newIdx == currentIdx) return;

            string oldType = entry.DisplayType;
            string newType = ConfigTypeConverter.BaseTypes[newIdx];
            entry.DisplayType = newType;
            entry.BaseType = newType == "ENUM" ? "STRING" : newType;
            if (entry.IsList) entry.BaseType = "STRING";

            OnTypeChanged(entry, oldType, newType);
            entry.RecalcDirty();
        }

        void OnTypeChanged(ConfigEntry entry, string oldType, string newType)
        {
            // ENUM 전환
            if (newType == "ENUM")
            {
                var defKeys = _schema?.EnumDefs.Keys.ToArray() ?? Array.Empty<string>();
                if (defKeys.Length > 0)
                {
                    entry.EnumSchemaKey = defKeys[0];
                    entry.EnumOptions = _schema.EnumDefs[defKeys[0]].ToArray();
                }
                else
                {
                    entry.EnumSchemaKey = null;
                    entry.EnumOptions = new[] { entry.EditValue };
                }
                entry.EnumIndex = Array.IndexOf(entry.EnumOptions, entry.EditValue);
                if (entry.EnumIndex < 0) { entry.EnumIndex = 0; if (entry.EnumOptions.Length > 0) entry.EditValue = entry.EnumOptions[0]; }

                // 리스트면 아이템도 변환
                if (entry.IsList && entry.ListItems != null)
                    entry.ListItems = ConfigTypeConverter.ConvertListItems(entry.ListItems, oldType, "ENUM", entry.EnumOptions);
                return;
            }

            // 기본 타입 변환
            entry.EditValue = ConfigTypeConverter.ConvertValue(entry.EditValue, oldType, newType);

            // 리스트면 아이템도 변환
            if (entry.IsList && entry.ListItems != null)
                entry.ListItems = ConfigTypeConverter.ConvertListItems(entry.ListItems, oldType, newType);
        }

        // ─── 리스트 체크박스 ────────────────────────

        void DrawListCheckbox(ConfigEntry entry)
        {
            bool newIsList = EditorGUILayout.Toggle(entry.IsList, GUILayout.Width(_columns.GetWidth(COL_LIST)));
            if (newIsList == entry.IsList) return;

            entry.IsList = newIsList;
            entry.BaseType = entry.IsList || entry.DisplayType == "ENUM" ? "STRING" : entry.DisplayType;

            if (newIsList)
            {
                // 단일 → 리스트: 현재 값을 1개 아이템으로
                string val = entry.EditValue ?? "";
                entry.ListItemType = entry.DisplayType;
                entry.ListItems = string.IsNullOrEmpty(val) ? new List<string>() : new List<string> { val };
                if (entry.DisplayType == "ENUM")
                    entry.ListEnumSchemaKey = entry.EnumSchemaKey;
            }
            else
            {
                // 리스트 → 단일: 첫 번째 아이템 또는 쉼표 결합
                if (entry.ListItems != null && entry.ListItems.Count > 0)
                {
                    entry.EditValue = entry.DisplayType == "STRING"
                        ? string.Join(",", entry.ListItems)
                        : entry.ListItems[0];
                }
                entry.ListItems = null;
                entry.IsExpanded = false;
            }

            entry.RecalcDirty();
        }

        // ─── 값 에디터 (단일) ──────────────────────

        void DrawValueEditor(ConfigEntry entry)
        {
            switch (entry.DisplayType)
            {
                case "BOOL":
                    bool bv = entry.EditValue == "true";
                    bool nb = EditorGUILayout.Toggle(bv);
                    string ns = nb ? "true" : "false";
                    if (ns != entry.EditValue) { entry.EditValue = ns; entry.RecalcDirty(); }
                    break;
                case "ENUM":
                    DrawEnumValueEditor(entry);
                    break;
                case "FLOAT":
                    string fs = EditorGUILayout.TextField(entry.EditValue);
                    if (fs != entry.EditValue && (float.TryParse(fs, out _) || string.IsNullOrEmpty(fs)))
                    { entry.EditValue = fs; entry.RecalcDirty(); }
                    break;
                case "INT":
                    string ist = EditorGUILayout.TextField(entry.EditValue);
                    if (ist != entry.EditValue && (int.TryParse(ist, out _) || string.IsNullOrEmpty(ist)))
                    { entry.EditValue = ist; entry.RecalcDirty(); }
                    break;
                default:
                    string sv = EditorGUILayout.TextField(entry.EditValue);
                    if (sv != entry.EditValue) { entry.EditValue = sv; entry.RecalcDirty(); }
                    break;
            }
        }

        void DrawEnumValueEditor(ConfigEntry entry)
        {
            var schemaNames = _schema?.EnumDefs.Keys.ToArray() ?? Array.Empty<string>();
            if (schemaNames.Length == 0) { DrawCellLabel("(enum 스키마 없음)", 0, COL_MUTED); return; }

            int schemaIdx = Array.IndexOf(schemaNames, entry.EnumSchemaKey);
            if (schemaIdx < 0) schemaIdx = 0;
            int newSchemaIdx = EditorGUILayout.Popup(schemaIdx, schemaNames, GUILayout.Width(90));
            if (newSchemaIdx != schemaIdx)
            {
                entry.EnumSchemaKey = schemaNames[newSchemaIdx];
                entry.EnumOptions = _schema.EnumDefs[entry.EnumSchemaKey].ToArray();
                entry.EnumIndex = 0;
                entry.EditValue = entry.EnumOptions.Length > 0 ? entry.EnumOptions[0] : "";
                entry.RecalcDirty();
            }

            if (entry.EnumOptions != null && entry.EnumOptions.Length > 0)
            {
                int newValIdx = EditorGUILayout.Popup(entry.EnumIndex, entry.EnumOptions);
                if (newValIdx != entry.EnumIndex)
                { entry.EnumIndex = newValIdx; entry.EditValue = entry.EnumOptions[newValIdx]; entry.RecalcDirty(); }
            }
        }

        // ─── 리스트 한 줄 표현 + 펼치기 ─────────────

        void DrawListCollapsedValue(ConfigEntry entry)
        {
            if (entry.ListItems == null || entry.ListItems.Count == 0)
            {
                EditorGUILayout.LabelField("(빈 리스트)", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } });
            }
            else
            {
                string preview = string.Join(", ", entry.ListItems);
                EditorGUILayout.LabelField(preview, new GUIStyle(EditorStyles.label)
                    { normal = { textColor = Color.white } });
            }

            // 펼치기/접기 토글
            string toggleLabel = entry.IsExpanded ? "▾" : "▸";
            if (GUILayout.Button(toggleLabel, EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                entry.IsExpanded = !entry.IsExpanded;

            EditorGUILayout.LabelField($"{entry.ListItems?.Count ?? 0}", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(18));
        }

        void DrawListExpandedItems(ConfigEntry entry)
        {
            if (entry.ListItems == null) return;

            string[] enumOpts = null;
            if (entry.DisplayType == "ENUM" && !string.IsNullOrEmpty(entry.EnumSchemaKey) &&
                _schema?.EnumDefs.TryGetValue(entry.EnumSchemaKey, out var eo) == true)
                enumOpts = eo.ToArray();
            else if (entry.DisplayType == "ENUM" && !string.IsNullOrEmpty(entry.ListEnumSchemaKey) &&
                _schema?.EnumDefs.TryGetValue(entry.ListEnumSchemaKey, out var eo2) == true)
                enumOpts = eo2.ToArray();

            for (int i = 0; i < entry.ListItems.Count; i++)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(16));
                GUILayout.Space(_columns.GetWidth(COL_KEY) + _columns.GetWidth(COL_TYPE) + 20);

                EditorGUILayout.LabelField($"[{i}]", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(24));

                string newItem = DrawListItemEditor(entry.ListItems[i], entry.DisplayType, enumOpts);
                if (newItem != entry.ListItems[i]) { entry.ListItems[i] = newItem; entry.RecalcDirty(); }

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(14)))
                { entry.ListItems.RemoveAt(i); entry.RecalcDirty(); i--; }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal(GUILayout.Height(16));
            GUILayout.Space(_columns.GetWidth(COL_KEY) + _columns.GetWidth(COL_TYPE) + 20);
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(14)))
            {
                string def = ConfigTypeConverter.GetDefault(entry.DisplayType);
                if (entry.DisplayType == "ENUM" && enumOpts is { Length: > 0 }) def = enumOpts[0];
                entry.ListItems.Add(def);
                entry.RecalcDirty();
            }
            EditorGUILayout.EndHorizontal();
        }

        string DrawListItemEditor(string value, string itemType, string[] enumOpts)
        {
            switch (itemType)
            {
                case "BOOL":
                    return EditorGUILayout.Toggle(value == "true") ? "true" : "false";
                case "INT":
                    string iv = EditorGUILayout.TextField(value);
                    return int.TryParse(iv, out _) || string.IsNullOrEmpty(iv) ? iv : value;
                case "FLOAT":
                    string fv = EditorGUILayout.TextField(value);
                    return float.TryParse(fv, out _) || string.IsNullOrEmpty(fv) ? fv : value;
                case "ENUM":
                    if (enumOpts == null || enumOpts.Length == 0) return EditorGUILayout.TextField(value);
                    int idx = Array.IndexOf(enumOpts, value);
                    if (idx < 0) idx = 0;
                    return enumOpts[EditorGUILayout.Popup(idx, enumOpts)];
                default:
                    return EditorGUILayout.TextField(value);
            }
        }

        // ─── 액션 버튼 ─────────────────────────────

        void DrawActionButtons(ConfigEntry entry)
        {
            GUI.enabled = entry.IsDirty;
            if (GUILayout.Button("✓", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                SaveEntry(entry);
            if (GUILayout.Button("↺", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                entry.Revert();
            GUI.enabled = true;

            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                DeleteEntry(entry);
        }

        // ─── 하단 섹션들 ───────────────────────────

        void DrawAddKeySection()
        {
            if (!DrawSectionFoldout(ref _foldAdd, "키 추가", COL_WARN)) return;
            BeginBody();

            _columns.DrawHeader();

            // 입력 행
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            _newKey = EditorGUILayout.TextField(_newKey, GUILayout.Width(_columns.GetWidth(COL_KEY)));
            _newTypeIdx = EditorGUILayout.Popup(_newTypeIdx, ConfigTypeConverter.BaseTypes, GUILayout.Width(_columns.GetWidth(COL_TYPE)));
            _newIsList = EditorGUILayout.Toggle(_newIsList, GUILayout.Width(_columns.GetWidth(COL_LIST)));
            _newValue = EditorGUILayout.TextField(_newValue);

            string trimmedKey = _newKey?.Trim() ?? "";
            bool isDuplicate = _entries.Any(e => e.Key == trimmedKey);
            GUI.enabled = !string.IsNullOrWhiteSpace(_newKey) && !isDuplicate;
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(16)))
            {
                string type = ConfigTypeConverter.BaseTypes[_newTypeIdx];
                // 빈 값이면 타입별 기본값 자동 입력
                string value = string.IsNullOrEmpty(_newValue) ? ConfigTypeConverter.GetDefault(type) : _newValue;
                var ne = new ConfigEntry
                {
                    Key = _newKey.Trim(),
                    BaseType = type == "ENUM" || _newIsList ? "STRING" : type,
                    DisplayType = type,
                    Value = value,
                    EditValue = value,
                    IsList = _newIsList
                };
                if (_newIsList)
                {
                    ne.ListItemType = type;
                    ne.ListItems = string.IsNullOrEmpty(value)
                        ? new List<string>()
                        : value.Split(',').Select(s => s.Trim()).ToList();
                }
                ne.OrigKey = ne.Key;
                ne.OrigDisplayType = ne.DisplayType;
                ne.OrigIsList = ne.IsList;
                ne.CommitAsOriginal();
                _entries.Add(ne);

                // 활성 그룹에 새 키 추가 (그룹 필터에 의해 안 보이는 버그 방지)
                if (_schema != null && _schema.Groups.Count > 0 && _activeGroupIdx < _schema.Groups.Count)
                {
                    var g = _schema.Groups[_activeGroupIdx];
                    var keys = g.Keys.ToList();
                    keys.Add(ne.Key);
                    _schema.Groups[_activeGroupIdx] = new GroupInfo { Name = g.Name, Color = g.Color, Keys = keys.ToArray() };
                }

                // LIST/ENUM 스키마도 저장
                if (ne.IsList)
                {
                    _schema.Lists[ne.Key] = new ListSchemaInfo
                    {
                        ItemType = ne.DisplayType,
                        EnumSchema = ne.DisplayType == "ENUM" ? ne.EnumSchemaKey : null
                    };
                }
                if (ne.DisplayType == "ENUM" && !string.IsNullOrEmpty(ne.EnumSchemaKey))
                    _schema.EnumMap[ne.Key] = ne.EnumSchemaKey;

                SaveRcFile();
                SaveSchema();
                _newKey = ""; _newValue = ""; _newIsList = false;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EndBody();
        }

        void DrawEnumSection()
        {
            if (!DrawSectionFoldout(ref _foldEnum, "Enum 스키마 관리", COL_INFO)) return;
            BeginBody();
            _enumEditor?.Draw();
            EndBody();
        }

        void DrawFileSection()
        {
            if (!DrawSectionFoldout(ref _foldFile, "파일 경로", COL_MUTED)) return;
            BeginBody();
            DrawFileRow(".rc", _rcFilePath);
            DrawFileRow("schema", _schemaFilePath);
            EndBody();
        }

        void DrawFileRow(string label, string path)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(50));
            bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
            EditorGUILayout.LabelField(exists ? path : "(없음)", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = exists ? COL_MUTED : COL_ERROR } });
            if (exists && GUILayout.Button("열기", GUILayout.Width(36)))
            {
                int ai = path.IndexOf("Assets", StringComparison.Ordinal);
                if (ai >= 0)
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path[ai..].Replace('\\', '/'));
                    if (obj != null) EditorGUIUtility.PingObject(obj);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── Deploy ────────────────────────────────

        void PushToServer()
        {
            if (string.IsNullOrEmpty(_rcFilePath)) { _lastError = ".rc 파일 없음"; return; }
            string dir = Path.GetDirectoryName(_rcFilePath)!.Replace('\\', '/');
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            UGSCliRunner.RunAsync($"deploy \"{dir}\" -s remote-config", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastSuccess = "Deploy 완료" + (!string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "");
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"Deploy 실패 (exit {result.ExitCode})");
                    if (!string.IsNullOrEmpty(result.Error)) sb.Append($"\n{result.Error}");
                    if (!string.IsNullOrEmpty(result.Output)) sb.Append($"\n{result.Output}");
                    _lastError = sb.ToString();
                }
            });
        }
    }
}
#endif
