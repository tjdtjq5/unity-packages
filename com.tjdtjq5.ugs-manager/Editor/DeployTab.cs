#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>UGS 통합 배포 탭. 서비스별 선택 배포 + 로그.</summary>
    public class DeployTab : UGSTabBase
    {
        public override string TabName => "Deploy";
        public override Color TabColor => new(0.30f, 0.80f, 0.40f);
        protected override string DashboardPath => "overview";

        bool _foldDeploy = true;
        bool _foldFetch;
        bool _foldLog = true;

        string _deployPath;

        // 서비스별 선택
        bool _selRemoteConfig = true;
        bool _selCloudCode = true;

        // 로그
        readonly List<LogEntry> _logs = new();
        Vector2 _logScroll;

        struct LogEntry
        {
            public string Time;
            public string Message;
            public Color Color;
        }

        protected override void FetchData()
        {
            if (string.IsNullOrEmpty(_deployPath))
                _deployPath = UGSConfig.DeployPath;

            _isLoading = false;
            _lastRefreshTime = DateTime.Now;
        }

        public override void OnDraw()
        {
            GUILayout.Space(4);

            // 배포
            if (DrawSectionFoldout(ref _foldDeploy, "배포 (로컬 → 서버)", TabColor))
            {
                BeginBody();

                // 경로
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("경로:", GUILayout.Width(40));
                _deployPath = EditorGUILayout.TextField(_deployPath);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string selected = EditorUtility.OpenFolderPanel("배포 폴더 선택",
                        "Assets/_Project", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        int assetsIdx = selected.IndexOf("Assets", StringComparison.Ordinal);
                        _deployPath = assetsIdx >= 0 ? selected[assetsIdx..] : selected;
                    }
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);
                DrawSubLabel("배포 대상 선택");

                // 체크박스
                _selRemoteConfig = EditorGUILayout.ToggleLeft("Remote Config", _selRemoteConfig);
                _selCloudCode = EditorGUILayout.ToggleLeft("Cloud Code Scripts", _selCloudCode);

                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();

                GUI.enabled = _selRemoteConfig || _selCloudCode;
                if (DrawColorBtn("Deploy Selected", COL_SUCCESS, 28))
                    DeploySelected();
                GUI.enabled = true;

                if (DrawColorBtn("Deploy All", COL_INFO, 28))
                    DeployAll();

                EditorGUILayout.EndHorizontal();

                EndBody();
            }

            GUILayout.Space(8);

            // Fetch
            if (DrawSectionFoldout(ref _foldFetch, "Fetch (서버 → 로컬)", COL_INFO))
            {
                BeginBody();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("경로:", GUILayout.Width(40));
                EditorGUILayout.LabelField(_deployPath, new GUIStyle(EditorStyles.label)
                    { normal = { textColor = COL_MUTED } });
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);

                if (DrawColorBtn("Fetch All", COL_INFO, 28))
                    FetchAll();

                EndBody();
            }

            GUILayout.Space(8);

            // 실행 로그
            if (DrawSectionFoldout(ref _foldLog, $"실행 로그 ({_logs.Count})", COL_MUTED))
            {
                BeginBody();

                if (_logs.Count == 0)
                {
                    EditorGUILayout.LabelField("아직 실행 기록 없음",
                        new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                }
                else
                {
                    _logScroll = EditorGUILayout.BeginScrollView(_logScroll,
                        GUILayout.MaxHeight(200));

                    // 최신 로그가 위에
                    for (int i = _logs.Count - 1; i >= 0; i--)
                    {
                        var log = _logs[i];
                        var style = new GUIStyle(EditorStyles.miniLabel)
                            { normal = { textColor = log.Color }, wordWrap = true };
                        EditorGUILayout.LabelField($"[{log.Time}] {log.Message}", style);
                    }

                    EditorGUILayout.EndScrollView();

                    GUILayout.Space(4);
                    if (DrawColorBtn("로그 지우기", COL_MUTED, 20))
                        _logs.Clear();
                }

                EndBody();
            }

            DrawLoading("배포 중...");
        }

        // ─── 배포 명령 ──────────────────────────────────

        void DeploySelected()
        {
            var services = new List<string>();
            if (_selRemoteConfig) services.Add("remote-config");
            if (_selCloudCode) services.Add("cloud-code-scripts");

            foreach (var svc in services)
                DeployService(svc);
        }

        void DeployAll()
        {
            AddLog("전체 배포 시작...", COL_INFO);
            _isLoading = true;

            UGSCliRunner.RunAsync($"deploy \"{_deployPath}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    AddLog("전체 배포 완료!", COL_SUCCESS);
                    if (!string.IsNullOrEmpty(result.Output))
                        AddLog(result.Output, Color.white);
                }
                else
                {
                    AddLog($"배포 실패: {result.Error}", COL_ERROR);
                }
            });
        }

        void DeployService(string service)
        {
            AddLog($"{service} 배포 시작...", COL_INFO);
            _isLoading = true;

            UGSCliRunner.RunAsync($"deploy \"{_deployPath}\" -s {service}", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    AddLog($"{service} 배포 완료!", COL_SUCCESS);
                    if (!string.IsNullOrEmpty(result.Output))
                        AddLog(result.Output, Color.white);
                }
                else
                {
                    AddLog($"{service} 실패: {result.Error}", COL_ERROR);
                }
            });
        }

        void FetchAll()
        {
            AddLog("Fetch 시작...", COL_INFO);
            _isLoading = true;

            UGSCliRunner.RunAsync($"fetch \"{_deployPath}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    AddLog("Fetch 완료!", COL_SUCCESS);
                    if (!string.IsNullOrEmpty(result.Output))
                        AddLog(result.Output, Color.white);
                }
                else
                {
                    AddLog($"Fetch 실패: {result.Error}", COL_ERROR);
                }
            });
        }

        void AddLog(string message, Color color)
        {
            _logs.Add(new LogEntry
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Message = message,
                Color = color
            });
        }
    }
}
#endif
