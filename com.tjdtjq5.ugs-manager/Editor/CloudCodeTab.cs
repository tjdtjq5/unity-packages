#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// Cloud Code 탭. 로컬/서버 스크립트 목록, 동기화·구현 상태, @desc/@param 파싱,
    /// Deploy + 파라미터 스키마 자동 등록, 프롬프트 복사, 스크립트 테스트.
    /// </summary>
    public class CloudCodeTab : UGSTabBase
    {
        public override string TabName => "Cloud Code";
        public override Color TabColor => new(0.65f, 0.50f, 0.95f);
        protected override string DashboardPath => "cloud-code/scripts";

        // ─── 서브 탭 ────────────────────────────────
        int _subTabIdx;
        static readonly string[] SUB_TAB_LABELS = { "Scripts", "Schedules", "Triggers" };
        SchedulerTab _schedulerSub;
        TriggersTab _triggersSub;

        // ─── 데이터 ──────────────────────────────────
        List<LocalScript> _localScripts = new();
        List<ServerScript> _serverScripts = new();
        List<MergedScript> _mergedScripts = new();
        string _scriptDir = "";

        // ─── UI 상태 ────────────────────────────────
        bool _foldScripts = true;
        bool _foldCreate;
        bool _foldTest;
        ResizableColumns _columns;
        const int COL_STATUS = 0, COL_NAME = 1, COL_DESC = 2, COL_DATE = 3, COL_ACT = 4;

        // 새 스크립트
        string _newName = "";
        string _newDesc = "";

        // 커스텀 그룹
        int _groupIdx;
        List<ScriptGroup> _groups = new();
        const string KEY_CC_GROUPS = "UGS_CC_Groups";

        struct ScriptGroup
        {
            public string Name;
            public List<string> ScriptNames;
        }

        // 테스트
        int _testScriptIdx;

        // FileSystemWatcher
        FileSystemWatcher _watcher;

        // ─── 데이터 모델 ─────────────────────────────

        struct LocalScript
        {
            public string Name;
            public string FilePath;
            public string Description;
            public List<ParamInfo> Params;
            public bool IsImplemented;  // true = // TODO 없음
            public string LocalModified; // yyyy-MM-dd
        }

        struct ServerScript
        {
            public string Name;
            public string LastModified;
        }

        struct MergedScript
        {
            public string Name;
            public string Description;
            public List<ParamInfo> Params;
            public string FilePath;
            public string ModifiedDate;  // 로컬 or 서버 날짜
            public bool IsImplemented;
            public SyncState Status;
        }

        enum SyncState { Synced, LocalOnly, ServerOnly }

        struct ParamInfo
        {
            public string Name;
            public string Type;     // NUMERIC, STRING, BOOLEAN, JSON, ANY
            public bool Required;
            public string Description;
        }

        // ─── 라이프사이클 ────────────────────────────

        public override void OnEnable()
        {
            _schedulerSub ??= new SchedulerTab();
            _triggersSub ??= new TriggersTab();
            _schedulerSub.OnEnable();
            _triggersSub.OnEnable();
            base.OnEnable();
            SetupWatcher();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            DisposeWatcher();
            _schedulerSub?.OnDisable();
            _triggersSub?.OnDisable();
        }

        void SetupWatcher()
        {
            DisposeWatcher();
            if (string.IsNullOrEmpty(_scriptDir) || !Directory.Exists(_scriptDir)) return;

            try
            {
                _watcher = new FileSystemWatcher(_scriptDir, "*.js")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Deleted += OnFileChanged;
                _watcher.Renamed += (_, _) => OnFileChanged(null, null);
            }
            catch { /* ignore */ }
        }

        void DisposeWatcher()
        {
            if (_watcher == null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // 메인 스레드에서 갱신
            EditorApplication.delayCall += () =>
            {
                ScanLocalScripts();
                BuildMergedList();
                var wnd = EditorWindow.GetWindow<UGSWindow>();
                if (wnd != null) wnd.Repaint();
            };
        }

        // ─── 데이터 로드 ─────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _lastError = null;

            _columns ??= new ResizableColumns("UGS_CC", new[]
            {
                new ColDef("상태",  36f),
                new ColDef("이름", 130f, resizable: true),
                new ColDef("설명",   0f),   // flex
                new ColDef("수정일", 72f, resizable: true),
                new ColDef("",      50f),   // 액션
            });

            ResolveScriptDir();
            ScanLocalScripts();
            SetupWatcher();

            // 로컬만으로 우선 병합 (서버 조회 전에도 목록 표시)
            BuildMergedList();
            LoadGroups();

            _isLoading = true;
            UGSCliRunner.RunAsync("cc scripts list -j -q", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastError = null;
                    _lastRefreshTime = DateTime.Now;
                    ParseServerScripts(result.Output);
                }
                else
                {
                    _serverScripts.Clear();
                    Debug.LogWarning($"[UGS] cc scripts list 실패: {result.Error}");
                }
                BuildMergedList();
                LoadGroups();
            });
        }

        void ResolveScriptDir()
        {
            string configPath = UGSConfig.CloudCodePath;
            if (string.IsNullOrEmpty(configPath))
                configPath = "Assets/UGS/CloudCode";

            string projectRoot = Application.dataPath.Replace("/Assets", "");
            string abs = Path.Combine(projectRoot, configPath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(abs)) { _scriptDir = abs; return; }

            foreach (var dir in Directory.GetDirectories(Application.dataPath, "CloudCode", SearchOption.AllDirectories))
            {
                if (dir.Contains("Library") || dir.Contains("Temp") || dir.Contains("PackageCache")) continue;
                if (Directory.GetFiles(dir, "*.js").Length > 0) { _scriptDir = dir; return; }
            }
            _scriptDir = abs;
        }

        // ─── 로컬 스캔 ──────────────────────────────

        void ScanLocalScripts()
        {
            _localScripts.Clear();
            if (!Directory.Exists(_scriptDir)) return;

            foreach (var file in Directory.GetFiles(_scriptDir, "*.js"))
                _localScripts.Add(ParseJsFile(file));
        }

        static LocalScript ParseJsFile(string filePath)
        {
            var script = new LocalScript
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                Description = "",
                Params = new List<ParamInfo>(),
                IsImplemented = true
            };

            try
            {
                string content = File.ReadAllText(filePath);
                script.IsImplemented = !content.Contains("// TODO:");

                var modTime = File.GetLastWriteTime(filePath);
                script.LocalModified = modTime.ToString("yyyy-MM-dd");

                foreach (var line in content.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("// @desc "))
                        script.Description = trimmed.Substring(9).Trim();
                    else if (trimmed.StartsWith("// @param "))
                        script.Params.Add(ParseParam(trimmed.Substring(10)));
                    else if (!trimmed.StartsWith("//") && !string.IsNullOrWhiteSpace(trimmed))
                        break;
                }
            }
            catch { /* ignore */ }

            return script;
        }

        static ParamInfo ParseParam(string text)
        {
            int dashIdx = text.IndexOf(" - ", StringComparison.Ordinal);
            string meta = dashIdx >= 0 ? text.Substring(0, dashIdx) : text;
            string desc = dashIdx >= 0 ? text.Substring(dashIdx + 3).Trim() : "";
            string[] parts = meta.Split(':');

            return new ParamInfo
            {
                Name = parts.Length > 0 ? parts[0].Trim() : "",
                Type = parts.Length > 1 ? parts[1].Trim().ToUpper() : "ANY",
                Required = parts.Length > 2 && parts[2].Trim().Equals("required", StringComparison.OrdinalIgnoreCase),
                Description = desc
            };
        }

        // ─── 서버 파싱 ──────────────────────────────

        void ParseServerScripts(string json)
        {
            _serverScripts.Clear();
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                int idx = 0;
                while (true)
                {
                    int nameIdx = json.IndexOf("\"Name\"", idx, StringComparison.Ordinal);
                    if (nameIdx < 0) { nameIdx = json.IndexOf("\"name\"", idx, StringComparison.Ordinal); }
                    if (nameIdx < 0) break;

                    string name = ExtractJsonValue(json, nameIdx);
                    string modified = "";
                    int modIdx = json.IndexOf("Published", nameIdx, StringComparison.OrdinalIgnoreCase);
                    if (modIdx >= 0 && modIdx < nameIdx + 300)
                    {
                        modified = ExtractJsonValue(json, modIdx - 5);
                        if (modified.Length > 10) modified = modified[..10];
                    }

                    _serverScripts.Add(new ServerScript { Name = name, LastModified = modified });
                    idx = nameIdx + 1;
                }
            }
            catch (Exception e) { _lastError = $"파싱 실패: {e.Message}"; }
        }

        static string ExtractJsonValue(string json, int keyIdx)
        {
            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return "";
            int qs = json.IndexOf('"', colonIdx + 1);
            if (qs < 0) return "";
            int qe = json.IndexOf('"', qs + 1);
            return qe > qs ? json.Substring(qs + 1, qe - qs - 1) : "";
        }

        // ─── 병합 ──────────────────────────────────

        void BuildMergedList()
        {
            _mergedScripts.Clear();
            var serverNames = new HashSet<string>(_serverScripts.Select(s => s.Name));
            var localNames = new HashSet<string>(_localScripts.Select(s => s.Name));

            foreach (var local in _localScripts)
            {
                var merged = new MergedScript
                {
                    Name = local.Name,
                    Description = local.Description,
                    Params = local.Params,
                    FilePath = local.FilePath,
                    IsImplemented = local.IsImplemented,
                    ModifiedDate = local.LocalModified,
                    Status = serverNames.Contains(local.Name) ? SyncState.Synced : SyncState.LocalOnly
                };
                _mergedScripts.Add(merged);
            }

            foreach (var server in _serverScripts)
            {
                if (!localNames.Contains(server.Name))
                {
                    _mergedScripts.Add(new MergedScript
                    {
                        Name = server.Name,
                        ModifiedDate = server.LastModified,
                        Status = SyncState.ServerOnly,
                        Params = new List<ParamInfo>()
                    });
                }
            }
        }

        // ─── 메인 UI ────────────────────────────────

        public override void OnDraw()
        {
            // 서브 탭 (Scripts / Schedules / Triggers)
            var subColors = new[] { TabColor, new Color(0.75f, 0.60f, 0.85f), new Color(0.90f, 0.65f, 0.35f) };
            _subTabIdx = DrawStyledTabs(SUB_TAB_LABELS, _subTabIdx, subColors);
            GUILayout.Space(2);

            switch (_subTabIdx)
            {
                case 0: DrawScriptsContent(); break;
                case 1: _schedulerSub?.OnDraw(); break;
                case 2: _triggersSub?.OnDraw(); break;
            }
        }

        void DrawScriptsContent()
        {
            DrawMainToolbar();
            DrawError();
            DrawSuccess();
            DrawLoading();
            if (_isLoading) return;

            GUILayout.Space(4);
            DrawScriptList();
            GUILayout.Space(8);
            DrawCreateSection();
            DrawTestSection();

            if (!string.IsNullOrEmpty(_scriptDir))
                DrawEnvCopySection("cloud-code-scripts", _scriptDir, onComplete: () => FetchData());
        }

        // ─── 툴바 ──────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (DrawColorBtn("Refresh", COL_INFO, 22)) FetchData();
            if (DrawColorBtn("Deploy ↑", COL_SUCCESS, 22)) DeployScripts();

            GUILayout.Space(8);
            int lc = _localScripts.Count, sc = _serverScripts.Count;
            EditorGUILayout.LabelField($"로컬: {lc} / 서버: {sc}",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleLeft },
                GUILayout.Width(110));

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(DashboardPath) && DrawLinkBtn("Dashboard"))
            {
                if (UGSConfig.IsConfigured)
                {
                    var pid = UGSCliRunner.GetProjectId();
                    if (!string.IsNullOrEmpty(pid))
                    {
                        string url = UGSConfig.GetDashboardUrl(pid, null, DashboardPath);
                        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
                    }
                }
            }

            if (_lastRefreshTime != default)
            {
                var el = DateTime.Now - _lastRefreshTime;
                string t = el.TotalSeconds < 60 ? $"{el.Seconds}초 전" : $"{(int)el.TotalMinutes}분 전";
                EditorGUILayout.LabelField(t, new GUIStyle(EditorStyles.miniLabel)
                    { normal = { textColor = COL_MUTED }, alignment = TextAnchor.MiddleRight }, GUILayout.Width(50));
            }

            EditorGUILayout.EndHorizontal();
        }

        // ─── 스크립트 목록 ──────────────────────────

        void DrawScriptList()
        {
            int total = _mergedScripts.Count;
            if (!DrawSectionFoldout(ref _foldScripts, $"Scripts ({total})", TabColor)) return;
            BeginBody();

            // 커스텀 그룹 탭
            var groupLabels = _groups.Select(g => g.Name).ToArray();
            _groupIdx = DrawStyledTabs(groupLabels, _groupIdx, onAdd: AddGroup, onRename: RenameGroup);

            List<MergedScript> filtered;
            if (_groups.Count == 0 || _groupIdx >= _groups.Count)
                filtered = _mergedScripts;
            else
            {
                var keys = new HashSet<string>(_groups[_groupIdx].ScriptNames);
                filtered = _mergedScripts.Where(s => keys.Contains(s.Name)).ToList();
            }

            if (filtered.Count == 0)
            {
                EditorGUILayout.LabelField("스크립트 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            }
            else
            {
                _columns.DrawHeader();

                for (int i = 0; i < filtered.Count; i++)
                    DrawScriptRow(filtered[i], i);
            }

            EndBody();
        }

        void DrawScriptRow(MergedScript script, int index)
        {
            var bg = index % 2 == 0 ? BG_CARD : BG_SECTION;
            EditorGUILayout.BeginVertical(GetBgStyle(bg));

            // 메인 행
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

            // 상태 아이콘 (2축: 배포 + 구현)
            string icon; Color iconColor; string tooltip;
            switch (script.Status)
            {
                case SyncState.Synced when script.IsImplemented:
                    icon = "●"; iconColor = COL_SUCCESS; tooltip = "동기화 + 구현완료"; break;
                case SyncState.Synced:
                    icon = "◐"; iconColor = COL_WARN; tooltip = "동기화 + 미구현 (TODO 있음)"; break;
                case SyncState.LocalOnly when script.IsImplemented:
                    icon = "○"; iconColor = COL_INFO; tooltip = "로컬만 + 구현완료 (미배포)"; break;
                case SyncState.LocalOnly:
                    icon = "◌"; iconColor = COL_WARN; tooltip = "로컬만 + 미구현 (TODO 있음)"; break;
                default:
                    icon = "☁"; iconColor = COL_MUTED; tooltip = "서버만 (로컬 없음)"; break;
            }
            EditorGUILayout.LabelField(new GUIContent(icon, tooltip),
                new GUIStyle(EditorStyles.label) { normal = { textColor = iconColor }, alignment = TextAnchor.MiddleCenter, fontSize = 13 },
                GUILayout.Width(_columns.GetWidth(COL_STATUS)));

            // 이름 (우클릭 → 그룹 할당)
            var nameRect = GUILayoutUtility.GetRect(_columns.GetWidth(COL_NAME), 18, GUILayout.Width(_columns.GetWidth(COL_NAME)));
            EditorGUI.LabelField(nameRect, script.Name);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && nameRect.Contains(Event.current.mousePosition))
            { ShowScriptGroupMenu(script.Name); Event.current.Use(); }

            // 설명
            bool hasDesc = !string.IsNullOrEmpty(script.Description);
            DrawCellLabel(hasDesc ? script.Description : "(설명 없음)", 0, hasDesc ? (Color?)null : COL_MUTED);

            // 수정일
            DrawCellLabel(script.ModifiedDate ?? "", _columns.GetWidth(COL_DATE), COL_MUTED);

            // 액션 버튼
            DrawActionButtons(script);

            EditorGUILayout.EndHorizontal();

            // 파라미터 행
            if (script.Params is { Count: > 0 })
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(40);
                string paramText = string.Join(", ", script.Params.Select(p =>
                    $"{p.Name}({p.Type}{(p.Required ? ",필수" : "")})"));
                EditorGUILayout.LabelField($"params: {paramText}",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.5f, 0.9f) } });
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        void DrawActionButtons(MergedScript script)
        {
            // 프롬프트 복사 (미구현 로컬 스크립트만)
            if (script.Status != SyncState.ServerOnly && !script.IsImplemented)
            {
                if (GUILayout.Button(new GUIContent("📋", "구현 프롬프트 복사"), EditorStyles.miniButton,
                    GUILayout.Width(22), GUILayout.Height(16)))
                {
                    CopyImplementPrompt(script);
                }
            }

            // 삭제
            if (script.Status == SyncState.ServerOnly)
            {
                // 서버 스크립트 삭제
                if (GUILayout.Button(new GUIContent("✕", "서버에서 삭제"), EditorStyles.miniButton,
                    GUILayout.Width(22), GUILayout.Height(16)))
                {
                    if (EditorUtility.DisplayDialog("스크립트 삭제", $"서버에서 '{script.Name}'을 삭제하시겠습니까?", "삭제", "취소"))
                        DeleteServerScript(script.Name);
                }
            }
            else if (!string.IsNullOrEmpty(script.FilePath))
            {
                // 로컬 파일 삭제
                if (GUILayout.Button(new GUIContent("✕", "로컬 파일 삭제"), EditorStyles.miniButton,
                    GUILayout.Width(22), GUILayout.Height(16)))
                {
                    if (EditorUtility.DisplayDialog("파일 삭제", $"'{script.Name}.js'를 삭제하시겠습니까?", "삭제", "취소"))
                        DeleteLocalScript(script);
                }
            }
            else
            {
                GUILayout.Space(50);
            }
        }

        // ─── 프롬프트 복사 ──────────────────────────

        void CopyImplementPrompt(MergedScript script)
        {
            // Assets/ 상대 경로
            string relativePath = "";
            if (!string.IsNullOrEmpty(script.FilePath))
            {
                int ai = script.FilePath.IndexOf("Assets", StringComparison.Ordinal);
                relativePath = ai >= 0 ? script.FilePath.Substring(ai).Replace('\\', '/') : script.FilePath;
            }

            string rulesPath = "";
            string rulesFullPath = Path.Combine(_scriptDir, "RULES.md");
            if (File.Exists(rulesFullPath))
            {
                int ai = rulesFullPath.IndexOf("Assets", StringComparison.Ordinal);
                rulesPath = ai >= 0 ? rulesFullPath.Substring(ai).Replace('\\', '/') : "RULES.md";
            }

            var sb = new StringBuilder();
            sb.AppendLine("SurvivorsDuo Cloud Code 스크립트를 구현해줘.");
            sb.AppendLine();
            sb.AppendLine($"파일: {relativePath}");
            if (!string.IsNullOrEmpty(script.Description))
                sb.AppendLine($"설명: {script.Description}");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(rulesPath))
                sb.AppendLine($"규칙 파일을 먼저 읽어줘: {rulesPath}");
            sb.AppendLine("완료 후 // TODO 줄은 제거해줘.");
            sb.AppendLine("새로운 규칙이나 API를 사용했다면 RULES.md도 업데이트해줘.");

            EditorGUIUtility.systemCopyBuffer = sb.ToString().TrimEnd();
            Debug.Log("[UGS] 구현 프롬프트가 클립보드에 복사되었습니다.");
        }

        // ─── 새 스크립트 생성 ────────────────────────

        void DrawCreateSection()
        {
            if (!DrawSectionFoldout(ref _foldCreate, "새 스크립트", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("이름:", GUILayout.Width(35));
            _newName = EditorGUILayout.TextField(_newName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("설명:", GUILayout.Width(35));
            _newDesc = EditorGUILayout.TextField(_newDesc);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool isDup = _localScripts.Any(s => s.Name.Equals(_newName?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
            GUI.enabled = !string.IsNullOrWhiteSpace(_newName) && !isDup;
            if (GUILayout.Button("+ 생성", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(18)))
                CreateScript(_newName.Trim(), _newDesc.Trim());
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDup && !string.IsNullOrWhiteSpace(_newName))
                EditorGUILayout.LabelField("이미 존재하는 이름입니다",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_ERROR } });

            EndBody();
        }

        void CreateScript(string name, string desc)
        {
            if (!Directory.Exists(_scriptDir))
                Directory.CreateDirectory(_scriptDir);

            string filePath = Path.Combine(_scriptDir, $"{name}.js");
            string descLine = string.IsNullOrEmpty(desc) ? $"// @desc {name}" : $"// @desc {desc}";

            string template = $@"{descLine}
//
// === 주석 규칙 (구현 후 이 블록 삭제 가능) ===
//   @desc  스크립트 설명 (첫 줄, 필수)
//   @param 이름:타입:required|optional - 설명
//          타입: NUMERIC, STRING, BOOLEAN, JSON, ANY
//   상세: RULES.md 참조
// =============================================
//
// TODO: Claude Code에게 구현 요청 (이 줄이 있으면 '미구현' 상태)
module.exports = async ({{ params, context, logger }}) => {{
  return {{ success: true }};
}};
";
            File.WriteAllText(filePath, template);
            AssetDatabase.Refresh();

            _newName = "";
            _newDesc = "";
            ScanLocalScripts();
            BuildMergedList();
        }

        // ─── 스크립트 테스트 (Dashboard) ─────────────

        void DrawTestSection()
        {
            if (!DrawSectionFoldout(ref _foldTest, "테스트", TabColor)) return;
            BeginBody();

            var testable = _mergedScripts
                .Where(s => s.Status is SyncState.Synced or SyncState.ServerOnly)
                .ToList();

            if (testable.Count == 0)
            {
                EditorGUILayout.LabelField("테스트 가능한 스크립트 없음 (서버에 배포 필요)",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel));
                EndBody();
                return;
            }

            // 스크립트 선택
            string[] names = testable.Select(s => s.Name).ToArray();
            if (_testScriptIdx >= names.Length) _testScriptIdx = 0;

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("스크립트:", GUILayout.Width(60));
            _testScriptIdx = EditorGUILayout.Popup(_testScriptIdx, names);
            EditorGUILayout.EndHorizontal();

            var selected = testable[_testScriptIdx];

            // 파라미터 정보 표시 (읽기 전용)
            if (selected.Params is { Count: > 0 })
            {
                GUILayout.Space(2);
                foreach (var p in selected.Params)
                {
                    string reqLabel = p.Required ? "필수" : "선택";
                    string descPart = string.IsNullOrEmpty(p.Description) ? "" : $" — {p.Description}";
                    EditorGUILayout.LabelField($"  {p.Name} ({p.Type}, {reqLabel}){descPart}",
                        new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.5f, 0.9f) } });
                }
            }
            else
            {
                EditorGUILayout.LabelField("  파라미터 없음",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED } });
            }

            // Dashboard 테스트 버튼
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Dashboard에서 테스트 실행 가능",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED } });

            if (DrawLinkBtn($"{names[_testScriptIdx]} 테스트"))
                OpenScriptDashboard(names[_testScriptIdx]);

            EditorGUILayout.EndHorizontal();

            EndBody();
        }

        void OpenScriptDashboard(string scriptName)
        {
            if (!UGSConfig.IsConfigured) return;
            var pid = UGSCliRunner.GetProjectId();
            if (string.IsNullOrEmpty(pid)) return;
            // Cloud Code 스크립트 상세 페이지
            string url = UGSConfig.GetDashboardUrl(pid, null, $"cloud-code/scripts/{scriptName}");
            if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
        }

        // ─── Deploy ─────────────────────────────────

        void DeployScripts()
        {
            if (!Directory.Exists(_scriptDir))
            {
                _lastError = "스크립트 폴더를 찾을 수 없습니다.";
                return;
            }

            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;
            string dir = _scriptDir.Replace('\\', '/');

            UGSCliRunner.RunAsync($"deploy \"{dir}\" -s cloud-code-scripts", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    _lastSuccess = "Deploy 완료" + (!string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : "");
                    UpdateParamSchemas();
                    FetchData();
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.Append($"Deploy 실패 (exit {result.ExitCode})");
                    if (!string.IsNullOrEmpty(result.Error)) sb.Append($"\n{result.Error}");
                    if (!string.IsNullOrEmpty(result.Output)) sb.Append($"\n{result.Output}");
                    _lastError = sb.ToString();
                }
            });
        }

        // ─── 파라미터 스키마 등록 (REST API) ──────────

        void UpdateParamSchemas()
        {
            var withParams = _localScripts.Where(s => s.Params.Count > 0).ToList();
            if (withParams.Count == 0) return;

            foreach (var script in withParams)
            {
                string json = BuildParamsJson(script.Params);
                UGSCliRunner.PatchScriptParameters(script.Name, json, (success, error) =>
                {
                    if (success)
                        Debug.Log($"[UGS] {script.Name}: 파라미터 스키마 등록 완료");
                    else
                        Debug.LogWarning($"[UGS] {script.Name}: 파라미터 등록 실패 - {error}");
                });
            }
        }

        static string BuildParamsJson(List<ParamInfo> paramList)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < paramList.Count; i++)
            {
                var p = paramList[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(p.Name)
                  .Append("\",\"type\":\"").Append(p.Type)
                  .Append("\",\"required\":").Append(p.Required ? "true" : "false")
                  .Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ─── 삭제 ──────────────────────────────────

        // ─── 그룹 관리 ─────────────────────────────

        void LoadGroups()
        {
            _groups.Clear();
            string raw = EditorPrefs.GetString(KEY_CC_GROUPS, "");
            if (string.IsNullOrEmpty(raw)) return;

            foreach (var part in raw.Split('|'))
            {
                int sep = part.IndexOf(':');
                if (sep < 0) continue;
                _groups.Add(new ScriptGroup
                {
                    Name = part.Substring(0, sep),
                    ScriptNames = new List<string>(
                        part.Substring(sep + 1).Split(',').Where(s => !string.IsNullOrEmpty(s)))
                });
            }
        }

        void SaveGroups()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _groups.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(_groups[i].Name).Append(':');
                sb.Append(string.Join(",", _groups[i].ScriptNames));
            }
            EditorPrefs.SetString(KEY_CC_GROUPS, sb.ToString());
        }

        void AddGroup()
        {
            // 첫 그룹 생성 시 모든 기존 스크립트 포함
            var scripts = _groups.Count == 0
                ? new List<string>(_mergedScripts.Select(s => s.Name))
                : new List<string>();

            _groups.Add(new ScriptGroup
            {
                Name = $"Group {_groups.Count + 1}",
                ScriptNames = scripts
            });
            _groupIdx = _groups.Count - 1;
            SaveGroups();
        }

        void RenameGroup(int idx, string newName)
        {
            if (idx < 0 || idx >= _groups.Count) return;

            // 빈 이름 입력 → 그룹 삭제
            if (string.IsNullOrWhiteSpace(newName))
            {
                _groups.RemoveAt(idx);
                if (_groupIdx >= _groups.Count) _groupIdx = Mathf.Max(0, _groups.Count - 1);
                SaveGroups();
                return;
            }

            var g = _groups[idx];
            g.Name = newName.Trim();
            _groups[idx] = g;
            SaveGroups();
        }

        void ShowScriptGroupMenu(string scriptName)
        {
            if (_groups.Count == 0) return;
            var menu = new GenericMenu();
            for (int i = 0; i < _groups.Count; i++)
            {
                var g = _groups[i];
                bool inGroup = g.ScriptNames.Contains(scriptName);
                int idx = i;
                menu.AddItem(new GUIContent(g.Name), inGroup, () =>
                {
                    var grp = _groups[idx];
                    if (grp.ScriptNames.Contains(scriptName)) grp.ScriptNames.Remove(scriptName);
                    else grp.ScriptNames.Add(scriptName);
                    _groups[idx] = grp;
                    SaveGroups();
                });
            }
            menu.ShowAsContext();
        }

        // ─── 삭제 ──────────────────────────────────

        void DeleteServerScript(string name)
        {
            _isLoading = true;
            _lastError = null;
            _lastSuccess = null;
            UGSCliRunner.RunAsync($"cc scripts delete {name}", result =>
            {
                if (result.Success) { _lastSuccess = $"'{name}' 삭제 완료"; FetchData(); }
                else { _isLoading = false; _lastError = $"삭제 실패: {result.Error}"; }
            });
        }

        void DeleteLocalScript(MergedScript script)
        {
            try
            {
                File.Delete(script.FilePath);
                string metaPath = script.FilePath + ".meta";
                if (File.Exists(metaPath)) File.Delete(metaPath);
                AssetDatabase.Refresh();
                ScanLocalScripts();
                BuildMergedList();
            }
            catch (Exception e) { _lastError = $"삭제 실패: {e.Message}"; }
        }

    }
}
#endif
