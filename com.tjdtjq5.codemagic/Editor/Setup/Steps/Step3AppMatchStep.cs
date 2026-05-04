#if UNITY_EDITOR
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Tjdtjq5.Codemagic.Editor.Codemagic;
using Tjdtjq5.Codemagic.Editor.Git;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 3/6 — Codemagic 앱과 git remote 매칭. ListAppsAsync로 자동 + 수동 선택.</summary>
    public sealed class Step3AppMatchStep : ISetupStep
    {
        public string Title => "앱 매칭";
        public bool IsCompleted =>
            !string.IsNullOrEmpty(CodemagicProjectSettings.Instance.CodemagicAppId);
        public bool IsRequired => true;

        List<CodemagicAppDto> _apps = new();
        bool _isLoading;
        int _selectedIdx = -1;
        string _gitHubRepo;

        public void OnEnter(SetupContext ctx)
        {
            _gitHubRepo = GitHelpers.GetGitHubRepo();
            _selectedIdx = -1;
            LoadAppsAsync(ctx).Forget();
        }

        public void OnDraw(SetupContext ctx)
        {
            EditorUI.DrawSubLabel("Step 3/6: 앱 매칭");
            EditorUI.DrawDescription(
                "현재 git remote와 연결된 Codemagic 앱을 찾습니다.\n" +
                "Codemagic UI에서 앱을 미리 추가해 두어야 합니다.");

            GUILayout.Space(8);

            // git remote 표시.
            EditorUI.BeginBody();
            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("Git remote", 100, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(
                string.IsNullOrEmpty(_gitHubRepo) ? "(없음)" : _gitHubRepo,
                0,
                string.IsNullOrEmpty(_gitHubRepo) ? EditorUI.COL_ERROR : EditorUI.COL_INFO);
            EditorUI.EndRow();
            EditorUI.EndBody();

            GUILayout.Space(4);

            // 앱 목록.
            EditorUI.BeginBody();
            if (_isLoading)
            {
                EditorUI.DrawDescription("Codemagic 앱 목록 로딩 중...", EditorUI.COL_MUTED);
            }
            else if (_apps == null || _apps.Count == 0)
            {
                EditorUI.DrawCellLabel("  (등록된 Codemagic 앱이 없습니다)", 0, EditorUI.COL_WARN);
                GUILayout.Space(4);
                if (EditorUI.DrawLinkButton("Codemagic UI에서 앱 추가하기"))
                    Application.OpenURL("https://codemagic.io/apps");
            }
            else
            {
                // 옵션 라벨 — "AppName (owner/repo)" 또는 단순 AppName.
                var labels = new string[_apps.Count];
                for (int i = 0; i < _apps.Count; i++)
                {
                    var a = _apps[i];
                    labels[i] = string.IsNullOrEmpty(a.RepoUrl)
                        ? a.AppName
                        : $"{a.AppName}  ({a.RepoUrl})";
                }

                // 자동 매칭 — Settings에 이미 ID가 있거나 git remote와 일치하는 첫 항목.
                if (_selectedIdx < 0)
                    _selectedIdx = AutoMatchIndex(_apps, ctx);

                _selectedIdx = EditorUI.DrawPopup("Codemagic 앱",
                    Mathf.Clamp(_selectedIdx, 0, _apps.Count - 1), labels);

                if (_selectedIdx >= 0 && _selectedIdx < _apps.Count)
                {
                    var picked = _apps[_selectedIdx];

                    GUILayout.Space(4);
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel("App ID", 100, EditorUI.COL_MUTED);
                    EditorUI.DrawCellLabel(picked.AppId ?? "", 0, EditorUI.COL_INFO);
                    EditorUI.EndRow();
                    EditorUI.BeginRow();
                    EditorUI.DrawCellLabel("Repo URL", 100, EditorUI.COL_MUTED);
                    EditorUI.DrawCellLabel(picked.RepoUrl ?? "(none)", 0, EditorUI.COL_MUTED);
                    EditorUI.EndRow();

                    GUILayout.Space(4);
                    if (EditorUI.DrawColorButton("이 앱으로 설정", EditorUI.COL_SUCCESS))
                    {
                        ctx.Settings.CodemagicAppId = picked.AppId ?? "";
                        ctx.Settings.CodemagicAppName = picked.AppName ?? "";
                        ctx.Settings.CodemagicAppRepoUrl = picked.RepoUrl ?? "";
                        ctx.Settings.Save();
                        ctx.ShowNotification($"Codemagic 앱 설정됨: {picked.AppName}",
                            EditorUI.NotificationType.Success);
                    }
                }
            }
            EditorUI.EndBody();

            GUILayout.Space(4);

            // 액션 행.
            EditorUI.BeginRow();
            EditorUI.BeginDisabled(_isLoading);
            if (EditorUI.DrawColorButton("다시 가져오기", EditorUI.COL_INFO))
                LoadAppsAsync(ctx).Forget();
            EditorUI.EndDisabled();
            if (EditorUI.DrawLinkButton("Codemagic Apps 페이지"))
                Application.OpenURL("https://codemagic.io/apps");
            EditorUI.EndRow();

            // 현재 설정된 App.
            var curId = ctx.Settings.CodemagicAppId;
            if (!string.IsNullOrEmpty(curId))
            {
                GUILayout.Space(4);
                EditorUI.DrawCellLabel(
                    $"  ✓ 현재 설정: {ctx.Settings.CodemagicAppName} ({curId})",
                    0, EditorUI.COL_SUCCESS);
            }
        }

        public void OnLeave(SetupContext ctx) { }

        // ── 내부 ────────────────────────────────────────────────────────────

        static int AutoMatchIndex(List<CodemagicAppDto> apps, SetupContext ctx)
        {
            if (apps == null || apps.Count == 0) return -1;

            // 1) 이미 Settings에 AppId 있으면 그 index.
            var curId = ctx.Settings.CodemagicAppId;
            if (!string.IsNullOrEmpty(curId))
            {
                for (int i = 0; i < apps.Count; i++)
                    if (apps[i].AppId == curId) return i;
            }

            // 2) git remote URL이 RepoUrl에 포함된 첫 앱.
            var ghRepo = GitHelpers.GetGitHubRepo();
            if (!string.IsNullOrEmpty(ghRepo))
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    var url = apps[i].RepoUrl;
                    if (!string.IsNullOrEmpty(url) && url.Contains(ghRepo))
                        return i;
                }
            }

            return 0;
        }

        async UniTask LoadAppsAsync(SetupContext ctx)
        {
            if (ctx.Api == null)
            {
                ctx.ShowNotification(
                    "API 토큰이 등록되지 않았습니다. 이전 단계에서 토큰을 등록하세요.",
                    EditorUI.NotificationType.Error);
                _apps = new List<CodemagicAppDto>();
                return;
            }

            _isLoading = true;
            ctx.ShowNotification("Codemagic 앱 목록을 가져오는 중...", EditorUI.NotificationType.Info);

            try
            {
                _apps = await ctx.Api.ListAppsAsync();
                if (_apps == null) _apps = new List<CodemagicAppDto>();

                if (_apps.Count == 0)
                {
                    ctx.ShowNotification(
                        "Codemagic에 등록된 앱이 없습니다. UI에서 먼저 앱을 추가하세요.",
                        EditorUI.NotificationType.Info);
                }
                else
                {
                    _selectedIdx = AutoMatchIndex(_apps, ctx);
                    ctx.ShowNotification($"Codemagic 앱 {_apps.Count}개 로드됨.",
                        EditorUI.NotificationType.Success);
                }
            }
            catch (System.Exception ex)
            {
                ctx.ShowNotification($"앱 목록 로드 실패: {ex.Message}",
                    EditorUI.NotificationType.Error);
            }
            finally
            {
                _isLoading = false;
                EditorWindow.focusedWindow?.Repaint();
            }
        }
    }
}
#endif
