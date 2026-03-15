#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// Triggers 탭. 이벤트 → Cloud Code 연결 관리. 단일 .tr 파일.
    /// </summary>
    public class TriggersTab : UGSTabBase
    {
        public override string TabName => "Trigger";
        public override Color TabColor => new(0.90f, 0.65f, 0.35f);
        protected override string DashboardPath => "cloud-code/triggers";

        // ─── 데이터 ──────────────────────────────────
        List<TriggerEntry> _entries = new();
        string _triggerDir = "";
        string _triggerFilePath = "";

        // ─── UI 상태 ────────────────────────────────
        ResizableColumns _columns;
        const int COL_NAME = 0, COL_EVENT = 1, COL_SCRIPT = 2, COL_ACT = 3;

        bool _foldList = true;
        bool _foldCreate;

        // 새 트리거
        string _newName = "";
        string _newEvent = "";
        string _newScript = "";
        string _newFilter = "";

        // ─── 데이터 모델 ─────────────────────────────

        class TriggerEntry
        {
            public string Name = "";
            public string EventType = "";
            public string ActionUrn = "";  // urn:ugs:cloud-code:ScriptName
            public string Filter = "";
            public bool IsExpanded;
            public bool IsDirty;

            public string ScriptName => ActionUrn.StartsWith("urn:ugs:cloud-code:")
                ? ActionUrn.Substring(19) : ActionUrn;
        }

        // ─── 데이터 로드 ─────────────────────────────

        protected override void FetchData()
        {
            _isLoading = false;
            _notification = null;

            _columns ??= new ResizableColumns("UGS_TR", new ColumnDef[]
            {
                new ColumnDef("이름", 120f, resizable: true),
                new ColumnDef("이벤트", 0f),
                new ColumnDef("스크립트", 120f, resizable: true),
                new ColumnDef("", 26f),
            }, () => EditorWindow.GetWindow<UGSWindow>()?.Repaint());

            ResolveDir();
            ScanLocalFile();
            CacheScriptNames();
            _lastRefreshTime = DateTime.Now;
        }

        void ResolveDir()
        {
            foreach (var dir in Directory.GetDirectories(Application.dataPath, "UGS", SearchOption.AllDirectories))
            {
                if (dir.Contains("Library") || dir.Contains("Temp") || dir.Contains("PackageCache")) continue;
                string trDir = Path.Combine(dir, "Triggers");
                if (Directory.Exists(trDir) || File.Exists(Path.Combine(dir, "RemoteConfig.rc")))
                {
                    _triggerDir = Path.Combine(dir, "Triggers");
                    return;
                }
            }
            string root = Application.dataPath.Replace("/Assets", "");
            _triggerDir = Path.Combine(root, "Assets/UGS/Triggers");
        }

        // ─── 로컬 파일 파싱 ─────────────────────────

        void ScanLocalFile()
        {
            _entries.Clear();
            if (!Directory.Exists(_triggerDir)) return;

            var files = Directory.GetFiles(_triggerDir, "*.tr");
            if (files.Length == 0) return;

            _triggerFilePath = files[0];
            string json = File.ReadAllText(_triggerFilePath);

            // "Configs" 배열 파싱
            int arrStart = json.IndexOf('[');
            int arrEnd = arrStart >= 0 ? JsonHelper.FindBracket(json, arrStart) : -1;
            if (arrStart < 0 || arrEnd < 0) return;
            string arrBlock = json.Substring(arrStart, arrEnd - arrStart + 1);

            int sf = 0;
            while (true)
            {
                int os = arrBlock.IndexOf('{', sf); if (os < 0) break;
                int oe = JsonHelper.FindBrace(arrBlock, os); if (oe < 0) break;
                string obj = arrBlock.Substring(os, oe - os + 1);

                string name = JsonHelper.GetString(obj, "Name");
                if (string.IsNullOrEmpty(name)) { sf = oe + 1; continue; }

                _entries.Add(new TriggerEntry
                {
                    Name = name,
                    EventType = JsonHelper.GetString(obj, "EventType"),
                    ActionUrn = JsonHelper.GetString(obj, "ActionUrn"),
                    Filter = JsonHelper.GetString(obj, "Filter")
                });

                sf = oe + 1;
            }
        }

        // ─── 메인 UI ────────────────────────────────

        public override void OnDraw()
        {
            DrawMainToolbar();
            DrawNotifications();
            DrawLoading(_isLoading);
            if (_isLoading) return;

            GUILayout.Space(4);
            DrawList();
            GUILayout.Space(8);
            DrawCreateSection();

            if (!string.IsNullOrEmpty(_triggerDir))
                DrawEnvCopySection("triggers", _triggerDir, onComplete: () => FetchData());
        }

        // ─── 툴바 ──────────────────────────────────

        void DrawMainToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (DrawColorBtn("Refresh", COL_INFO, 22)) FetchData();

            bool hasDirty = _entries.Any(e => e.IsDirty);
            GUI.enabled = hasDirty || _entries.Count > 0;
            if (DrawColorBtn("Save", COL_WARN, 22)) SaveFile();
            GUI.enabled = true;

            if (DrawColorBtn("Deploy ↑", COL_SUCCESS, 22)) DeployTriggers();

            GUILayout.Space(8);
            EditorGUILayout.LabelField($"트리거: {_entries.Count}개",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED } }, GUILayout.Width(70));

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(DashboardPath) && DrawLinkButton("Dashboard"))
            {
                if (UGSConfig.IsConfigured)
                {
                    var pid = UGSCliRunner.GetProjectId();
                    var eid = UGSCliRunner.GetEnvironmentId();
                    if (!string.IsNullOrEmpty(pid))
                    {
                        string url = UGSConfig.GetDashboardUrl(pid, eid, DashboardPath);
                        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ─── 트리거 목록 ────────────────────────────

        void DrawList()
        {
            if (!DrawSectionFoldout(ref _foldList, $"Triggers ({_entries.Count})", TabColor)) return;
            BeginBody();

            if (_entries.Count == 0)
                EditorGUILayout.LabelField("트리거 없음", new GUIStyle(EditorStyles.centeredGreyMiniLabel));
            else
            {
                _columns.DrawHeader();
                for (int i = 0; i < _entries.Count; i++)
                    DrawRow(_entries[i], i);
            }

            EndBody();
        }

        void DrawRow(TriggerEntry entry, int index)
        {
            var bg = entry.IsDirty ? new Color(0.25f, 0.22f, 0.12f) : (index % 2 == 0 ? BG_CARD : BG_SECTION);
            EditorGUILayout.BeginVertical(GetBgStyle(bg));
            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));

            // 이름
            DrawCellLabel(entry.Name, _columns.GetWidth(COL_NAME));

            // 이벤트
            DrawCellLabel(entry.EventType, 0, COL_WARN);

            // 스크립트
            DrawCellLabel(entry.ScriptName, _columns.GetWidth(COL_SCRIPT), COL_INFO);

            // 펼침
            if (GUILayout.Button(entry.IsExpanded ? "▾" : "▸", EditorStyles.miniButton,
                GUILayout.Width(18), GUILayout.Height(16)))
                entry.IsExpanded = !entry.IsExpanded;

            EditorGUILayout.EndHorizontal();

            if (entry.IsExpanded)
                DrawInlineEdit(entry);

            EditorGUILayout.EndVertical();
        }

        // ─── 인라인 편집 ────────────────────────────

        void DrawInlineEdit(TriggerEntry entry)
        {
            // 이름
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("이름:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(50));
            string nn = EditorGUILayout.TextField(entry.Name);
            if (nn != entry.Name) { entry.Name = nn; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // 이벤트 타입
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("이벤트:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(50));
            string ne = EditorGUILayout.TextField(entry.EventType);
            if (ne != entry.EventType) { entry.EventType = ne; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            // 스크립트 (ActionUrn)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("스크립트:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(50));

            // Cloud Code 스크립트 목록에서 선택
            string currentScript = entry.ScriptName;
            string[] scripts = _cachedScriptNames;
            if (scripts.Length > 0)
            {
                int idx = Array.IndexOf(scripts, currentScript);
                if (idx < 0) idx = 0;
                int newIdx = EditorGUILayout.Popup(idx, scripts);
                if (newIdx != idx)
                {
                    entry.ActionUrn = $"urn:ugs:cloud-code:{scripts[newIdx]}";
                    entry.IsDirty = true;
                }
            }
            else
            {
                string nu = EditorGUILayout.TextField(currentScript);
                if (nu != currentScript)
                {
                    entry.ActionUrn = $"urn:ugs:cloud-code:{nu}";
                    entry.IsDirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 필터
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("필터:", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(50));
            string nf = EditorGUILayout.TextField(entry.Filter ?? "");
            if (nf != (entry.Filter ?? "")) { entry.Filter = nf; entry.IsDirty = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("  필터 예시: data['value'] > 5",
                new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_MUTED } });

            // 삭제
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("이 트리거 삭제", EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(14)))
            {
                if (EditorUtility.DisplayDialog("삭제", $"'{entry.Name}'를 삭제하시겠습니까?", "삭제", "취소"))
                {
                    _entries.Remove(entry);
                    SaveFile();
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // ─── 새 트리거 ──────────────────────────────

        void DrawCreateSection()
        {
            if (!DrawSectionFoldout(ref _foldCreate, "새 트리거", COL_WARN)) return;
            BeginBody();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("이름:", GUILayout.Width(50));
            _newName = EditorGUILayout.TextField(_newName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("이벤트:", GUILayout.Width(50));
            _newEvent = EditorGUILayout.TextField(_newEvent);
            EditorGUILayout.LabelField("← Scheduler EventName과 동일", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("스크립트:", GUILayout.Width(50));
            string[] scripts = _cachedScriptNames;
            if (scripts.Length > 0)
            {
                int idx = Array.IndexOf(scripts, _newScript);
                if (idx < 0) idx = 0;
                int newIdx = EditorGUILayout.Popup(idx, scripts);
                _newScript = scripts[newIdx];
            }
            else
                _newScript = EditorGUILayout.TextField(_newScript);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            EditorGUILayout.LabelField("필터:", GUILayout.Width(50));
            _newFilter = EditorGUILayout.TextField(_newFilter);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool isDup = _entries.Any(e => e.Name == _newName?.Trim());
            GUI.enabled = !string.IsNullOrWhiteSpace(_newName) && !string.IsNullOrWhiteSpace(_newEvent) && !isDup;
            if (GUILayout.Button("+ 추가", EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(18)))
                AddTrigger();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (isDup && !string.IsNullOrWhiteSpace(_newName))
                EditorGUILayout.LabelField("이미 존재하는 이름입니다",
                    new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = COL_ERROR } });

            EndBody();
        }

        void AddTrigger()
        {
            _entries.Add(new TriggerEntry
            {
                Name = _newName.Trim(),
                EventType = _newEvent.Trim(),
                ActionUrn = $"urn:ugs:cloud-code:{_newScript}",
                Filter = _newFilter.Trim(),
                IsDirty = true
            });

            _newName = "";
            _newEvent = "";
            _newFilter = "";
            SaveFile();
        }

        // ─── Cloud Code 스크립트 목록 ────────────────

        string[] _cachedScriptNames = Array.Empty<string>();

        void CacheScriptNames()
        {
            foreach (var dir in Directory.GetDirectories(Application.dataPath, "CloudCode", SearchOption.AllDirectories))
            {
                if (dir.Contains("Library") || dir.Contains("Temp") || dir.Contains("PackageCache")) continue;
                var jsFiles = Directory.GetFiles(dir, "*.js");
                if (jsFiles.Length > 0)
                {
                    _cachedScriptNames = jsFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
                    return;
                }
            }
            _cachedScriptNames = Array.Empty<string>();
        }

        // ─── 저장 ──────────────────────────────────

        void SaveFile()
        {
            if (!Directory.Exists(_triggerDir)) Directory.CreateDirectory(_triggerDir);
            if (string.IsNullOrEmpty(_triggerFilePath))
                _triggerFilePath = Path.Combine(_triggerDir, "Triggers.tr");

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/triggers.schema.json\",");
            sb.AppendLine("  \"Configs\": [");

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                string comma = i < _entries.Count - 1 ? "," : "";
                string filter = (e.Filter ?? "").Replace("\"", "\\\"");

                sb.AppendLine("    {");
                sb.AppendLine($"      \"Name\": \"{e.Name}\",");
                sb.AppendLine($"      \"EventType\": \"{e.EventType}\",");
                sb.AppendLine($"      \"ActionType\": \"cloud-code\",");
                sb.AppendLine($"      \"ActionUrn\": \"{e.ActionUrn}\",");
                sb.AppendLine($"      \"Filter\": \"{filter}\"");
                sb.AppendLine($"    }}{comma}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(_triggerFilePath, sb.ToString());
            foreach (var e in _entries) e.IsDirty = false;
            AssetDatabase.Refresh();

            ShowNotification("저장 완료", NotificationType.Success);
        }

        // ─── Deploy ─────────────────────────────────

        void DeployTriggers()
        {
            if (!Directory.Exists(_triggerDir)) { ShowNotification("Triggers 폴더 없음", NotificationType.Error); return; }
            _isLoading = true;
            _notification = null;

            UGSCliRunner.RunAsync($"deploy \"{_triggerDir.Replace('\\', '/')}\" -s triggers", result =>
            {
                _isLoading = false;
                if (result.Success)
                {
                    ShowNotification("Deploy 완료" + (!string.IsNullOrEmpty(result.Output) ? $"\n{result.Output}" : ""), NotificationType.Success);
                    FetchData();
                }
                else
                {
                    var sb = new StringBuilder($"Deploy 실패 (exit {result.ExitCode})");
                    if (!string.IsNullOrEmpty(result.Error)) sb.Append($"\n{result.Error}");
                    if (!string.IsNullOrEmpty(result.Output)) sb.Append($"\n{result.Output}");
                    ShowNotification(sb.ToString(), NotificationType.Error);
                }
            });
        }

    }
}
#endif
