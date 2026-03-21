#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Version 탭: 현재 버전 표시 + 태그 목록</summary>
    public class VersionTab
    {
        bool _showTags;

        public void OnDraw()
        {
            // ── 현재 버전 ──
            EditorUI.DrawSectionHeader("현재 버전", BuildAutomationWindow.COL_PRIMARY);

            EditorUI.BeginBody();

            string version = GitVersionResolver.GetVersion();
            string detailed = GitVersionResolver.GetDetailedVersion();
            int buildCode = GitVersionResolver.ComputeBuildCode(version);

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("Git Tag 버전", 120, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(version, 0, Color.white);
            EditorUI.EndRow();

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("상세 버전", 120, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(detailed, 0, EditorUI.COL_INFO);
            EditorUI.EndRow();

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("빌드 코드", 120, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(buildCode.ToString(), 0, Color.white);
            EditorUI.EndRow();

            EditorUI.EndBody();

            GUILayout.Space(4);

            // ── PlayerSettings 현재 값 ──
            EditorUI.DrawSectionHeader("PlayerSettings (현재)", EditorUI.COL_MUTED);
            EditorUI.BeginBody();

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("bundleVersion", 160, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(PlayerSettings.bundleVersion, 0, Color.white);
            EditorUI.EndRow();

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("Android versionCode", 160, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(PlayerSettings.Android.bundleVersionCode.ToString(), 0, Color.white);
            EditorUI.EndRow();

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("iOS buildNumber", 160, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(PlayerSettings.iOS.buildNumber, 0, Color.white);
            EditorUI.EndRow();

            EditorUI.EndBody();

            GUILayout.Space(4);

            // ── 버전 동기화 ──
            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "빌드 시 IPreprocessBuildWithReport가 자동으로\n" +
                "git tag 버전을 PlayerSettings에 적용합니다.",
                EditorUI.COL_MUTED);

            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("지금 PlayerSettings에 적용", EditorUI.COL_INFO))
            {
                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = buildCode;
                PlayerSettings.iOS.buildNumber = buildCode.ToString();
                Debug.Log($"[BuildAutomation] PlayerSettings 업데이트: {version} ({buildCode})");
            }
            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── 최근 태그 ──
            if (EditorUI.DrawSectionFoldout(ref _showTags, "최근 Git Tags",
                BuildAutomationWindow.COL_PRIMARY))
            {
                var tags = GitVersionResolver.GetRecentTags(10);
                EditorUI.BeginBody();
                if (tags.Length == 0)
                {
                    EditorUI.DrawDescription("태그가 없습니다. 첫 번째 태그를 생성하세요:\n  git tag v0.1.0",
                        EditorUI.COL_MUTED);
                }
                else
                {
                    foreach (var tag in tags)
                        EditorUI.DrawCellLabel($"  {tag}", 0, EditorUI.COL_INFO);
                }
                EditorUI.EndBody();
            }

            GUILayout.Space(8);

            // ── 버전 관리 가이드 ──
            EditorUI.BeginBody();
            EditorUI.DrawSectionHeader("버전 관리 가이드", EditorUI.COL_MUTED);
            EditorUI.DrawDescription(
                "태그 형식: v{MAJOR}.{MINOR}.{PATCH}\n\n" +
                "새 버전 릴리스:\n" +
                "  git tag v0.2.0\n" +
                "  git push origin v0.2.0\n\n" +
                "빌드 코드 공식:\n" +
                "  MAJOR × 10000 + MINOR × 100 + PATCH\n" +
                "  예: v1.2.3 → 10203",
                EditorUI.COL_MUTED);
            EditorUI.EndBody();
        }
    }
}
#endif
