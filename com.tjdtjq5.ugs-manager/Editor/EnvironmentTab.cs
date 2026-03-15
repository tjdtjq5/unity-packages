#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>UGS 환경 관리 탭. 환경 목록/전환/생성 + 통합 배포 + 전체 동기화.</summary>
    public class EnvironmentTab : UGSTabBase
    {
        public override string TabName => "Env";
        public override Color TabColor => new(0.40f, 0.75f, 0.95f);
        protected override string DashboardPath => "environments";
        protected override bool IsProjectLevelPath => true;

        // 컬럼 너비 상수
        const float COL_W_STATUS = 24f;
        const float COL_W_NAME = 120f;
        const float COL_W_ACTION = 60f;

        string _activeEnv = "";
        List<EnvEntry> _envList = new();
        bool _foldList = true;
        bool _foldCreate;
        bool _foldDeploy;
        bool _foldFetch;
        bool _foldSync;
        int _syncSrcIdx = -1, _syncDstIdx = -1;
        bool _syncRC = true, _syncCC = true, _syncEC = true;
        string _newEnvName = "";

        // Deploy
        string _deployPath;
        bool _selRemoteConfig = true;
        bool _selCloudCode = true;
        bool _selEconomy = true;
        bool _selLeaderboards = true;
        bool _selScheduler = true;
        bool _selTriggers = true;

        struct EnvEntry
        {
            public string Name;
            public string Id;
        }

        protected override void FetchData()
        {
            _isLoading = true;
            _activeEnv = UGSCliRunner.GetEnvironment();
            UGSTabBase.InvalidateEnvCache();

            if (string.IsNullOrEmpty(_deployPath))
                _deployPath = UGSConfig.DeployPath;

            UGSCliRunner.RunAsync("env list -j -q", result =>
            {
                HandleResult(result, () => ParseEnvList(result.Output));
            });
        }

        void ParseEnvList(string json)
        {
            _envList.Clear();
            try
            {
                int searchFrom = 0;
                while (true)
                {
                    int objStart = json.IndexOf('{', searchFrom);
                    if (objStart < 0) break;
                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    string block = json.Substring(objStart, objEnd - objStart + 1);
                    string id = ExtractField(block, "id");
                    string name = ExtractField(block, "name");

                    if (!string.IsNullOrEmpty(name))
                        _envList.Add(new EnvEntry { Name = name, Id = id });

                    searchFrom = objEnd + 1;
                }
            }
            catch (Exception e)
            {
                ShowNotification($"파싱 실패: {e.Message}", NotificationType.Error);
            }
        }

        static string ExtractField(string block, string field)
        {
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

        // ─── 메인 UI ────────────────────────────────

        public override void OnDraw()
        {
            DrawToolbar();
            DrawNotifications();
            DrawLoading(_isLoading);

            if (_isLoading) return;

            GUILayout.Space(4);

            // 현재 환경 카드
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawStatCard("현재 환경", _activeEnv, COL_SUCCESS);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            DrawEnvList();
            GUILayout.Space(8);
            DrawCreateSection();
            GUILayout.Space(8);
            DrawDeploySection();
            GUILayout.Space(8);
            DrawFetchSection();
            GUILayout.Space(8);
            DrawSyncSection();
        }

        // ─── 환경 목록 ──────────────────────────────

        void DrawEnvList()
        {
            if (!DrawSectionFoldout(ref _foldList, $"환경 목록 ({_envList.Count})", TabColor)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(COL_W_STATUS);
            DrawHeaderLabel("이름", COL_W_NAME);
            DrawHeaderLabel("ID");
            DrawHeaderLabel("액션", COL_W_ACTION);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);

            for (int i = 0; i < _envList.Count; i++)
            {
                var env = _envList[i];
                bool isActive = env.Name == _activeEnv;
                var rowBg = i % 2 == 0 ? BG_CARD : BG_SECTION;

                EditorGUILayout.BeginHorizontal(GetBgStyle(rowBg), GUILayout.Height(22));
                DrawCellLabel(isActive ? "●" : "○", COL_W_STATUS, isActive ? COL_SUCCESS : COL_MUTED);
                DrawCellLabel(env.Name, COL_W_NAME, isActive ? COL_SUCCESS : Color.white);
                DrawCellLabel(env.Id, 0, COL_MUTED);

                if (isActive)
                    EditorGUILayout.LabelField("활성", new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = COL_SUCCESS }, alignment = TextAnchor.MiddleCenter },
                        GUILayout.Width(COL_W_ACTION));
                else if (DrawColorBtn("전환", COL_INFO, 20))
                    SwitchEnvironment(env.Name);

                EditorGUILayout.EndHorizontal();
            }

            EndBody();
        }

        // ─── 새 환경 생성 ────────────────────────────

        void DrawCreateSection()
        {
            if (!DrawSectionFoldout(ref _foldCreate, "새 환경 생성", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("이름:", GUILayout.Width(40));
            _newEnvName = EditorGUILayout.TextField(_newEnvName);
            GUILayout.Space(4);
            GUI.enabled = !string.IsNullOrWhiteSpace(_newEnvName);
            if (DrawColorBtn("생성", COL_SUCCESS, 22))
                CreateEnvironment(_newEnvName.Trim());
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            EditorGUILayout.LabelField("※ 환경 이름은 생성 후 변경 불가 (CLI 미지원)",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED } });

            EndBody();
        }

        // ─── 배포 (로컬 → 서버) ─────────────────────

        void DrawDeploySection()
        {
            if (!DrawSectionFoldout(ref _foldDeploy, "배포 (로컬 → 서버)", COL_SUCCESS)) return;
            BeginBody();

            // 경로 (읽기 전용 — Settings에서 변경)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("경로:", GUILayout.Width(40));
            EditorGUILayout.LabelField(_deployPath ?? "", new GUIStyle(EditorStyles.label)
                { normal = { textColor = COL_MUTED } });
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
            if (DrawColorBtn("미리보기", COL_MUTED, 24))
                RunDeploy(true);
            bool anySelected = _selRemoteConfig || _selCloudCode || _selEconomy || _selLeaderboards || _selScheduler || _selTriggers;
            GUI.enabled = anySelected;
            if (DrawColorBtn("선택 배포", COL_SUCCESS, 24))
                RunDeploy(false);
            GUI.enabled = true;
            if (DrawColorBtn("전체 배포", COL_INFO, 24))
                DeployAll();
            EditorGUILayout.EndHorizontal();

            EndBody();
        }

        // ─── Fetch (서버 → 로컬) ────────────────────

        void DrawFetchSection()
        {
            if (!DrawSectionFoldout(ref _foldFetch, "Fetch (서버 → 로컬)", COL_INFO)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("경로:", GUILayout.Width(40));
            EditorGUILayout.LabelField(_deployPath ?? "", new GUIStyle(EditorStyles.label)
                { normal = { textColor = COL_MUTED } });
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (DrawColorBtn("Fetch All", COL_INFO, 24))
                FetchFromServer();

            EndBody();
        }

        // ─── 전체 환경 동기화 ────────────────────────

        void DrawSyncSection()
        {
            if (!DrawSectionFoldout(ref _foldSync, "전체 환경 동기화", COL_WARN)) return;
            BeginBody();

            if (_envList.Count < 2)
            {
                EditorGUILayout.LabelField("환경 2개 이상 필요",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                EndBody();
                return;
            }

            string[] envNames = _envList.Select(e => e.Name).ToArray();

            if (_syncSrcIdx < 0)
            {
                _syncSrcIdx = Array.IndexOf(envNames, "dev");
                _syncDstIdx = Array.IndexOf(envNames, "production");
                if (_syncSrcIdx < 0) _syncSrcIdx = 0;
                if (_syncDstIdx < 0) _syncDstIdx = envNames.Length > 1 ? 1 : 0;
            }

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("소스:", GUILayout.Width(35));
            _syncSrcIdx = EditorGUILayout.Popup(_syncSrcIdx, envNames, GUILayout.Width(120));
            EditorGUILayout.LabelField("→", GUILayout.Width(20));
            EditorGUILayout.LabelField("대상:", GUILayout.Width(35));
            _syncDstIdx = EditorGUILayout.Popup(_syncDstIdx, envNames, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            DrawSubLabel("동기화 대상");

            EditorGUILayout.BeginHorizontal();
            _syncRC = EditorGUILayout.ToggleLeft("Remote Config", _syncRC, GUILayout.Width(130));
            _syncCC = EditorGUILayout.ToggleLeft("Cloud Code", _syncCC, GUILayout.Width(100));
            _syncEC = EditorGUILayout.ToggleLeft("Economy", _syncEC, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool anyService = _syncRC || _syncCC || _syncEC;
            GUI.enabled = _syncSrcIdx != _syncDstIdx && anyService && !_isLoading;
            if (DrawColorBtn("전체 동기화 실행", COL_WARN, 22))
            {
                if (EditorUtility.DisplayDialog("전체 환경 동기화",
                    $"{envNames[_syncSrcIdx]} → {envNames[_syncDstIdx]}\n선택한 서비스를 동기화하시겠습니까?", "실행", "취소"))
                    RunFullSync(envNames[_syncSrcIdx], envNames[_syncDstIdx]);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EndBody();
        }

        // ─── 환경 전환/생성 ─────────────────────────

        void SwitchEnvironment(string name)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"config set environment-name {name}", result =>
            {
                HandleResult(result, () =>
                {
                    _activeEnv = name;
                    UGSTabBase.InvalidateEnvCache();
                    var window = EditorWindow.GetWindow<UGSWindow>();
                    if (window != null) window.OnEnvironmentChanged();
                });
            });
        }

        void CreateEnvironment(string name)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"env add {name}", result =>
            {
                HandleResult(result, () => { _newEnvName = ""; FetchData(); });
            });
        }

        // ─── Deploy 명령 ────────────────────────────

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
            _notification = null;

            string dryFlag = dryRun ? " --dry-run" : "";
            string svcFlag = $"-s {string.Join(" -s ", services)}";

            UGSCliRunner.RunAsync($"deploy \"{_deployPath}\" {svcFlag}{dryFlag}", result =>
            {
                if (result.Success)
                {
                    string prefix = dryRun ? "[Dry Run] " : "";
                    string output = !string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "";
                    ShowNotification($"{prefix}완료{output}", NotificationType.Success);

                    if (!dryRun && _selEconomy)
                    {
                        UGSCliRunner.RunAsync("economy publish", pubResult =>
                        {
                            _isLoading = false;
                            if (!pubResult.Success)
                                ShowNotification(_notification + "\n(Economy publish 실패)", NotificationType.Success);
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
                    ShowNotification(sb.ToString(), NotificationType.Error);
                }
            });
        }

        void DeployAll()
        {
            _isLoading = true;
            _notification = null;

            UGSCliRunner.RunAsync($"deploy \"{_deployPath}\"", result =>
            {
                if (result.Success)
                {
                    string output = !string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "";
                    ShowNotification($"전체 배포 완료{output}", NotificationType.Success);

                    UGSCliRunner.RunAsync("economy publish", pubResult =>
                    {
                        _isLoading = false;
                        if (!pubResult.Success)
                            ShowNotification(_notification + "\n(Economy publish 실패)", NotificationType.Success);
                    });
                }
                else
                {
                    _isLoading = false;
                    ShowNotification($"배포 실패: {result.Error}", NotificationType.Error);
                }
            });
        }

        void FetchFromServer()
        {
            _isLoading = true;
            _notification = null;

            UGSCliRunner.RunAsync($"fetch \"{_deployPath}\"", result =>
            {
                _isLoading = false;
                if (result.Success)
                    ShowNotification("Fetch 완료" + (!string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : ""), NotificationType.Success);
                else
                    ShowNotification($"Fetch 실패: {result.Error}", NotificationType.Error);
            });
        }

        // ─── 전체 동기화 실행 ────────────────────────

        void RunFullSync(string srcEnv, string dstEnv)
        {
            _isLoading = true;
            _notification = null;

            var services = new List<(string service, bool needsPublish, string publishCmd)>();
            if (_syncRC) services.Add(("remote-config", false, null));
            if (_syncCC) services.Add(("cloud-code-scripts", false, null));
            if (_syncEC) services.Add(("economy", true, "economy publish"));

            string deployPath = _deployPath;
            if (string.IsNullOrEmpty(deployPath)) deployPath = "Assets/UGS";

            string projectRoot = Application.dataPath.Replace("/Assets", "");
            string absPath = System.IO.Path.Combine(projectRoot, deployPath).Replace('\\', '/');
            if (!System.IO.Directory.Exists(absPath))
                System.IO.Directory.CreateDirectory(absPath);

            SyncNext(services, 0, srcEnv, dstEnv, absPath, 0);
        }

        void SyncNext(List<(string service, bool needsPublish, string publishCmd)> services,
            int idx, string srcEnv, string dstEnv, string dir, int successCount)
        {
            if (idx >= services.Count)
            {
                _isLoading = false;
                ShowNotification($"{srcEnv} → {dstEnv} 동기화 완료 ({successCount}/{services.Count} 서비스)", NotificationType.Success);
                return;
            }

            var (service, needsPublish, publishCmd) = services[idx];

            UGSCliRunner.RunAsync($"fetch \"{dir}\" -s {service} -e {srcEnv}", fetchResult =>
            {
                if (!fetchResult.Success)
                {
                    _isLoading = false;
                    ShowNotification($"{service} fetch 실패: {fetchResult.Error}", NotificationType.Error);
                    return;
                }

                UGSCliRunner.RunAsync($"deploy \"{dir}\" -s {service} -e {dstEnv}", deployResult =>
                {
                    if (!deployResult.Success)
                    {
                        _isLoading = false;
                        ShowNotification($"{service} deploy 실패: {deployResult.Error}", NotificationType.Error);
                        return;
                    }

                    if (needsPublish && !string.IsNullOrEmpty(publishCmd))
                    {
                        UGSCliRunner.RunAsync($"{publishCmd} -e {dstEnv}", pubResult =>
                        {
                            SyncNext(services, idx + 1, srcEnv, dstEnv, dir,
                                successCount + (pubResult.Success ? 1 : 0));
                        });
                    }
                    else
                    {
                        SyncNext(services, idx + 1, srcEnv, dstEnv, dir, successCount + 1);
                    }
                });
            });
        }
    }
}
#endif
