#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>UGS 환경 관리 탭. 환경 목록/전환/생성.</summary>
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
        // ID는 나머지 공간 사용 (width 지정 안 함)

        string _activeEnv = "";
        List<EnvEntry> _envList = new();
        bool _foldList = true;
        bool _foldCreate;
        bool _foldSync;
        int _syncSrcIdx = -1, _syncDstIdx = -1;
        bool _syncRC = true, _syncCC = true, _syncEC = true;
        string _newEnvName = "";

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
                _lastError = $"파싱 실패: {e.Message}\n{json}";
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
            while (start < block.Length && (block[start] == ' ' || block[start] == '\t')) start++;
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

        public override void OnDraw()
        {
            DrawToolbar();
            DrawError();
            DrawLoading();

            if (_isLoading) return;

            GUILayout.Space(4);

            // 현재 환경 카드
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawStatCard("현재 환경", _activeEnv, COL_SUCCESS);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // 환경 목록
            if (DrawSectionFoldout(ref _foldList, $"환경 목록 ({_envList.Count})", TabColor))
            {
                BeginBody();

                // 헤더
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(COL_W_STATUS);
                DrawHeaderLabel("이름", COL_W_NAME);
                DrawHeaderLabel("ID");
                DrawHeaderLabel("액션", COL_W_ACTION);
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(2);

                // 행
                for (int i = 0; i < _envList.Count; i++)
                {
                    var env = _envList[i];
                    bool isActive = env.Name == _activeEnv;
                    var rowBg = i % 2 == 0 ? BG_CARD : BG_SECTION;

                    EditorGUILayout.BeginHorizontal(GetBgStyle(rowBg), GUILayout.Height(22));

                    // 상태 아이콘
                    DrawCellLabel(isActive ? "●" : "○", COL_W_STATUS,
                        isActive ? COL_SUCCESS : COL_MUTED);

                    // 이름
                    DrawCellLabel(env.Name, COL_W_NAME,
                        isActive ? COL_SUCCESS : Color.white);

                    // ID (나머지 공간)
                    DrawCellLabel(env.Id, 0, COL_MUTED);

                    // 액션
                    if (isActive)
                    {
                        EditorGUILayout.LabelField("활성", new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal = { textColor = COL_SUCCESS },
                            alignment = TextAnchor.MiddleCenter
                        }, GUILayout.Width(COL_W_ACTION));
                    }
                    else
                    {
                        if (DrawColorBtn("전환", COL_INFO, 20))
                            SwitchEnvironment(env.Name);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EndBody();
            }

            GUILayout.Space(8);

            // 새 환경 생성
            if (DrawSectionFoldout(ref _foldCreate, "새 환경 생성", COL_WARN))
            {
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

            GUILayout.Space(8);
            DrawSyncSection();
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
                _syncSrcIdx = System.Array.IndexOf(envNames, "dev");
                _syncDstIdx = System.Array.IndexOf(envNames, "production");
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

        void RunFullSync(string srcEnv, string dstEnv)
        {
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;

            var services = new List<(string service, bool needsPublish, string publishCmd)>();
            if (_syncRC) services.Add(("remote-config", false, null));
            if (_syncCC) services.Add(("cloud-code-scripts", false, null));
            if (_syncEC) services.Add(("economy", true, "economy publish"));

            string deployPath = UGSConfig.DeployPath;
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
                _lastSuccess = $"{srcEnv} → {dstEnv} 동기화 완료 ({successCount}/{services.Count} 서비스)";
                return;
            }

            var (service, needsPublish, publishCmd) = services[idx];

            UGSCliRunner.RunAsync($"fetch \"{dir}\" -s {service} -e {srcEnv}", fetchResult =>
            {
                if (!fetchResult.Success)
                {
                    _isLoading = false;
                    _lastError = $"{service} fetch 실패: {fetchResult.Error}";
                    return;
                }

                UGSCliRunner.RunAsync($"deploy \"{dir}\" -s {service} -e {dstEnv}", deployResult =>
                {
                    if (!deployResult.Success)
                    {
                        _isLoading = false;
                        _lastError = $"{service} deploy 실패: {deployResult.Error}";
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
                HandleResult(result, () =>
                {
                    _newEnvName = "";
                    FetchData();
                });
            });
        }
    }
}
#endif
