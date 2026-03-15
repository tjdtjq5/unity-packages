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
    /// Player Data 탭. Cloud Save 플레이어 데이터 조회/편집/삭제 + 북마크.
    /// </summary>
    public class PlayerDataTab : UGSTabBase
    {
        public override string TabName => "Player";
        public override Color TabColor => new(0.50f, 0.80f, 0.65f);
        protected override string DashboardPath => "cloud-save/player-data";

        // ─── 데이터 ──────────────────────────────────
        string _inputPlayerId = "";
        string _activePlayerId = "";
        List<DataItem> _items = new();
        bool _dataLoaded;

        // 북마크
        List<Bookmark> _bookmarks = new();
        const string KEY_BOOKMARKS = "UGS_PD_Bookmarks";

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
            public string PlayerId;
        }

        // ─── 라이프사이클 ────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            _columns ??= new ResizableColumns("UGS_PD", new[]
            {
                new ColDef("키", 140f, resizable: true),
                new ColDef("값", 0f),
                new ColDef("", 44f),
            });

            LoadBookmarks();
            _lastRefreshTime = DateTime.Now;
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
                    PlayerId = part.Substring(sep + 1)
                });
            }
        }

        void SaveBookmarks()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _bookmarks.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(_bookmarks[i].Label).Append(':').Append(_bookmarks[i].PlayerId);
            }
            EditorPrefs.SetString(KEY_BOOKMARKS, sb.ToString());
        }

        void AddBookmark(string playerId)
        {
            if (_bookmarks.Any(b => b.PlayerId == playerId)) return;
            string label = $"Player {_bookmarks.Count + 1}";
            _bookmarks.Add(new Bookmark { Label = label, PlayerId = playerId });
            SaveBookmarks();
        }

        // ─── 데이터 조회 ────────────────────────────

        void FetchPlayerData(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;
            playerId = playerId.Trim();
            _activePlayerId = playerId;
            _inputPlayerId = playerId;
            _isLoading = true;
            _items.Clear();
            _dataLoaded = false;

            UGSCliRunner.RunAsync($"cs data player get --player-id {playerId} -j -q", result =>
            {
                _isLoading = false;
                if (!result.Success)
                {
                    _lastError = result.Error.Contains("404") || result.Error.Contains("not found")
                        ? $"플레이어를 찾을 수 없습니다: {playerId}"
                        : $"데이터 조회 실패: {result.Error}";
                    _dataLoaded = false;
                    return;
                }

                ParseItems(result.Output);
                _dataLoaded = true;

                if (_items.Count == 0)
                {
                    _lastError = $"플레이어 없음: {playerId}";
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
                    Key = key,
                    Value = value,
                    EditValue = value,
                    IsJson = isJson
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
            DrawPlayerInput();
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
                if (!string.IsNullOrEmpty(_activePlayerId))
                    FetchPlayerData(_activePlayerId);
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

        // ─── 플레이어 ID 입력 ───────────────────────

        void DrawPlayerInput()
        {
            EditorGUILayout.BeginVertical(GetBgStyle(BG_SECTION));

            EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            EditorGUILayout.LabelField("Player ID:", GUILayout.Width(65));
            _inputPlayerId = EditorGUILayout.TextField(_inputPlayerId);

            GUI.enabled = !string.IsNullOrWhiteSpace(_inputPlayerId);
            if (DrawColorBtn("조회", COL_SUCCESS, 20))
                FetchPlayerData(_inputPlayerId);
            GUI.enabled = true;

            // 조회 성공한 플레이어만 북마크 가능
            GUI.enabled = _dataLoaded && !string.IsNullOrEmpty(_activePlayerId);
            if (GUILayout.Button("☆", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
                AddBookmark(_activePlayerId);
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // 현재 조회 중인 플레이어
            if (!string.IsNullOrEmpty(_activePlayerId))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("조회 중:", new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED } }, GUILayout.Width(45));
                DrawCopyableLabel(_activePlayerId, COL_SUCCESS);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        // ─── 북마크 목록 ────────────────────────────

        void DrawBookmarkSection()
        {
            if (_bookmarks.Count == 0) return;
            if (!DrawSectionFoldout(ref _foldBookmarks, $"북마크 ({_bookmarks.Count})", COL_WARN)) return;
            BeginBody();

            for (int i = 0; i < _bookmarks.Count; i++)
            {
                var bm = _bookmarks[i];
                EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

                // 별명 (우클릭 → 복사/수정)
                DrawCopyableLabel($"★ {bm.Label}", new Color(0.95f, 0.75f, 0.20f), onRightClick: () =>
                {
                    int idx = i;
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("별명 복사"), false, () =>
                        EditorGUIUtility.systemCopyBuffer = _bookmarks[idx].Label);
                    menu.AddItem(new GUIContent("별명 수정"), false, () =>
                    {
                        string newLabel = _bookmarks[idx].Label;
                        newLabel = EditorInputDialog.Show("별명 수정", "새 별명:", newLabel);
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

                // ID (우클릭 → 복사)
                string shortId = bm.PlayerId.Length > 16 ? bm.PlayerId[..16] + "..." : bm.PlayerId;
                DrawCopyableLabel(shortId, COL_LINK, bm.PlayerId);

                GUILayout.FlexibleSpace();

                // 조회 버튼
                if (GUILayout.Button("조회", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(16)))
                    FetchPlayerData(bm.PlayerId);

                // 삭제 버튼
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

        // ─── 특수 라벨 (우클릭 복사) ─────────────────

        /// <summary>밑줄 + Link커서 + 우클릭 복사 가능한 라벨</summary>
        void DrawCopyableLabel(string text, Color color, string copyText = null, Action onRightClick = null)
        {
            string toCopy = copyText ?? text;
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = color },
                fontSize = 11
            };
            var content = new GUIContent(text);
            float textWidth = style.CalcSize(content).x + 4;
            var rect = GUILayoutUtility.GetRect(textWidth, 16, GUILayout.Width(textWidth));

            // 밑줄 (점선 느낌)
            var underline = new Rect(rect.x, rect.yMax - 1, rect.width, 1);
            EditorGUI.DrawRect(underline, new Color(color.r, color.g, color.b, 0.35f));

            // 호버 시 배경 하이라이트
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.08f));

            // Link 커서
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            // 라벨 드로잉
            EditorGUI.LabelField(rect, text, style);

            // 우클릭
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rect.Contains(Event.current.mousePosition))
            {
                if (onRightClick != null)
                {
                    onRightClick.Invoke();
                }
                else
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("복사"), false, () =>
                        EditorGUIUtility.systemCopyBuffer = toCopy);
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

            // 검색
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

            // 키
            DrawCellLabel(item.Key, _columns.GetWidth(COL_KEY));

            // 값
            if (item.IsEditing && !item.IsJson)
            {
                item.EditValue = EditorGUILayout.TextField(item.EditValue);
            }
            else if (!item.IsJson)
            {
                string displayVal = item.Value.Length > 60 ? item.Value[..60] + "..." : item.Value;
                DrawCellLabel(displayVal, 0, IsNumeric(item.Value) ? COL_INFO : (Color?)null);
            }
            else
            {
                string preview = item.Value.Length > 40 ? item.Value[..40] + "..." : item.Value;
                DrawCellLabel(preview, 0, new Color(0.70f, 0.55f, 0.95f));
            }

            // 액션
            if (item.IsJson)
            {
                if (GUILayout.Button(item.IsExpanded ? "▾" : "▸", EditorStyles.miniButton,
                    GUILayout.Width(18), GUILayout.Height(16)))
                    item.IsExpanded = !item.IsExpanded;
            }
            else if (!item.IsEditing)
            {
                if (GUILayout.Button("✎", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                {
                    item.IsEditing = true;
                    item.EditValue = item.Value;
                }
            }
            else
            {
                if (GUILayout.Button("✓", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                    SaveItem(item);
            }

            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
            {
                if (EditorUtility.DisplayDialog("키 삭제", $"'{item.Key}'를 삭제하시겠습니까?", "삭제", "취소"))
                    DeleteItem(item);
            }

            EditorGUILayout.EndHorizontal();

            // JSON 펼침
            if (item.IsJson && item.IsExpanded)
                DrawJsonEditor(item);

            // 편집 취소
            if (item.IsEditing && !item.IsJson)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("취소", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                {
                    item.IsEditing = false;
                    item.EditValue = item.Value;
                }
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
                var style = new GUIStyle(EditorStyles.textArea)
                {
                    fontSize = 11, wordWrap = true, richText = false,
                    normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
                };
                string formatted = FormatJson(item.Value);
                float h = Mathf.Min(style.CalcHeight(new GUIContent(formatted),
                    EditorGUIUtility.currentViewWidth - _columns.GetWidth(COL_KEY) - 60), 200f);
                EditorGUILayout.TextArea(formatted, style, GUILayout.Height(h + 4));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("편집", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                {
                    item.IsEditing = true;
                    item.EditValue = FormatJson(item.Value);
                }
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                    EditorGUIUtility.systemCopyBuffer = item.Value;
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                var style = new GUIStyle(EditorStyles.textArea)
                {
                    fontSize = 11, wordWrap = true,
                    normal = { textColor = new Color(0.95f, 0.90f, 0.70f) }
                };
                float h = Mathf.Min(style.CalcHeight(new GUIContent(item.EditValue),
                    EditorGUIUtility.currentViewWidth - _columns.GetWidth(COL_KEY) - 60), 200f);
                item.EditValue = EditorGUILayout.TextArea(item.EditValue, style, GUILayout.Height(Mathf.Max(h + 4, 60)));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("저장", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                    SaveItem(item);
                if (GUILayout.Button("취소", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(14)))
                {
                    item.IsEditing = false;
                    item.EditValue = item.Value;
                }
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
            string escapedValue = value.Replace("\"", "\\\"");

            _isLoading = true;
            UGSCliRunner.RunAsync($"cs data player set --player-id {_activePlayerId} --key \"{item.Key}\" --value \"{escapedValue}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    item.Value = item.EditValue.Trim();
                    item.IsEditing = false;
                    _lastSuccess = $"'{item.Key}' 저장 완료";
                }
                else
                    _lastError = $"저장 실패: {result.Error}";
            });
        }

        void DeleteItem(DataItem item)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"cs data player set --player-id {_activePlayerId} --key \"{item.Key}\" --value \"\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _items.Remove(item);
                    _lastSuccess = $"'{item.Key}' 삭제 완료";
                }
                else
                    _lastError = $"삭제 실패: {result.Error}";
            });
        }

        void AddItem()
        {
            string key = _newKey.Trim();
            string value = string.IsNullOrEmpty(_newValue) ? "0" : _newValue.Trim();
            string escapedValue = value.Replace("\"", "\\\"");

            _isLoading = true;
            UGSCliRunner.RunAsync($"cs data player set --player-id {_activePlayerId} --key \"{key}\" --value \"{escapedValue}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastSuccess = $"'{key}' 추가 완료";
                    _newKey = "";
                    _newValue = "";
                    FetchPlayerData(_activePlayerId);
                }
                else
                    _lastError = $"추가 실패: {result.Error}";
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
                int indent = 0;
                bool inString = false;
                foreach (char c in json)
                {
                    if (c == '"' && (sb.Length == 0 || sb[sb.Length - 1] != '\\'))
                        inString = !inString;
                    if (inString) { sb.Append(c); continue; }
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
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return "";
            int s = ci + 1;
            while (s < json.Length && json[s] == ' ') s++;
            if (s >= json.Length) return "";
            if (json[s] == '"')
            {
                int qe = json.IndexOf('"', s + 1);
                return qe > s ? json.Substring(s + 1, qe - s - 1) : "";
            }
            int e = s;
            while (e < json.Length && json[e] != ',' && json[e] != '}') e++;
            return json.Substring(s, e - s).Trim();
        }

        static string ExtractValue(string json, string field)
        {
            string key = $"\"{field}\"";
            int ki = json.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int ci = json.IndexOf(':', ki + key.Length);
            if (ci < 0) return "";
            int s = ci + 1;
            while (s < json.Length && json[s] == ' ') s++;
            if (s >= json.Length) return "";
            if (json[s] == '{') { int e = JsonFindBrace(json, s); return json.Substring(s, e - s + 1); }
            if (json[s] == '[') { int e = JsonFindBracket(json, s); return json.Substring(s, e - s + 1); }
            if (json[s] == '"')
            {
                int qe = json.IndexOf('"', s + 1);
                return qe > s ? json.Substring(s + 1, qe - s - 1) : "";
            }
            int end = s;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(s, end - s).Trim();
        }

    }

    /// <summary>간단한 텍스트 입력 다이얼로그</summary>
    class EditorInputDialog : EditorWindow
    {
        string _value;
        string _message;
        Action<string> _onConfirm;

        public static string Show(string title, string message, string defaultValue)
        {
            string result = defaultValue;
            var wnd = CreateInstance<EditorInputDialog>();
            wnd.titleContent = new GUIContent(title);
            wnd._message = message;
            wnd._value = defaultValue;
            wnd._onConfirm = v => result = v;
            wnd.minSize = new Vector2(250, 80);
            wnd.maxSize = new Vector2(400, 80);
            wnd.ShowModal();
            return result;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField(_message);
            _value = EditorGUILayout.TextField(_value);
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("확인")) { _onConfirm?.Invoke(_value); Close(); }
            if (GUILayout.Button("취소")) Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
