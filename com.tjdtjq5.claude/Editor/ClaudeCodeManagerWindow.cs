using System.Collections.Generic;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Claude
{
    public class ClaudeCodeManagerWindow : EditorWindow
    {
        // ── Claude 고유 색상 (EditorTabBase에 없는 것만) ──
        static readonly Color COL_PRIMARY = new(0.42f, 0.36f, 0.91f);
        static readonly Color COL_ROW_ALT = new(0.17f, 0.17f, 0.21f);

        // ── 테이블 컬럼 폭 ──
        const float COL_W_NAME   = 70f;
        const float COL_W_BRANCH = 70f;
        const float COL_W_STATUS = 50f;
        const float COL_W_BTN_OPEN   = 40f;
        const float COL_W_BTN_DEL    = 22f;
        const float COL_W_BTN_EXPAND = 22f;

        // ── 상태 ──
        List<WorktreeInfo> _worktrees = new();
        readonly HashSet<string> _deletingNames = new();
        string _highlightName;
        bool _isCreating;
        bool _isRefreshing;
        string _statusMessage;
        double _statusExpireTime;
        Vector2 _scrollPos;

        // ── GUIStyle 캐시 ──
        GUIStyle _titleStyle;
        GUIStyle _mutedCenterStyle;

        [MenuItem("Tools/Claude Code/Manager")]
        public static void Open()
        {
            var wnd = GetWindow<ClaudeCodeManagerWindow>("Claude Code Manager");
            wnd.minSize = new Vector2(420, 360);
        }

        void OnEnable()
        {
            RefreshWorktrees();
            EditorApplication.update += OnUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
        }

        void OnFocus() => RefreshWorktrees();

        void OnUpdate()
        {
            if (!string.IsNullOrEmpty(_statusMessage) &&
                EditorApplication.timeSinceStartup > _statusExpireTime)
            {
                _statusMessage = null;
                Repaint();
            }
        }

        void EnsureStyles()
        {
            _titleStyle ??= new GUIStyle(EditorStyles.boldLabel)
                { fontSize = 16, fixedHeight = 26f };
            _mutedCenterStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                { normal = { textColor = EditorTabBase.COL_MUTED } };
        }

        void OnGUI()
        {
            EnsureStyles();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawBanner();
            GUILayout.Space(6);
            DrawMainLaunchSection();
            GUILayout.Space(6);
            DrawWorktreeSection();
            DrawStatusMessage();

            EditorGUILayout.EndScrollView();
        }

        // ── 배너 ──

        void DrawBanner()
        {
            var rect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorTabBase.BG_HEADER);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), COL_PRIMARY);

            EditorGUI.LabelField(
                new Rect(rect.x + 8, rect.y, rect.width - 16, rect.height),
                "Claude Code Manager", _titleStyle);
        }

        // ── 메인 실행 ──

        void DrawMainLaunchSection()
        {
            if (EditorTabBase.DrawColorBtn("메인 Claude 실행", COL_PRIMARY, 32))
            {
                ClaudeCodeLauncher.LaunchMain();
                SetStatus("메인 Claude 실행됨");
            }
        }

        // ── 워크트리 통합 섹션 ──

        void DrawWorktreeSection()
        {
            EditorTabBase.DrawSectionHeader("워크트리", EditorTabBase.COL_WARN);

            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_SECTION));

            // 생성 버튼
            GUILayout.Space(2);
            EditorGUI.BeginDisabledGroup(_isCreating);
            if (EditorTabBase.DrawColorBtn(_isCreating ? "생성 중..." : "+ 새 워크트리", EditorTabBase.COL_WARN, 26))
            {
                _isCreating = true;
                _highlightName = null;
                ClaudeCodeLauncher.CreateWorktreeAsync(
                    info =>
                    {
                        _isCreating = false;
                        _highlightName = info.Name;
                        SetStatus($"{info.Name} 생성 완료");
                        RefreshWorktrees();
                        ClaudeToolbar.RefreshBadge();

                        if (ClaudeCodeSettings.AutoLaunch)
                            ClaudeCodeLauncher.LaunchClaudeAt(info.Path, $"Claude {info.Name}");

                        Focus();
                        ShowNotification(new GUIContent($"{info.Name} 생성 완료"));
                        Repaint();
                    },
                    error =>
                    {
                        _isCreating = false;
                        SetStatus($"생성 실패: {error}");
                        Focus();
                        ShowNotification(new GUIContent("워크트리 생성 실패"));
                        Repaint();
                    });
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(6);

            // 워크트리 목록 (기존 데이터가 있으면 새로고침 중에도 테이블 유지 — Layout 안정성)
            if (_worktrees.Count == 0)
            {
                EditorGUILayout.LabelField(
                    _isRefreshing ? "조회 중..." : "활성 워크트리 없음", _mutedCenterStyle);
            }
            else
            {
                DrawWorktreeTable();

                // 전체 정리
                GUILayout.Space(6);
                bool isAnyDeleting = _deletingNames.Count > 0;
                EditorGUI.BeginDisabledGroup(isAnyDeleting);
                if (EditorTabBase.DrawColorBtn(
                    isAnyDeleting ? "정리 중..." : $"전체 정리 ({_worktrees.Count}개)",
                    EditorTabBase.COL_ERROR))
                {
                    if (EditorUtility.DisplayDialog("전체 워크트리 정리",
                        $"{_worktrees.Count}개 워크트리를 모두 삭제하시겠습니까?\n(로컬 + 원격 브랜치도 함께 정리됩니다)",
                        "전체 삭제", "취소"))
                    {
                        foreach (var w in _worktrees) _deletingNames.Add(w.Name);
                        Repaint();
                        ClaudeCodeLauncher.RemoveAllWorktreesAsync(
                            () => { _deletingNames.Clear(); RefreshWorktrees(); ClaudeToolbar.RefreshBadge(); SetStatus("전체 정리 완료"); Repaint(); },
                            err => { _deletingNames.Clear(); SetStatus($"정리 실패: {err}"); Repaint(); });
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        void DrawWorktreeTable()
        {
            // 테이블 헤더
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(COL_W_BTN_EXPAND + 4);
            EditorTabBase.DrawHeaderLabel("이름", COL_W_NAME);
            EditorTabBase.DrawHeaderLabel("브랜치", COL_W_BRANCH);
            EditorTabBase.DrawHeaderLabel("상태", COL_W_STATUS);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 헤더 하단 구분선
            var lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, EditorTabBase.COL_MUTED);

            // 행
            for (int i = 0; i < _worktrees.Count; i++)
            {
                var wt = _worktrees[i];
                bool isExpanded = wt.Name == _highlightName;
                bool isAlt = i % 2 == 1;

                DrawWorktreeRow(wt, isAlt, isExpanded);

                if (isExpanded)
                    DrawWorktreeDetail(wt);
            }
        }

        void DrawWorktreeRow(WorktreeInfo wt, bool isAlt, bool isExpanded)
        {
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));

            // 배경
            if (isExpanded)
            {
                EditorGUI.DrawRect(rowRect, new Color(COL_PRIMARY.r, COL_PRIMARY.g, COL_PRIMARY.b, 0.12f));
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3, rowRect.height), COL_PRIMARY);
            }
            else if (isAlt)
            {
                EditorGUI.DrawRect(rowRect, COL_ROW_ALT);
            }

            // 펼치기/접기 토글 (제일 왼쪽)
            var toggleLabel = isExpanded ? "\u25B2" : "\u25BC";
            if (GUILayout.Button(toggleLabel, EditorStyles.miniButton, GUILayout.Width(COL_W_BTN_EXPAND)))
                _highlightName = isExpanded ? null : wt.Name;

            // 이름
            EditorTabBase.DrawCellLabel(wt.Name, COL_W_NAME, isExpanded ? COL_PRIMARY : (Color?)null);

            // 브랜치
            EditorTabBase.DrawCellLabel(wt.Branch, COL_W_BRANCH);

            // 상태
            bool isRowDeleting = _deletingNames.Contains(wt.Name);
            var statusText = isRowDeleting ? "삭제 중" : wt.IsDirty ? "dirty" : "clean";
            var statusColor = isRowDeleting ? EditorTabBase.COL_ERROR
                : wt.IsDirty ? EditorTabBase.COL_WARN : EditorTabBase.COL_SUCCESS;
            EditorTabBase.DrawCellLabel(statusText, COL_W_STATUS, statusColor);

            GUILayout.FlexibleSpace();

            bool isDeleting = _deletingNames.Contains(wt.Name);

            EditorGUI.BeginDisabledGroup(isDeleting);

            // 열기 버튼
            if (GUILayout.Button("열기", EditorStyles.miniButton, GUILayout.Width(COL_W_BTN_OPEN)))
            {
                ClaudeCodeLauncher.LaunchClaudeAt(wt.Path, $"Claude {wt.Name}");
                SetStatus($"{wt.Name}에서 Claude 실행됨");
            }

            // 삭제 버튼
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = EditorTabBase.COL_ERROR;
            if (GUILayout.Button(isDeleting ? "..." : "x", EditorStyles.miniButton, GUILayout.Width(COL_W_BTN_DEL)))
            {
                if (EditorUtility.DisplayDialog("워크트리 삭제",
                    $"{wt.Name} ({wt.Path})를 삭제하시겠습니까?\n(로컬 + 원격 브랜치도 함께 정리됩니다)",
                    "삭제", "취소"))
                {
                    if (_highlightName == wt.Name) _highlightName = null;
                    _deletingNames.Add(wt.Name);
                    ClaudeCodeLauncher.RemoveWorktreeAsync(wt,
                        () => { _deletingNames.Remove(wt.Name); RefreshWorktrees(); ClaudeToolbar.RefreshBadge(); SetStatus($"{wt.Name} 삭제됨"); Repaint(); },
                        err => { _deletingNames.Remove(wt.Name); SetStatus($"삭제 실패: {err}"); Repaint(); });
                }
            }
            GUI.backgroundColor = prevBg;

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        void DrawWorktreeDetail(WorktreeInfo wt)
        {
            EditorGUILayout.BeginVertical(EditorTabBase.GetBgStyle(EditorTabBase.BG_CARD));

            EditorGUILayout.LabelField($"경로: {wt.Path}", EditorStyles.miniLabel);

            var cmd = ClaudeCodeLauncher.BuildClaudeCommand();
            EditorGUILayout.LabelField($"> {cmd}", EditorStyles.miniLabel);

            GUILayout.Space(2);
            if (GUILayout.Button("클립보드 복사", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                EditorGUIUtility.systemCopyBuffer = $"cd \"{wt.Path}\" && {cmd}";
                SetStatus("클립보드에 복사됨");
            }

            EditorGUILayout.EndVertical();
        }

        // ── 상태 메시지 ──

        void DrawStatusMessage()
        {
            if (string.IsNullOrEmpty(_statusMessage)) return;
            if (EditorApplication.timeSinceStartup > _statusExpireTime)
            {
                _statusMessage = null;
                return;
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField(_statusMessage, _mutedCenterStyle);
        }

        // ── 헬퍼 ──

        void RefreshWorktrees()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            ClaudeCodeLauncher.GetActiveWorktreesAsync(list =>
            {
                _worktrees = list ?? new List<WorktreeInfo>();
                _isRefreshing = false;
                Repaint();
            });
        }

        void SetStatus(string msg, float duration = 5f)
        {
            _statusMessage = msg;
            _statusExpireTime = EditorApplication.timeSinceStartup + duration;
        }
    }
}
