using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class CostMenu
    {
        [MenuItem("Tools/SupaRun/Cost/Supabase")]
        static void OpenSupabase()
        {
            var s = SupaRunSettings.Instance;
            var id = s.SupabaseProjectId;
            if (string.IsNullOrEmpty(id))
            {
                EditorUtility.DisplayDialog("SupaRun", "Supabase URL이 설정되지 않았습니다.", "확인");
                return;
            }
            Application.OpenURL($"https://supabase.com/dashboard/project/{id}/settings/billing/usage");
        }

        [MenuItem("Tools/SupaRun/Cost/Google Cloud")]
        static void OpenGoogleCloud()
        {
            var s = SupaRunSettings.Instance;
            if (string.IsNullOrEmpty(s.gcpProjectId))
            {
                EditorUtility.DisplayDialog("SupaRun", "GCP Project ID가 설정되지 않았습니다.", "확인");
                return;
            }
            Application.OpenURL($"https://console.cloud.google.com/billing?project={s.gcpProjectId}");
        }

        [MenuItem("Tools/SupaRun/Cost/GitHub Actions")]
        static void OpenGitHubActions()
        {
            var gh = PrerequisiteChecker.CheckGh();
            var s = SupaRunSettings.Instance;
            if (!gh.LoggedIn || string.IsNullOrEmpty(s.githubRepoName))
            {
                EditorUtility.DisplayDialog("SupaRun", "GitHub 설정이 필요합니다.", "확인");
                return;
            }
            Application.OpenURL($"https://github.com/{gh.Account}/{s.githubRepoName}/settings/billing");
        }
    }
}
