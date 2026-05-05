#if UNITY_EDITOR
using System;
using System.IO;
using Tjdtjq5.Codemagic.Editor.Dashboard;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 6/6 — 셋업 요약 + Build Dialog 진입 안내.</summary>
    public sealed class Step6CompleteStep : ISetupStep
    {
        public string Title => "완료";
        public bool IsCompleted => true;
        public bool IsRequired => false;

        public void OnEnter(SetupContext ctx) { }

        public void OnDraw(SetupContext ctx)
        {
            EditorUI.DrawSubLabel("Step 6/6: 셋업 완료");
            EditorUI.DrawDescription(
                "Codemagic 셋업이 마무리되었습니다.\n" +
                "이제 Build Dialog 에서 빌드를 시작할 수 있습니다.",
                EditorUI.COL_SUCCESS);

            GUILayout.Space(8);

            // ── 요약 ──
            EditorUI.DrawSectionHeader("셋업 요약", EditorUI.COL_INFO);
            EditorUI.BeginBody();

            DrawSummaryRow("API 토큰",
                !string.IsNullOrEmpty(SecretStore.CodemagicToken),
                "등록됨", "(미등록)");

            var appName = ctx.Settings.CodemagicAppName;
            var appId = ctx.Settings.CodemagicAppId;
            DrawSummaryRow("Codemagic 앱",
                !string.IsNullOrEmpty(appId),
                string.IsNullOrEmpty(appName) ? appId : appName,
                "(미선택)");

            var stopDate = ctx.State.LicenseStopDate;
            DrawSummaryRow("Unity 라이선스",
                !string.IsNullOrEmpty(stopDate),
                FormatLicenseLabel(stopDate),
                "(미동기)");

            DrawSummaryRow("  └ Codemagic 등록",
                ctx.State.LicenseEnvRegistered,
                "✓ unity_credentials 3개 등록 완료",
                "(미등록 — Step 4에서 셀프 체크)");

            var keystorePath = ctx.State.KeystorePath;
            var keyAlias = ctx.Settings.KeyAlias;
            bool keystoreSet = !string.IsNullOrEmpty(keystorePath) && !string.IsNullOrEmpty(keyAlias);
            DrawSummaryRow("Android keystore",
                keystoreSet,
                keystoreSet ? $"{Path.GetFileName(keystorePath)} ({keyAlias})" : "(건너뜀)",
                "(건너뜀)");

            DrawSummaryRow("  └ Codemagic 등록",
                ctx.State.KeystoreEnvRegistered,
                "✓ android_keystore 4개 등록 완료",
                "(미등록 또는 건너뜀)");

            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── 다음 단계 ──
            EditorUI.DrawSectionHeader("다음 단계", EditorUI.COL_INFO);
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "ㆍ Build Dialog 를 열고 빌드 옵션을 선택해 첫 빌드를 시작하세요.\n" +
                "ㆍ 셋업 정보는 EditorPrefs(시크릿) + Library/codemagic-setup.json(메타) 에 저장됩니다.\n" +
                "ㆍ Build Dialog는 [Build → Codemagic → Build Dialog] 메뉴에서도 열 수 있습니다.",
                EditorUI.COL_MUTED);
            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── 액션 ──
            EditorUI.BeginRow();

            if (EditorUI.DrawColorButton("Build Dialog 열기", EditorUI.COL_SUCCESS, 28))
                OpenBuildDialog();

            if (EditorUI.DrawColorButton("Setup 마치기", EditorUI.COL_MUTED, 28))
            {
                var win = EditorWindow.focusedWindow;
                if (win != null) win.Close();
            }

            EditorUI.EndRow();
        }

        public void OnLeave(SetupContext ctx) { }

        // ── 헬퍼 ───────────────────────────────────────────────────────────

        static void DrawSummaryRow(string label, bool ok, string okText, string failText)
        {
            EditorUI.BeginRow();
            var mark = ok ? "✓" : "·";
            var color = ok ? EditorUI.COL_SUCCESS : EditorUI.COL_MUTED;
            EditorUI.DrawCellLabel($"  {mark} {label}", 180, color);
            EditorUI.DrawCellLabel(ok ? okText : failText, 0,
                ok ? EditorUI.COL_INFO : EditorUI.COL_MUTED);
            EditorUI.EndRow();
        }

        static string FormatLicenseLabel(string stopDate)
        {
            if (string.IsNullOrEmpty(stopDate)) return "(미동기)";
            if (DateTime.TryParse(stopDate, out var d))
            {
                var diff = (int)(d.Date - DateTime.UtcNow.Date).TotalDays;
                if (diff < 0) return $"{stopDate} (만료됨)";
                return $"{stopDate} (D-{diff})";
            }
            return stopDate;
        }

        /// <summary>Build Dialog 열기. 직접 호출 → 실패 시 메뉴 호출 fallback.</summary>
        static void OpenBuildDialog()
        {
            try
            {
                BuildDialog.Open();
                return;
            }
            catch (Exception)
            {
                // 동일 메뉴 등록이 있으면 fallback으로 동작.
                if (EditorApplication.ExecuteMenuItem("Tjdtjq/Codemagic/Build Dialog"))
                    return;
            }

            EditorUtility.DisplayDialog("Build Dialog",
                "Build Dialog를 열 수 없습니다.\n" +
                "[Build → Codemagic → Build Dialog] 메뉴를 직접 사용하세요.",
                "확인");
        }
    }
}
#endif
