#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>UGS 통합 배포 탭. 서비스별 선택 배포 + Dry Run + Fetch.</summary>
    public class DeployTab : UGSTabBase
    {
        public override string TabName => "Deploy";
        public override Color TabColor => new(0.30f, 0.80f, 0.40f);
        protected override string DashboardPath => "overview";

        bool _foldDeploy = true;
        bool _foldFetch;
        string _deployPath;

        // 서비스별 선택
        bool _selRemoteConfig = true;
        bool _selCloudCode = true;
        bool _selEconomy = true;
        bool _selLeaderboards = true;
        bool _selScheduler = true;
        bool _selTriggers = true;

        protected override void FetchData()
        {
            if (string.IsNullOrEmpty(_deployPath))
                _deployPath = UGSConfig.DeployPath;
            _isLoading = false;
            _lastRefreshTime = DateTime.Now;
        }

        public override void OnDraw()
        {
            DrawError();
            DrawSuccess();

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
                    string selected = EditorUtility.OpenFolderPanel("배포 폴더 선택", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        int ai = selected.IndexOf("Assets", StringComparison.Ordinal);
                        _deployPath = ai >= 0 ? selected[ai..] : selected;
                    }
                }
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);
                DrawSubLabel("배포 대상 선택");

                EditorGUILayout.BeginHorizontal();
                _selRemoteConfig = EditorGUILayout.ToggleLeft("Remote Config", _selRemoteConfig, GUILayout.Width(130));
                _selCloudCode = EditorGUILayout.ToggleLeft("Cloud Code", _selCloudCode, GUILayout.Width(100));
                _selEconomy = EditorGUILayout.ToggleLeft("Economy", _selEconomy, GUILayout.Width(80));
                _selLeaderboards = EditorGUILayout.ToggleLeft("LB", _selLeaderboards, GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                _selScheduler = EditorGUILayout.ToggleLeft("Scheduler", _selScheduler, GUILayout.Width(80));
                _selTriggers = EditorGUILayout.ToggleLeft("Triggers", _selTriggers, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();
                if (DrawColorBtn("Dry Run", COL_MUTED, 24))
                    RunDeploy(true);

                bool anySelected = _selRemoteConfig || _selCloudCode || _selEconomy || _selLeaderboards || _selScheduler || _selTriggers;
                GUI.enabled = anySelected;
                if (DrawColorBtn("Deploy Selected", COL_SUCCESS, 24))
                    RunDeploy(false);
                GUI.enabled = true;

                if (DrawColorBtn("Deploy All", COL_INFO, 24))
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
                if (DrawColorBtn("Fetch All", COL_INFO, 24))
                    FetchFromServer();

                EndBody();
            }

            DrawLoading("실행 중...");
        }

        // ─── Deploy ──────────────────────────────────

        void RunDeploy(bool dryRun)
        {
            var services = new List<string>();
            if (_selRemoteConfig) services.Add("remote-config");
            if (_selCloudCode) services.Add("cloud-code-scripts");
            if (_selEconomy) services.Add("economy");
            if (_selLeaderboards) services.Add("leaderboards");
            if (_selScheduler) services.Add("scheduler");
            if (_selTriggers) services.Add("triggers");

            if (services.Count == 0) return;

            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            string dryFlag = dryRun ? " --dry-run" : "";
            string svcFlag = $"-s {string.Join(" -s ", services)}";
            string cmd = $"deploy \"{_deployPath}\" {svcFlag}{dryFlag}";

            UGSCliRunner.RunAsync(cmd, result =>
            {
                if (result.Success)
                {
                    string prefix = dryRun ? "[Dry Run] " : "";
                    string output = !string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "";
                    _lastSuccess = $"{prefix}완료{output}";

                    // Economy는 publish 필요
                    if (!dryRun && _selEconomy)
                    {
                        UGSCliRunner.RunAsync("economy publish", pubResult =>
                        {
                            _isLoading = false;
                            if (!pubResult.Success)
                                _lastSuccess += "\n(Economy publish 실패 — Dashboard에서 수동 publish 필요)";
                        });
                    }
                    else
                        _isLoading = false;
                }
                else
                {
                    _isLoading = false;
                    var sb = new StringBuilder($"실패 (exit {result.ExitCode})");
                    if (!string.IsNullOrEmpty(result.Error)) sb.Append($"\n{result.Error}");
                    if (!string.IsNullOrEmpty(result.Output)) sb.Append($"\n{result.Output}");
                    _lastError = sb.ToString();
                }
            });
        }

        void DeployAll()
        {
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            UGSCliRunner.RunAsync($"deploy \"{_deployPath}\"", result =>
            {
                if (result.Success)
                {
                    string output = !string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "";
                    _lastSuccess = $"전체 배포 완료{output}";

                    UGSCliRunner.RunAsync("economy publish", pubResult =>
                    {
                        _isLoading = false;
                        if (!pubResult.Success)
                            _lastSuccess += "\n(Economy publish 실패)";
                    });
                }
                else
                {
                    _isLoading = false;
                    _lastError = $"배포 실패: {result.Error}";
                }
            });
        }

        void FetchFromServer()
        {
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            UGSCliRunner.RunAsync($"fetch \"{_deployPath}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                    _lastSuccess = "Fetch 완료" + (!string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "");
                else
                    _lastError = $"Fetch 실패: {result.Error}";
            });
        }
    }
}
#endif
