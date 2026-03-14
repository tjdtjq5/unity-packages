#if UNITY_EDITOR
using System;
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

        /// <summary>에러 메시지 빨간 박스</summary>
        protected void DrawError()
        {
            if (string.IsNullOrEmpty(_lastError)) return;

            GUILayout.Space(4);
            EditorGUILayout.BeginVertical(GetBgStyle(new Color(0.35f, 0.12f, 0.12f)));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Error", new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = COL_ERROR } }, GUILayout.Width(40));

            if (GUILayout.Button("✕", GUILayout.Width(20), GUILayout.Height(16)))
                _lastError = null;

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastError))
            {
                EditorGUILayout.LabelField(_lastError, new GUIStyle(EditorStyles.wordWrappedLabel)
                    { normal = { textColor = new Color(0.95f, 0.60f, 0.60f) } });
            }

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

        // ─── 라이프사이클 ────────────────────────────────

        public override void OnEnable()
        {
            FetchData();
        }
    }
}
#endif
