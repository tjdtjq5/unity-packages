#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>UGS Cloud Code 탭. 스크립트 목록/배포/삭제.</summary>
    public class CloudCodeTab : UGSTabBase
    {
        public override string TabName => "Code";
        public override Color TabColor => new(0.65f, 0.50f, 0.95f);
        protected override string DashboardPath => "cloud-code/scripts";

        List<ScriptEntry> _scripts = new();
        bool _foldScripts = true;
        bool _foldDeploy;
        string _deployPath;

        struct ScriptEntry
        {
            public string Name;
            public string LastModified;
        }

        protected override void FetchData()
        {
            if (string.IsNullOrEmpty(_deployPath))
                _deployPath = UGSConfig.CloudCodePath;

            _isLoading = true;
            UGSCliRunner.RunAsync("cc scripts list -j -q", result =>
            {
                HandleResult(result, () => ParseScripts(result.Output));
            });
        }

        void ParseScripts(string json)
        {
            _scripts.Clear();
            try
            {
                int idx = 0;
                while (true)
                {
                    int nameIdx = json.IndexOf("\"name\"", idx, StringComparison.Ordinal);
                    if (nameIdx < 0) break;

                    string name = ExtractJsonValue(json, nameIdx);

                    // lastModifiedDate 또는 dateModified
                    string modified = "";
                    int modIdx = json.IndexOf("Modified", nameIdx, StringComparison.OrdinalIgnoreCase);
                    if (modIdx >= 0 && modIdx < nameIdx + 300)
                    {
                        modified = ExtractJsonValue(json, modIdx - 5);
                        // ISO 날짜에서 날짜 부분만
                        if (modified.Length > 10) modified = modified[..10];
                    }

                    _scripts.Add(new ScriptEntry { Name = name, LastModified = modified });
                    idx = nameIdx + 1;
                }
            }
            catch (Exception e)
            {
                _lastError = $"파싱 실패: {e.Message}";
            }
        }

        static string ExtractJsonValue(string json, int keyIdx)
        {
            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return "";
            int quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return "";
            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return "";
            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        public override void OnDraw()
        {
            DrawToolbar();
            DrawError();
            DrawLoading();

            if (_isLoading) return;

            GUILayout.Space(4);

            // 스크립트 목록
            if (DrawSectionFoldout(ref _foldScripts, $"Scripts ({_scripts.Count})", TabColor))
            {
                BeginBody();

                if (_scripts.Count == 0)
                {
                    EditorGUILayout.LabelField("등록된 스크립트 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                }
                else
                {
                    // 헤더
                    EditorGUILayout.BeginHorizontal();
                    DrawHeaderLabel("이름");
                    DrawHeaderLabel("최종 수정", 90);
                    DrawHeaderLabel("액션", 50);
                    EditorGUILayout.EndHorizontal();

                    // 행
                    for (int i = 0; i < _scripts.Count; i++)
                    {
                        var script = _scripts[i];
                        var rowBg = i % 2 == 0 ? BG_CARD : BG_SECTION;

                        EditorGUILayout.BeginHorizontal(GetBgStyle(rowBg));

                        DrawCellLabel(script.Name);
                        DrawCellLabel(script.LastModified, 90, COL_MUTED);

                        if (DrawColorBtn("✕", COL_ERROR, 20))
                        {
                            if (EditorUtility.DisplayDialog("스크립트 삭제",
                                $"'{script.Name}' 스크립트를 삭제하시겠습니까?", "삭제", "취소"))
                            {
                                DeleteScript(script.Name);
                            }
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }

                EndBody();
            }

            GUILayout.Space(8);

            // 배포 설정
            if (DrawSectionFoldout(ref _foldDeploy, "배포", COL_WARN))
            {
                BeginBody();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("폴더:", GUILayout.Width(40));
                _deployPath = EditorGUILayout.TextField(_deployPath);

                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string selected = EditorUtility.OpenFolderPanel("Cloud Code 폴더 선택",
                        "Assets/_Project", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        // 프로젝트 상대 경로로 변환
                        int assetsIdx = selected.IndexOf("Assets", StringComparison.Ordinal);
                        _deployPath = assetsIdx >= 0 ? selected[assetsIdx..] : selected;
                    }
                }

                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);

                if (DrawColorBtn("Deploy Scripts", COL_SUCCESS, 26))
                    DeployScripts();

                EndBody();
            }
        }

        void DeleteScript(string name)
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"cc scripts delete {name}", result =>
            {
                HandleResult(result, () => FetchData());
            });
        }

        void DeployScripts()
        {
            _isLoading = true;
            UGSCliRunner.RunAsync($"deploy \"{_deployPath}\" -s cloud-code-scripts", result =>
            {
                HandleResult(result, () => FetchData());
            });
        }
    }
}
#endif
