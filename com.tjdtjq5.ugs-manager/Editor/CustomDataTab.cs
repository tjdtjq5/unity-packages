#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// Custom Data 탭. Cloud Save 커스텀 엔티티 데이터 조회/편집/삭제 + 엔티티 목록 + 북마크.
    /// </summary>
    public class CustomDataTab : UGSTabBase
    {
        public override string TabName => "Custom";
        public override Color TabColor => new(0.65f, 0.75f, 0.45f);
        protected override string DashboardPath => "cloud-save/custom-data";

        // ─── 데이터 ──────────────────────────────────
        string[] _entityIds;
        int _entityDropdownIdx;
        string _inputEntityId = "";
        string _activeEntityId = "";
        List<DataItem> _items = new();
        bool _dataLoaded;

        // 북마크
        List<Bookmark> _bookmarks = new();
        const string KEY_BOOKMARKS = "UGS_CD_Bookmarks";

        // ─── UI 상태 ────────────────────────────────
        ResizableColumns _columns;
        const int COL_KEY = 0, COL_VAL = 1, COL_ACT = 2;

        bool _foldData = true;
        bool _foldAdd;
        bool _foldBookmarks = true;
        string _searchFilter = "";

        // 키 추가
        string _newKey = "";
        string _newValue = "";

        // ─── 데이터 모델 ─────────────────────────────

        class DataItem
        {
            public string Key;
            public string Value;
            public string EditValue;
            public bool IsEditing;
            public bool IsExpanded;
            public bool IsJson;
        }

        struct Bookmark
        {
            public string Label;
            public string EntityId;
        }

        // ─── 라이프사이클 ────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            _columns ??= new ResizableColumns("UGS_CD", new[]
            {
                new ColDef("키", 140f, resizable: true),
                new ColDef("값", 0f),
                new ColDef("", 44f),
            });

            LoadBookmarks();
            LoadEntityList();
        }

        // ─── 엔티티 목록 ────────────────────────────

        void LoadEntityList()
        {
            _isLoading = true;
            UGSCliRunner.RunAsync("cs data custom list -j -q", result =>
            {
                _isLoading = false;
                if (!result.Success)
                {
                    _entityIds = Array.Empty<string>();
                    return;
                }

                _lastRefreshTime = DateTime.Now;
                ParseEntityList(result.Output);
            });
        }

        void ParseEntityList(string json)
        {
            var ids = new List<string>();
            int sf = 0;
            while (true)
            {
                // results 배열 내의 문자열 목록
                int qs = json.IndexOf('"', sf);
                if (qs < 0) break;

                // "results" 같은 키는 건너뛰기
                int qe = json.IndexOf('"', qs + 1);
                if (qe < 0) break;
                string val = json.Substring(qs + 1, qe - qs - 1);

                // 키워드가 아닌 실제 ID만 추가
                if (val != "results" && val != "next" && !val.Contains("http") && !string.IsNullOrEmpty(val))
                    ids.Add(val);

                sf = qe + 1;
            }

            _entityIds = ids.ToArray();
            if (_entityDropdownIdx >= _entityIds.Length) _entityDropdownIdx = 0;
        }

        // ─── 북마크 ─────────────────────────────────

        void LoadBookmarks()
        {
            _bookmarks.Clear();
            string raw = EditorPrefs.GetString(KEY_BOOKMARKS, "");
            if (string.IsNullOrEmpty(raw)) return;

            foreach (var part in raw.Split('|'))
            {
                int sep = part.IndexOf(':');
                if (sep < 0) continue;
                _bookmarks.Add(new Bookmark
                {
                    Label = part.Substring(0, sep),
                    EntityId = part.Substring(sep + 1)
                });
            }
        }

        void SaveBookmarks()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _bookmarks.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(_bookmarks[i].Label).Append(':').Append(_bookmarks[i].EntityId);
            }
            EditorPrefs.SetString(KEY_BOOKMARKS, sb.ToString());
        }

        void AddBookmark(string entityId)
        {
            if (_bookmarks.Any(b => b.EntityId == entityId)) return;
            _bookmarks.Add(new Bookmark { Label = entityId, EntityId = entityId });
            SaveBookmarks();
        }

        // ─── 데이터 조회 ────────────────────────────

        void FetchEntityData(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return;
            entityId = entityId.Trim();
            _activeEntityId = entityId;
            _inputEntityId = entityId;
            _isLoading = true;
            _items.Clear();
            _dataLoaded = false;

            UGSCliRunner.RunAsync($"cs data custom get --custom-id {entityId} -j -q", result =>
            {
                _isLoading = false;
                if (!result.Success)
                {
                    _lastError = result.Error.Contains("404") || result.Error.Contains("not found")
                        ? $"엔티티를 찾을 수 없습니다: {entityId}"
                        : $"데이터 조회 실패: {result.Error}";
                    _dataLoaded = false;
                    return;
                }

                ParseItems(result.Output);
                _dataLoaded = true;

                if (_items.Count == 0)
                {
                    _lastError = $"엔티티 없음: {entityId}";
                    _dataLoaded = false;
                }
                else
                    _lastSuccess = $"데이터 조회 완료 ({_items.Count}개 키)";
            });
        }

        void ParseItems(string json)
        {
            _items.Clear();
            if (string.IsNullOrEmpty(json)) return;

            int arrStart = json.IndexOf('[');
            int arrEnd = arrStart >= 0 ? JsonFindBracket(json, arrStart) : -1;
            if (arrStart < 0 || arrEnd < 0) return;
            string arrBlock = json.Substring(arrStart, arrEnd - arrStart + 1);

            int searchFrom = 0;
            while (true)
            {
                int objStart = arrBlock.IndexOf('{', searchFrom);
                if (objStart < 0) break;
                int objEnd = JsonFindBrace(arrBlock, objStart);
                if (objEnd < 0) break;
                string obj = arrBlock.Substring(objStart, objEnd - objStart + 1);

                string key = ExtractStr(obj, "key");
                if (string.IsNullOrEmpty(key)) { searchFrom = objEnd + 1; continue; }

                string value = ExtractValue(obj, "value");
                bool isJson = value.TrimStart().StartsWith("{") || value.TrimStart().StartsWith("[");

                _items.Add(new DataItem
                {
                    Key = key, Value = value, EditValue = value, IsJson = isJson
                });

                searchFrom = objEnd + 1;
            }

            _items.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
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
            DrawEntitySelector();
            GUILayout.Space(4);
            DrawBookmarkSection();

            if (_dataLoaded)
            {
                GUILayout.Space(4);
                DrawDataList();
                GUILayout.Space(8);
                DrawAddSection();
            }
        }

        // ─── 툴바 ──────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawColorBtn("Refresh", COL_INFO, 22))
            {
                if (!string.IsNullOrEmpty(_activeEntityId))
                    FetchEntityData(_activeEntityId);
                else
                    FetchData();
            }

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

        // ─── 엔티티 선택 ────────────────────────────

        void DrawEntitySelector()
        {
            EditorGUILayout.BeginVertical(GetBgStyle(BG_SECTION));

            // 드롭다운 (기존 엔티티 목록)
            if (_entityIds != null && _entityIds.Length > 0)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
                EditorGUILayout.LabelField("기존:", GUILayout.Width(35));
                _entityDropdownIdx = EditorGUILayout.Popup(_entityDropdownIdx, _entityIds);
                if (DrawColorBtn("조회", COL_SUCCESS, 20))
                    FetchEntityData(_entityIds[_entityDropdownIdx]);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            // ID 직접 입력
            EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            EditorGUILayout.LabelField("ID:", GUILayout.Width(35));
            _inputEntityId = EditorGUILayout.TextField(_inputEntityId);

            GUI.enabled = !string.IsNullOrWhiteSpace(_inputEntityId);
            if (DrawColorBtn("조회", COL_SUCCESS, 20))
                FetchEntityData(_inputEntityId);
            GUI.enabled = true;

            GUI.enabled = _dataLoaded && !string.IsNullOrEmpty(_activeEntityId);
            if (GUILayout.Button("☆", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
                AddBookmark(_activeEntityId);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // 현재 조회 중인 엔티티
            if (!string.IsNullOrEmpty(_activeEntityId) && _dataLoaded)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("조회 중:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(45));
                DrawCopyableLabel(_activeEntityId, COL_SUCCESS);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        // ─── 북마크 ─────────────────────────────────

        void DrawBookmarkSection()
        {
            if (_bookmarks.Count == 0) return;
            if (!DrawSectionFoldout(ref _foldBookmarks, $"북마크 ({_bookmarks.Count})", COL_WARN)) return;
            BeginBody();

            for (int i = 0; i < _bookmarks.Count; i++)
            {
                var bm = _bookmarks[i];
                EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

                // 별명
                int idx = i;
                DrawCopyableLabel($"★ {bm.Label}", new Color(0.95f, 0.75f, 0.20f), onRightClick: () =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("별명 복사"), false, () =>
                        EditorGUIUtility.systemCopyBuffer = _bookmarks[idx].Label);
                    menu.AddItem(new GUIContent("별명 수정"), false, () =>
                    {
                        string newLabel = EditorInputDialog.Show("별명 수정", "새 별명:", _bookmarks[idx].Label);
                        if (!string.IsNullOrEmpty(newLabel))
                        {
                            var b = _bookmarks[idx];
                            b.Label = newLabel;
                            _bookmarks[idx] = b;
                            SaveBookmarks();
                        }
                    });
                    menu.ShowAsContext();
                });

                GUILayout.Space(4);
                DrawCopyableLabel(bm.EntityId, COL_LINK);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("조회", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(16)))
                    FetchEntityData(bm.EntityId);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                {
                    _bookmarks.RemoveAt(i);
                    SaveBookmarks();
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EndBody();
        }

        // ─── 특수 라벨 ──────────────────────────────

        void DrawCopyableLabel(string text, Color color, string copyText = null, Action onRightClick = null)
        {
            string toCopy = copyText ?? text;
            var style = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = color }, fontSize = 11 };
            var content = new GUIContent(text);
            float textWidth = style.CalcSize(content).x + 4;
            var rect = GUILayoutUtility.GetRect(textWidth, 16, GUILayout.Width(textWidth));

            var underline = new Rect(rect.x, rect.yMax - 1, rect.width, 1);
            EditorGUI.DrawRect(underline, new Color(color.r, color.g, color.b, 0.35f));

            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.08f));

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            EditorGUI.LabelField(rect, text, style);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rect.Contains(Event.current.mousePosition))
            {
                if (onRightClick != null) onRightClick.Invoke();
                else
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("복사"), false, () => EditorGUIUtility.systemCopyBuffer = toCopy);
                    menu.ShowAsContext();
                }
                Event.current.Use();
            }
        }

        // ─── 데이터 목록 ────────────────────────────

        void DrawDataList()
        {
            if (!DrawSectionFoldout(ref _foldData, $"데이터 ({_items.Count})", TabColor)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("검색:", GUILayout.Width(35));
            _searchFilter = EditorGUILayout.TextField(_searchFilter);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            var filtered = string.IsNullOrEmpty(_searchFilter)
                ? _items
                : _items.Where(i => i.Key.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (filtered.Count == 0)
            {
                EditorGUILayout.LabelField(
                    _items.Count == 0 ? "데이터 없음" : "검색 결과 없음",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
            else
            {
                _columns.DrawHeader();
                for (int i = 0; i < filtered.Count; i++)
                    DrawDataRow(filtered[i], i);
            }

            EndBody();
        }

        void DrawDataRow(DataItem item, int index)
        {
            var bg = item.IsEditing ? new Color(0.25f, 0.22f, 0.12f) : (index % 2 == 0 ? BG_CARD : BG_SECTION);
            EditorGUILayout.BeginVertical(GetBgStyle(bg));

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

            DrawCellLabel(item.Key, _columns.GetWidth(COL_KEY));

            if (item.IsEditing && !item.IsJson)
                item.EditValue = EditorGUILayout.TextField(item.EditValue);
            else if (!item.IsJson)
            {
                string dv = item.Value.Length > 60 ? item.Value[..60] + "..." : item.Value;
                DrawCellLabel(dv, 0, IsNumeric(item.Value) ? COL_INFO : (Color?)null);
            }
            else
            {
                string preview = item.Value.Length > 40 ? item.Value[..40] + "..." : item.Value;
                DrawCellLabel(preview, 0, new Color(0.70f, 0.55f, 0.95f));
            }

            if (item.IsJson)
            {
                if (GUILayout.Button(item.IsExpanded ? "▾" : "▸", EditorStyles.miniButton,
                    GUILayout.Width(18), GUILayout.Height(16)))
                    item.IsExpanded = !item.IsExpanded;
            }
            else if (!item.IsEditing)
            {
                if (GUILayout.Button("✎", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                { item.IsEditing = true; item.EditValue = item.Value; }
            }
            else
            {
                if (GUILayout.Button("✓", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                    SaveItem(item);
            }

            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                if (EditorUtility.DisplayDialog("키 삭제", $"'{item.Key}'를 삭제하시겠습니까?", "삭제", "취소"))
                    DeleteItem(item);

            EditorGUILayout.EndHorizontal();

            if (item.IsJson && item.IsExpanded)
                DrawJsonEditor(item);

            if (item.IsEditing && !item.IsJson)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("취소", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                { item.IsEditing = false; item.EditValue = item.Value; }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        void DrawJsonEditor(DataItem item)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(_columns.GetWidth(COL_KEY) + 4);
            EditorGUILayout.BeginVertical();

            if (!item.IsEditing)
            {
                var st = new GUIStyle(EditorStyles.textArea)
                    { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
                string formatted = FormatJson(item.Value);
                float h = Mathf.Min(st.CalcHeight(new GUIContent(formatted),
                    EditorGUIUtility.currentViewWidth - _columns.GetWidth(COL_KEY) - 60), 200f);
                EditorGUILayout.TextArea(formatted, st, GUILayout.Height(h + 4));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("편집", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                { item.IsEditing = true; item.EditValue = FormatJson(item.Value); }
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                    EditorGUIUtility.systemCopyBuffer = item.Value;
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                var st = new GUIStyle(EditorStyles.textArea)
                    { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.95f, 0.90f, 0.70f) } };
                float h = Mathf.Min(st.CalcHeight(new GUIContent(item.EditValue),
                    EditorGUIUtility.currentViewWidth - _columns.GetWidth(COL_KEY) - 60), 200f);
                item.EditValue = EditorGUILayout.TextArea(item.EditValue, st, GUILayout.Height(Mathf.Max(h + 4, 60)));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("저장", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                    SaveItem(item);
                if (GUILayout.Button("취소", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                { item.IsEditing = false; item.EditValue = item.Value; }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        // ─── 키 추가 ───────────────────────────────

        void DrawAddSection()
        {
            if (!DrawSectionFoldout(ref _foldAdd, "키 추가", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("키:", GUILayout.Width(25));
            _newKey = EditorGUILayout.TextField(_newKey, GUILayout.Width(120));
            EditorGUILayout.LabelField("값:", GUILayout.Width(20));
            _newValue = EditorGUILayout.TextField(_newValue);

            bool isDup = _items.Any(i => i.Key == _newKey?.Trim());
            GUI.enabled = !string.IsNullOrWhiteSpace(_newKey) && !isDup;
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(16)))
                AddItem();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDup && !string.IsNullOrWhiteSpace(_newKey))
                EditorGUILayout.LabelField("이미 존재하는 키입니다",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_ERROR } });

            EndBody();
        }

        // ─── CLI 명령 ──────────────────────────────

        void SaveItem(DataItem item)
        {
            string value = item.EditValue.Trim();
            if (item.IsJson) value = value.Replace("\n", "").Replace("\r", "");
            string ev = value.Replace("\"", "\\\"");

            _isLoading = true;
            UGSCliRunner.RunAsync($"cs data custom set --custom-id {_activeEntityId} --key \"{item.Key}\" --value \"{ev}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                { item.Value = item.EditValue.Trim(); item.IsEditing = false; _lastSuccess = $"'{item.Key}' 저장 완료"; }
                else _lastError = $"저장 실패: {result.Error}";
            });
        }

        void DeleteItem(DataItem item)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"cs data custom set --custom-id {_activeEntityId} --key \"{item.Key}\" --value \"\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                { _items.Remove(item); _lastSuccess = $"'{item.Key}' 삭제 완료"; }
                else _lastError = $"삭제 실패: {result.Error}";
            });
        }

        void AddItem()
        {
            string key = _newKey.Trim();
            string value = string.IsNullOrEmpty(_newValue) ? "0" : _newValue.Trim();
            string ev = value.Replace("\"", "\\\"");

            _isLoading = true;
            UGSCliRunner.RunAsync($"cs data custom set --custom-id {_activeEntityId} --key \"{key}\" --value \"{ev}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                { _lastSuccess = $"'{key}' 추가 완료"; _newKey = ""; _newValue = ""; FetchEntityData(_activeEntityId); }
                else _lastError = $"추가 실패: {result.Error}";
            });
        }

        // ─── 유틸 ──────────────────────────────────

        static bool IsNumeric(string s) =>
            !string.IsNullOrEmpty(s) && (double.TryParse(s, out _) || s == "true" || s == "false");

        static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            try
            {
                var sb = new StringBuilder();
                int indent = 0; bool inStr = false;
                foreach (char c in json)
                {
                    if (c == '"' && (sb.Length == 0 || sb[sb.Length - 1] != '\\')) inStr = !inStr;
                    if (inStr) { sb.Append(c); continue; }
                    switch (c)
                    {
                        case '{': case '[':
                            sb.Append(c); sb.AppendLine(); indent++;
                            sb.Append(new string(' ', indent * 2)); break;
                        case '}': case ']':
                            sb.AppendLine(); indent--;
                            sb.Append(new string(' ', indent * 2)); sb.Append(c); break;
                        case ',':
                            sb.Append(c); sb.AppendLine();
                            sb.Append(new string(' ', indent * 2)); break;
                        case ':': sb.Append(": "); break;
                        case ' ': case '\n': case '\r': case '\t': break;
                        default: sb.Append(c); break;
                    }
                }
                return sb.ToString();
            }
            catch { return json; }
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

        static string ExtractValue(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal); if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length); if (ci < 0) return "";
            int s = ci + 1;
            while (s < json.Length && json[s] == ' ') s++;
            if (s >= json.Length) return "";
            if (json[s] == '{') { int e = JsonFindBrace(json, s); return json.Substring(s, e - s + 1); }
            if (json[s] == '[') { int e = JsonFindBracket(json, s); return json.Substring(s, e - s + 1); }
            if (json[s] == '"') { int qe = json.IndexOf('"', s + 1); return qe > s ? json.Substring(s + 1, qe - s - 1) : ""; }
            int end = s;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(s, end - s).Trim();
        }

    }
}
#endif
