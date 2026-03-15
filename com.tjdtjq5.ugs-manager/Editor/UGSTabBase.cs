#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// UGS 탭 공통 베이스. CLI 로딩/에러/Refresh UI 제공.
    /// </summary>
    public abstract class UGSTabBase : Tjdtjq5.EditorToolkit.Editor.EditorTabBase
    {
        protected static readonly Color COL_SUCCESS = new(0.30f, 0.80f, 0.40f);
        protected static readonly Color COL_WARN    = new(0.95f, 0.75f, 0.20f);
        protected static readonly Color COL_ERROR   = new(0.95f, 0.30f, 0.30f);
        protected static readonly Color COL_INFO    = new(0.40f, 0.70f, 0.95f);
        protected static readonly Color COL_MUTED   = new(0.45f, 0.45f, 0.50f);
        protected static readonly Color COL_LINK    = new(0.35f, 0.65f, 0.95f);

        protected bool _isLoading;
        protected string _lastError;
        protected string _lastSuccess;
        protected string _lastResult;      // 실행 결과 (테스트 등)
        protected bool _lastResultIsError; // 결과가 에러인지
        protected DateTime _lastRefreshTime;

        // Dashboard 캐시
        static string _cachedProjectId;
        static string _cachedEnvId;

        /// <summary>CLI로 데이터 가져오기 (탭별 구현)</summary>
        protected abstract void FetchData();

        /// <summary>탭별 Dashboard 경로 (오버라이드 가능)</summary>
        protected virtual string DashboardPath => null;

        /// <summary>환경 ID를 포함하지 않는 프로젝트 레벨 경로인지 여부</summary>
        protected virtual bool IsProjectLevelPath => false;

        // ─── 공통 UI 요소 ────────────────────────────────

        /// <summary>상단 툴바: [Refresh] + [Dashboard] + 마지막 갱신 시간</summary>
        protected void DrawToolbar(params (string label, Color color, Action action)[] extraButtons)
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawColorBtn("Refresh", COL_INFO, 22))
                FetchData();

            if (extraButtons != null)
            {
                foreach (var (label, color, action) in extraButtons)
                {
                    if (DrawColorBtn(label, color, 22))
                        action?.Invoke();
                }
            }

            GUILayout.FlexibleSpace();

            // Dashboard 링크 버튼
            if (!string.IsNullOrEmpty(DashboardPath) && UGSConfig.IsConfigured)
            {
                if (DrawLinkBtn("Dashboard"))
                    OpenDashboard();
            }

            if (_lastRefreshTime != default)
            {
                var elapsed = DateTime.Now - _lastRefreshTime;
                string timeText = elapsed.TotalSeconds < 60
                    ? $"{elapsed.Seconds}초 전"
                    : $"{(int)elapsed.TotalMinutes}분 전";
                EditorGUILayout.LabelField(timeText, new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = COL_MUTED },
                    alignment = TextAnchor.MiddleRight
                }, GUILayout.Width(60));
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>하이퍼링크 스타일 버튼 (밑줄 + 파란색)</summary>
        protected bool DrawLinkBtn(string text)
        {
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = COL_LINK },
                hover = { textColor = Color.white },
                alignment = TextAnchor.MiddleRight,
            };
            var content = new GUIContent($"  {text} ↗");
            var rect = GUILayoutUtility.GetRect(content, style);

            var underlineRect = new Rect(rect.x + 2, rect.yMax - 2, rect.width - 4, 1);
            EditorGUI.DrawRect(underlineRect, new Color(COL_LINK.r, COL_LINK.g, COL_LINK.b, 0.4f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            return GUI.Button(rect, content, style);
        }

        // ─── Dashboard ──────────────────────────────────

        /// <summary>Dashboard 열기 (UGSConfig 기반)</summary>
        void OpenDashboard()
        {
            if (string.IsNullOrEmpty(_cachedProjectId))
                _cachedProjectId = UGSCliRunner.GetProjectId();

            if (string.IsNullOrEmpty(_cachedProjectId))
            {
                _lastError = "Project ID를 가져올 수 없습니다. UGS CLI 로그인 상태를 확인하세요.";
                return;
            }

            string envId = null;
            if (!IsProjectLevelPath)
            {
                if (string.IsNullOrEmpty(_cachedEnvId))
                    _cachedEnvId = GetActiveEnvId();
                envId = _cachedEnvId;
            }

            string url = UGSConfig.GetDashboardUrl(_cachedProjectId, envId, DashboardPath);
            if (!string.IsNullOrEmpty(url))
                Application.OpenURL(url);
            else
                _lastError = "Dashboard URL을 생성할 수 없습니다. Settings에서 조직 ID를 설정하세요.";
        }

        /// <summary>현재 활성 환경의 ID 조회</summary>
        static string GetActiveEnvId()
        {
            var result = UGSCliRunner.RunJson("env list");
            if (!result.Success || string.IsNullOrEmpty(result.Output)) return "";

            string activeEnv = UGSCliRunner.GetEnvironment();
            if (string.IsNullOrEmpty(activeEnv)) return "";

            string json = result.Output;
            int searchFrom = 0;
            while (true)
            {
                int objStart = json.IndexOf('{', searchFrom);
                if (objStart < 0) break;
                int objEnd = json.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string block = json.Substring(objStart, objEnd - objStart + 1);
                string name = ExtractField(block, "name");
                if (name == activeEnv)
                    return ExtractField(block, "id");

                searchFrom = objEnd + 1;
            }
            return "";
        }

        protected static string ExtractField(string block, string field)
        {
            if (string.IsNullOrEmpty(block) || string.IsNullOrEmpty(field)) return "";

            string key = $"\"{field}\"";
            int keyIdx = block.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx < 0) return "";
            int colonIdx = block.IndexOf(':', keyIdx + key.Length);
            if (colonIdx < 0) return "";
            int start = colonIdx + 1;
            while (start < block.Length && char.IsWhiteSpace(block[start])) start++;
            if (start >= block.Length) return "";
            if (block[start] == '"')
            {
                int end = block.IndexOf('"', start + 1);
                return end > start ? block.Substring(start + 1, end - start - 1) : "";
            }
            int valEnd = start;
            while (valEnd < block.Length && block[valEnd] != ',' && block[valEnd] != '}') valEnd++;
            return block.Substring(start, valEnd - start).Trim();
        }

        /// <summary>환경 변경 시 캐시 초기화</summary>
        public static void InvalidateEnvCache() => _cachedEnvId = null;

        // ─── 에러/로딩 ──────────────────────────────────

        /// <summary>에러 메시지 빨간 박스 + 복사 버튼</summary>
        protected void DrawError()
        {
            if (string.IsNullOrEmpty(_lastError)) return;
            DrawNotification("Error", _lastError, COL_ERROR,
                new Color(0.35f, 0.12f, 0.12f), new Color(0.95f, 0.60f, 0.60f),
                () => _lastError = null);
        }

        /// <summary>성공 메시지 초록 박스</summary>
        protected void DrawSuccess()
        {
            if (string.IsNullOrEmpty(_lastSuccess)) return;
            DrawNotification("Success", _lastSuccess, COL_SUCCESS,
                new Color(0.12f, 0.28f, 0.14f), new Color(0.60f, 0.90f, 0.65f),
                () => _lastSuccess = null);
        }

        /// <summary>실행 결과 알림 (테스트 등)</summary>
        protected void DrawResult()
        {
            if (string.IsNullOrEmpty(_lastResult)) return;
            if (_lastResultIsError)
                DrawNotification("Result", _lastResult, COL_ERROR,
                    new Color(0.35f, 0.12f, 0.12f), new Color(0.95f, 0.60f, 0.60f),
                    () => _lastResult = null);
            else
                DrawNotification("Result", _lastResult, COL_INFO,
                    new Color(0.12f, 0.18f, 0.30f), new Color(0.70f, 0.85f, 0.95f),
                    () => _lastResult = null);
        }

        /// <summary>공통 알림 박스 (라벨 + 내용 + Copy + 닫기)</summary>
        void DrawNotification(string label, string content, Color labelColor, Color bgColor, Color textColor, Action onClose)
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(GetBgStyle(bgColor));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = labelColor } }, GUILayout.Width(55));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(16)))
                EditorGUIUtility.systemCopyBuffer = content;
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(16)))
                onClose?.Invoke();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(content, new GUIStyle(EditorStyles.wordWrappedLabel)
                { normal = { textColor = textColor } });

            EditorGUILayout.EndVertical();
        }

        /// <summary>로딩 표시</summary>
        protected void DrawLoading(string message = "로딩 중...")
        {
            if (!_isLoading) return;

            GUILayout.Space(8);
            EditorGUILayout.LabelField($"⟳ {message}", new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_INFO }
            });
            GUILayout.Space(8);
        }

        /// <summary>CLI 결과 처리 공통 패턴</summary>
        protected void HandleResult(UGSCliRunner.CliResult result, Action onSuccess)
        {
            _isLoading = false;
            if (result.Success)
            {
                _lastError = null;
                _lastRefreshTime = DateTime.Now;
                onSuccess?.Invoke();
            }
            else
            {
                _lastError = string.IsNullOrEmpty(result.Error)
                    ? $"CLI 실행 실패 (exit code: {result.ExitCode})"
                    : result.Error;
            }
        }

        // ─── 테이블 드로잉 ──────────────────────────────

        /// <summary>테이블 헤더 라벨</summary>
        protected void DrawHeaderLabel(string text, float width = 0)
        {
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_MUTED },
                fontSize = 10
            };

            if (width > 0)
                EditorGUILayout.LabelField(text, style, GUILayout.Width(width));
            else
                EditorGUILayout.LabelField(text, style);
        }

        /// <summary>테이블 셀 라벨</summary>
        protected void DrawCellLabel(string text, float width = 0, Color? color = null)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = color ?? Color.white }
            };

            if (width > 0)
                EditorGUILayout.LabelField(text ?? "", style, GUILayout.Width(width));
            else
                EditorGUILayout.LabelField(text ?? "", style);
        }

        // ─── JSON 유틸 (공통) ────────────────────────────

        /// <summary>중괄호 매칭. 문자열 내 괄호를 무시.</summary>
        protected static int JsonFindBrace(string s, int open)
        {
            int depth = 1;
            bool inStr = false;
            for (int i = open + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inStr) { i++; continue; } // 이스케이프 건너뜀
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return s.Length - 1;
        }

        /// <summary>대괄호 매칭. 문자열 내 괄호를 무시.</summary>
        protected static int JsonFindBracket(string s, int open)
        {
            int depth = 1;
            bool inStr = false;
            for (int i = open + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '[') depth++;
                else if (c == ']') { depth--; if (depth == 0) return i; }
            }
            return s.Length - 1;
        }

        // ─── 스타일 탭 (공통) ────────────────────────────

        // 더블클릭 이름 편집 상태
        int _tabEditIdx = -1;
        string _tabEditName;

        /// <summary>
        /// 스타일 탭 바. 반환값: 선택된 인덱스.
        /// onAdd: + 탭 표시, onRename: 더블클릭 이름 편집 활성화
        /// </summary>
        protected int DrawStyledTabs(string[] labels, int activeIdx, Color[] colors = null,
            Action onAdd = null, Action<int, string> onRename = null)
        {
            if (labels == null || labels.Length == 0)
            {
                // 탭 없어도 + 버튼은 표시
                if (onAdd != null)
                {
                    var r2 = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(r2, BG_HEADER);
                    var addR = new Rect(r2.x, r2.y, 30, r2.height);
                    DrawAddTab(addR, onAdd);
                }
                return 0;
            }
            if (activeIdx >= labels.Length) activeIdx = 0;

            bool hasAdd = onAdd != null;
            float addW = hasAdd ? 30f : 0f;
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, BG_HEADER);

            float tabW = (rect.width - addW) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var tr = new Rect(rect.x + tabW * i, rect.y, tabW, rect.height);
                bool active = activeIdx == i;
                Color c = colors != null && i < colors.Length ? colors[i] : TabColor;

                if (active)
                {
                    EditorGUI.DrawRect(tr, new Color(c.r, c.g, c.b, 0.15f));
                    EditorGUI.DrawRect(new Rect(tr.x, tr.yMax - 2, tr.width, 2), c);
                }

                // 더블클릭 이름 편집
                if (_tabEditIdx == i && onRename != null)
                {
                    GUI.SetNextControlName($"tabEdit_{i}");
                    _tabEditName = EditorGUI.TextField(new Rect(tr.x + 2, tr.y + 3, tr.width - 4, tr.height - 6),
                        _tabEditName, new GUIStyle(EditorStyles.textField) { fontSize = 11, alignment = TextAnchor.MiddleCenter });

                    // Enter로 확정
                    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                    {
                        onRename.Invoke(i, _tabEditName);
                        _tabEditIdx = -1;
                        Event.current.Use();
                    }
                    // 포커스 이탈 시 확정
                    else if (Event.current.type == EventType.MouseDown && !tr.Contains(Event.current.mousePosition))
                    {
                        onRename.Invoke(i, _tabEditName);
                        _tabEditIdx = -1;
                    }
                }
                else
                {
                    var st = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 11, alignment = TextAnchor.MiddleCenter,
                        fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                        normal = { textColor = active ? c : COL_MUTED }
                    };
                    EditorGUI.LabelField(tr, labels[i], st);
                }

                // 클릭 / 더블클릭
                if (Event.current.type == EventType.MouseDown && tr.Contains(Event.current.mousePosition))
                {
                    if (Event.current.clickCount == 2 && onRename != null)
                    {
                        _tabEditIdx = i;
                        _tabEditName = labels[i];
                    }
                    else
                    {
                        activeIdx = i;
                        if (_tabEditIdx != i) _tabEditIdx = -1;
                    }
                    Event.current.Use();
                }
            }

            // + 탭 (탭 바 끝에 자연스럽게)
            if (hasAdd)
            {
                var addRect = new Rect(rect.x + tabW * labels.Length, rect.y, addW, rect.height);
                DrawAddTab(addRect, onAdd);
            }

            return activeIdx;
        }

        void DrawAddTab(Rect rect, Action onAdd)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            var st = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 14, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = COL_MUTED }
            };
            EditorGUI.LabelField(rect, "+", st);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            { onAdd.Invoke(); Event.current.Use(); }
        }

        // ─── 환경 복사 (공통) ────────────────────────────

        protected static string[] _sharedEnvNames;
        protected static string[] _sharedEnvIds;
        protected int _envCopySrcIdx;
        protected int _envCopyDstIdx;
        protected bool _foldEnvCopy;

        /// <summary>환경 목록 로드 (전 탭 공유)</summary>
        protected static void LoadSharedEnvironments()
        {
            if (_sharedEnvNames != null) return;
            var result = UGSCliRunner.RunJson("env list");
            if (!result.Success) return;

            var names = new List<string>();
            var ids = new List<string>();
            int sf = 0;
            while (true)
            {
                int os = result.Output.IndexOf('{', sf); if (os < 0) break;
                int oe = result.Output.IndexOf('}', os); if (oe < 0) break;
                string blk = result.Output.Substring(os, oe - os + 1);
                string name = ExtractField(blk, "name");
                string id = ExtractField(blk, "id");
                if (!string.IsNullOrEmpty(name)) { names.Add(name); ids.Add(id); }
                sf = oe + 1;
            }
            _sharedEnvNames = names.ToArray();
            _sharedEnvIds = ids.ToArray();
        }

        /// <summary>환경 복사 UI 섹션 그리기. fetchService와 deployDir를 지정하면 자동 처리.</summary>
        /// <param name="serviceName">CLI 서비스명 (remote-config, cloud-code-scripts, economy)</param>
        /// <param name="deployDir">로컬 파일 경로</param>
        /// <param name="needsPublish">Deploy 후 publish 필요 여부 (Economy만 true)</param>
        /// <param name="publishCmd">publish CLI 명령 (예: "economy publish")</param>
        /// <param name="onComplete">복사 완료 후 콜백</param>
        protected void DrawEnvCopySection(string serviceName, string deployDir, bool needsPublish = false,
            string publishCmd = null, Action onComplete = null)
        {
            if (!DrawSectionFoldout(ref _foldEnvCopy, "환경 복사", COL_INFO)) return;
            BeginBody();

            LoadSharedEnvironments();

            if (_sharedEnvNames == null || _sharedEnvNames.Length == 0)
            {
                EditorGUILayout.LabelField("환경 정보 없음",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                EndBody();
                return;
            }

            // 기본값: src=production, dst=dev
            if (_envCopySrcIdx == 0 && _envCopyDstIdx == 0 && _sharedEnvNames.Length > 1)
            {
                _envCopySrcIdx = System.Array.IndexOf(_sharedEnvNames, "dev");
                _envCopyDstIdx = System.Array.IndexOf(_sharedEnvNames, "production");
                if (_envCopySrcIdx < 0) _envCopySrcIdx = 0;
                if (_envCopyDstIdx < 0) _envCopyDstIdx = _sharedEnvNames.Length > 1 ? 1 : 0;
            }

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("소스:", GUILayout.Width(35));
            _envCopySrcIdx = EditorGUILayout.Popup(_envCopySrcIdx, _sharedEnvNames, GUILayout.Width(120));
            EditorGUILayout.LabelField("→", GUILayout.Width(20));
            EditorGUILayout.LabelField("대상:", GUILayout.Width(35));
            _envCopyDstIdx = EditorGUILayout.Popup(_envCopyDstIdx, _sharedEnvNames, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = _envCopySrcIdx != _envCopyDstIdx && !_isLoading;
            if (GUILayout.Button("복사 실행", EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(18)))
                ExecuteEnvCopy(serviceName, deployDir, _sharedEnvNames[_envCopySrcIdx],
                    _sharedEnvNames[_envCopyDstIdx], needsPublish, publishCmd, onComplete);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EndBody();
        }

        void ExecuteEnvCopy(string service, string dir, string srcEnv, string dstEnv,
            bool needsPublish, string publishCmd, Action onComplete)
        {
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            string safeDir = dir.Replace('\\', '/');

            // Step 1: Fetch from source
            UGSCliRunner.RunAsync($"fetch \"{safeDir}\" -s {service} -e {srcEnv}", fetchResult =>
            {
                if (!fetchResult.Success)
                {
                    _isLoading = false;
                    _lastError = $"Fetch 실패 ({srcEnv}): {fetchResult.Error}";
                    return;
                }

                // Step 2: Deploy to target
                UGSCliRunner.RunAsync($"deploy \"{safeDir}\" -s {service} -e {dstEnv}", deployResult =>
                {
                    if (!deployResult.Success)
                    {
                        _isLoading = false;
                        _lastError = $"Deploy 실패 ({dstEnv}): {deployResult.Error}";
                        return;
                    }

                    // Step 3: Publish if needed
                    if (needsPublish && !string.IsNullOrEmpty(publishCmd))
                    {
                        UGSCliRunner.RunAsync($"{publishCmd} -e {dstEnv}", pubResult =>
                        {
                            _isLoading = false;
                            if (pubResult.Success)
                            {
                                _lastSuccess = $"{srcEnv} → {dstEnv} 복사 완료 ({service})";
                                onComplete?.Invoke();
                            }
                            else
                                _lastError = $"Publish 실패: {pubResult.Error}";
                        });
                    }
                    else
                    {
                        _isLoading = false;
                        _lastSuccess = $"{srcEnv} → {dstEnv} 복사 완료 ({service})";
                        onComplete?.Invoke();
                    }
                });
            });
        }

        // ─── 리사이저블 컬럼 ────────────────────────────

        /// <summary>컬럼 정의</summary>
        protected struct ColDef
        {
            public string Name;
            public float DefaultWidth;
            public bool Resizable;
            public float MinWidth;

            public ColDef(string name, float defaultWidth = 0f, bool resizable = false, float minWidth = 40f)
            {
                Name = name;
                DefaultWidth = defaultWidth;
                Resizable = resizable;
                MinWidth = minWidth;
            }
        }

        /// <summary>
        /// Rect 기반 리사이저블 컬럼 헤더. EditorPrefs로 너비 자동 저장/복원.
        /// width=0인 컬럼은 나머지 공간을 사용 (1개만 허용).
        /// </summary>
        protected class ResizableColumns
        {
            readonly string _prefsPrefix;
            readonly ColDef[] _defs;
            readonly float[] _widths;
            readonly bool[] _dragging;

            const float DRAG_W = 6f;
            static readonly Color COL_DIVIDER = new(0.35f, 0.35f, 0.40f);

            public int Count => _defs.Length;

            public ResizableColumns(string prefsPrefix, ColDef[] defs)
            {
                _prefsPrefix = prefsPrefix;
                _defs = defs;
                _widths = new float[defs.Length];
                _dragging = new bool[defs.Length];
                LoadWidths();
            }

            /// <summary>컬럼 너비 (인덱스). 0이면 flex(나머지 공간)이므로 DrawHeader 이후에 계산됨.</summary>
            public float GetWidth(int index) => _widths[index];

            /// <summary>GUILayout.Width 옵션 반환. flex 컬럼이면 null (width 지정 안 함).</summary>
            public GUILayoutOption WidthOption(int index)
            {
                return _defs[index].DefaultWidth > 0 ? GUILayout.Width(_widths[index]) : null;
            }

            /// <summary>Rect 기반 헤더 드로잉 (배경 + 라벨 + 구분선 + 드래그 핸들)</summary>
            public void DrawHeader()
            {
                var r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, new Color(0.18f, 0.18f, 0.22f)); // BG_HEADER

                var st = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.45f, 0.45f, 0.50f) },
                    fontSize = 10
                };

                // flex 컬럼 탐색 + 실제 너비 계산
                float fixedTotal = 0f;
                int flexIdx = -1;
                for (int i = 0; i < _defs.Length; i++)
                {
                    if (_defs[i].DefaultWidth <= 0) flexIdx = i;
                    else fixedTotal += _widths[i];
                }
                if (flexIdx >= 0)
                    _widths[flexIdx] = Mathf.Max(r.width - fixedTotal, 40f);

                // 컬럼 드로잉
                float x = r.x;
                for (int i = 0; i < _defs.Length; i++)
                {
                    float w = _widths[i];
                    EditorGUI.LabelField(new Rect(x, r.y, w, r.height), _defs[i].Name, st);
                    x += w;

                    // 구분선 + 드래그 핸들
                    // 규칙: flex 왼쪽 컬럼 → 오른쪽 경계에서 리사이즈 (정방향)
                    //       flex 오른쪽 컬럼 → flex|컬럼 경계에서만 리사이즈 (역방향)
                    if (i < _defs.Length - 1)
                    {
                        EditorGUI.DrawRect(new Rect(x - 1, r.y, 1, r.height), COL_DIVIDER);
                        var dragRect = new Rect(x - DRAG_W / 2, r.y, DRAG_W, r.height);

                        if (_defs[i].Resizable && (flexIdx < 0 || i < flexIdx))
                            HandleDrag(dragRect, i, false);
                        else if (flexIdx >= 0 && i == flexIdx && i + 1 < _defs.Length && _defs[i + 1].Resizable)
                            HandleDrag(dragRect, i + 1, true);
                    }
                }

                // 하단 구분선
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), COL_DIVIDER);
            }

            void HandleDrag(Rect handle, int colIndex, bool invertDelta)
            {
                EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);
                var evt = Event.current;

                if (evt.type == EventType.MouseDown && handle.Contains(evt.mousePosition))
                { _dragging[colIndex] = true; evt.Use(); }
                else if (evt.type == EventType.MouseUp && _dragging[colIndex])
                { _dragging[colIndex] = false; SaveWidths(); evt.Use(); }
                else if (evt.type == EventType.MouseDrag && _dragging[colIndex])
                {
                    float delta = invertDelta ? -evt.delta.x : evt.delta.x;
                    _widths[colIndex] = Mathf.Max(_widths[colIndex] + delta, _defs[colIndex].MinWidth);
                    evt.Use();
                    EditorWindow.GetWindow<UGSWindow>()?.Repaint();
                }
            }

            void SaveWidths()
            {
                for (int i = 0; i < _defs.Length; i++)
                {
                    if (_defs[i].Resizable)
                        EditorPrefs.SetFloat($"{_prefsPrefix}_Col{i}", _widths[i]);
                }
            }

            void LoadWidths()
            {
                for (int i = 0; i < _defs.Length; i++)
                {
                    if (_defs[i].Resizable)
                        _widths[i] = EditorPrefs.GetFloat($"{_prefsPrefix}_Col{i}", _defs[i].DefaultWidth);
                    else
                        _widths[i] = _defs[i].DefaultWidth;
                }
            }
        }

        // ─── 라이프사이클 ────────────────────────────────

        public override void OnEnable()
        {
            FetchData();
        }
    }
}
#endif
