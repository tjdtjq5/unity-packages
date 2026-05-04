#if UNITY_EDITOR
using Cysharp.Threading.Tasks;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 2/6 — Codemagic Personal Access Token 등록 + 검증.</summary>
    public sealed class Step2TokenStep : ISetupStep
    {
        public string Title => "API 토큰";
        public bool IsCompleted => _validated;
        public bool IsRequired => true;

        // 평문 token은 instance 필드에서만 보관, Debug.Log/직렬화 금지.
        string _token = "";
        bool _validated;
        bool _isValidating;

        public void OnEnter(SetupContext ctx)
        {
            // 이전 세션에서 토큰을 저장했으면 prefill — 입력 없이도 검증만 다시 가능.
            _token = SecretStore.CodemagicToken;
            _validated = false;

            // 이미 ApiClient 가용하면 자동 1회 검증해 사용자 입력 부담 줄임.
            if (!string.IsNullOrEmpty(_token) && ctx.Api != null)
                ValidateAsync(ctx).Forget();
        }

        public void OnDraw(SetupContext ctx)
        {
            EditorUI.DrawSubLabel("Step 2/6: API 토큰");
            EditorUI.DrawDescription(
                "Codemagic Personal Access Token을 등록합니다.\n" +
                "발급 위치: 좌측 사이드바에서 Personal account 선택 → Settings → Integrations → Codemagic API → Show");

            GUILayout.Space(8);

            EditorUI.BeginBody();
            _token = EditorUI.DrawPasswordField("Token", _token);

            if (!string.IsNullOrEmpty(_token))
                EditorUI.DrawDescription($"  현재 입력 길이: {_token.Length}자", EditorUI.COL_MUTED);

            GUILayout.Space(4);

            EditorUI.BeginRow();
            EditorUI.BeginDisabled(string.IsNullOrEmpty(_token) || _isValidating);
            if (EditorUI.DrawColorButton(_isValidating ? "검증 중..." : "저장 + 검증",
                EditorUI.COL_INFO))
            {
                ValidateAsync(ctx).Forget();
            }
            EditorUI.EndDisabled();

            if (EditorUI.DrawLinkButton("Codemagic 열기"))
                Application.OpenURL("https://codemagic.io/apps");
            EditorUI.EndRow();

            // 결과 표시 (성공만 인라인, 실패는 상단 토스트).
            if (_validated)
            {
                GUILayout.Space(4);
                EditorUI.DrawCellLabel("  ✓ 토큰 검증됨 — 다음 단계로 진행할 수 있습니다.",
                    0, EditorUI.COL_SUCCESS);
            }
            EditorUI.EndBody();

            GUILayout.Space(4);
            EditorUI.DrawDescription(
                "토큰은 EditorPrefs에 저장됩니다 (per-user, OS-level). git에 노출되지 않습니다.",
                EditorUI.COL_MUTED);
        }

        public void OnLeave(SetupContext ctx) { }

        async UniTask ValidateAsync(SetupContext ctx)
        {
            _isValidating = true;
            ctx.ShowNotification("Codemagic API 토큰 검증 중...", EditorUI.NotificationType.Info);

            // 입력값을 SecretStore에 저장 후 ApiClient 재생성.
            SecretStore.CodemagicToken = _token ?? "";
            ctx.RebuildApi();

            if (ctx.Api == null)
            {
                _validated = false;
                _isValidating = false;
                ctx.ShowNotification("토큰이 비어 있습니다.", EditorUI.NotificationType.Error);
                return;
            }

            try
            {
                var (ok, error) = await ctx.Api.ValidateTokenAsync();
                _validated = ok;
                if (ok)
                    ctx.ShowNotification("토큰 검증 OK", EditorUI.NotificationType.Success);
                else
                    ctx.ShowNotification($"토큰 검증 실패: {error ?? "알 수 없는 오류"}",
                        EditorUI.NotificationType.Error);
            }
            finally
            {
                _isValidating = false;
                EditorWindow.focusedWindow?.Repaint();
            }
        }
    }
}
#endif
