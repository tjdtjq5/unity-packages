#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// UGS 탭 공통 베이스. CLI 로딩/에러/Refresh UI 제공.
    /// EditorTabBase의 알림/테이블/탭 바/색상/JSON/리사이저블 컬럼 직접 사용.
    /// </summary>
    public abstract class UGSTabBase : EditorTabBase
    {
        // ─── 상태 필드 (유지) ─────────────────────────────

        protected bool _isLoading;
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
                if (DrawLinkButton("Dashboard"))
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

        // ─── Dashboard ──────────────────────────────────

        /// <summary>Dashboard 열기 (UGSConfig 기반)</summary>
        void OpenDashboard()
        {
            if (string.IsNullOrEmpty(_cachedProjectId))
                _cachedProjectId = UGSCliRunner.GetProjectId();

            if (string.IsNullOrEmpty(_cachedProjectId))
            {
                ShowNotification("Project ID를 가져올 수 없습니다. UGS CLI 로그인 상태를 확인하세요.", NotificationType.Error);
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
                ShowNotification("Dashboard URL을 생성할 수 없습니다. Settings에서 조직 ID를 설정하세요.", NotificationType.Error);
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

        // ─── CLI 결과 처리 ──────────────────────────────

        /// <summary>CLI 결과 처리 공통 패턴</summary>
        protected void HandleResult(UGSCliRunner.CliResult result, Action onSuccess)
        {
            _isLoading = false;
            if (result.Success)
            {
                _notification = null;
                _lastRefreshTime = DateTime.Now;
                onSuccess?.Invoke();
            }
            else
            {
                ShowNotification(string.IsNullOrEmpty(result.Error)
                    ? $"CLI 실행 실패 (exit code: {result.ExitCode})"
                    : result.Error, NotificationType.Error);
            }
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
            EditorGUILayout.LabelField("\u2192", GUILayout.Width(20));
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
            _notification = null;

            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            string safeDir = dir.Replace('\\', '/');

            // Step 1: Fetch from source
            UGSCliRunner.RunAsync($"fetch \"{safeDir}\" -s {service} -e {srcEnv}", fetchResult =>
            {
                if (!fetchResult.Success)
                {
                    _isLoading = false;
                    ShowNotification($"Fetch 실패 ({srcEnv}): {fetchResult.Error}", NotificationType.Error);
                    return;
                }

                // Step 2: Deploy to target
                UGSCliRunner.RunAsync($"deploy \"{safeDir}\" -s {service} -e {dstEnv}", deployResult =>
                {
                    if (!deployResult.Success)
                    {
                        _isLoading = false;
                        ShowNotification($"Deploy 실패 ({dstEnv}): {deployResult.Error}", NotificationType.Error);
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
                                ShowNotification($"{srcEnv} \u2192 {dstEnv} 복사 완료 ({service})", NotificationType.Success);
                                onComplete?.Invoke();
                            }
                            else
                                ShowNotification($"Publish 실패: {pubResult.Error}", NotificationType.Error);
                        });
                    }
                    else
                    {
                        _isLoading = false;
                        ShowNotification($"{srcEnv} \u2192 {dstEnv} 복사 완료 ({service})", NotificationType.Success);
                        onComplete?.Invoke();
                    }
                });
            });
        }

        // ─── 라이프사이클 ────────────────────────────────

        public override void OnEnable()
        {
            FetchData();
        }
    }
}
#endif
