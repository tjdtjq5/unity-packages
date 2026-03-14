#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
