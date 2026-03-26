using System.Linq;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>GCP 설정 UI. Setup Step 2와 Settings에서 공용.</summary>
    public static class GcpSetupUI
    {
        enum Phase { NoCli, NotLoggedIn, NoProject, NoApi, Complete }

        static Phase GetPhase(PrerequisiteChecker.ToolStatus gcloud, SupaRunSettings s)
        {
            if (!gcloud.Installed) return Phase.NoCli;
            if (!gcloud.LoggedIn) return Phase.NotLoggedIn;
            if (string.IsNullOrEmpty(s.gcpProjectId)) return Phase.NoProject;
            if (!s.gcpCloudRunApiEnabled || string.IsNullOrEmpty(s.gcpServiceAccountEmail))
                return Phase.NoApi;
            return Phase.Complete;
        }

        public static void Draw(SupaRunDashboard dashboard, SupaRunSettings settings)
        {
            var gcloud = PrerequisiteChecker.CheckGcloud();
            var phase = GetPhase(gcloud, settings);

            // 완료된 단계 요약 (항상)
            if (phase > Phase.NoCli)
                EditorUI.DrawCellLabel($"  \u2713 gcloud ({gcloud.Version})", 0, EditorUI.COL_SUCCESS);
            if (phase > Phase.NotLoggedIn)
                EditorUI.DrawCellLabel($"  \u2713 {gcloud.Account}", 0, EditorUI.COL_SUCCESS);
            if (phase > Phase.NoProject)
                EditorUI.DrawCellLabel($"  \u2713 {settings.gcpProjectId}", 0, EditorUI.COL_SUCCESS);
            if (phase == Phase.Complete)
            {
                EditorUI.DrawCellLabel("  \u2713 API \u2713 SA", 0, EditorUI.COL_SUCCESS);
                GUILayout.Space(2);

                // 요약
                EditorUI.DrawCellLabel(
                    $"  {settings.gcpRegion} | {settings.gcpServiceName} | " +
                    (settings.gcpMinInstances == 0 ? "무료" : "항상 켜짐"),
                    0, EditorUI.COL_MUTED);

                if (EditorUI.DrawLinkButton("Cloud Run 콘솔"))
                    Application.OpenURL("https://console.cloud.google.com/run");
                return;
            }

            GUILayout.Space(4);

            // 현재 단계만 표시
            switch (phase)
            {
                case Phase.NoCli:
                    DrawCliInstall();
                    break;
                case Phase.NotLoggedIn:
                    DrawLogin();
                    break;
                case Phase.NoProject:
                    DrawProjectSelector(settings);
                    break;
                case Phase.NoApi:
                    DrawConfigAndAutoSetup(dashboard, settings, gcloud);
                    break;
            }
        }

        /// <summary>Cloud Run 서비스 이름 규칙: 소문자 + 하이픈만, 63자 이내</summary>
        static string SanitizeServiceName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var result = new System.Text.StringBuilder();
            foreach (var c in input.ToLower())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')
                    result.Append(c);
                else if (c == ' ' || c == '_')
                    result.Append('-');
            }
            var s = result.ToString().Trim('-');
            if (s.Length > 63) s = s.Substring(0, 63).TrimEnd('-');
            return s;
        }

        static void DrawCliInstall()
        {
            EditorUI.DrawDescription("gcloud CLI를 설치하세요.\n서버 배포에 필요한 도구입니다.");
            GUILayout.Space(4);
            if (EditorUI.DrawLinkButton("gcloud CLI 설치하기", SupaRunDashboard.COL_GCP))
                Application.OpenURL("https://cloud.google.com/sdk/docs/install");
        }

        static void DrawLogin()
        {
            EditorUI.DrawDescription("Google 계정으로 로그인하세요.");
            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("로그인", SupaRunDashboard.COL_GCP, 28))
                PrerequisiteChecker.RunGcloudLogin();
        }

        static void DrawProjectSelector(SupaRunSettings settings)
        {
            EditorUI.DrawDescription("프로젝트를 선택하세요.\n게임 1개당 프로젝트 1개 추천합니다.");
            GUILayout.Space(4);

            var projects = PrerequisiteChecker.GetGcpProjects();

            if (projects.Length > 0)
            {
                // 드롭다운
                var labels = projects.Select(p =>
                    string.IsNullOrEmpty(p.name) ? p.id : $"{p.id} ({p.name})").ToArray();
                var fullLabels = labels.Append("+ 새 프로젝트 만들기").ToArray();

                var currentIdx = -1;
                for (int i = 0; i < projects.Length; i++)
                {
                    if (projects[i].id == settings.gcpProjectId)
                    { currentIdx = i; break; }
                }
                if (currentIdx < 0) currentIdx = 0;

                var newIdx = EditorUI.DrawPopup("Project", currentIdx, fullLabels);

                if (newIdx < projects.Length)
                {
                    if (projects[newIdx].id != settings.gcpProjectId)
                    {
                        settings.gcpProjectId = projects[newIdx].id;
                        PrerequisiteChecker.SetGcloudProject(settings.gcpProjectId);
                        settings.Save();
                    }
                }
                else
                {
                    // 새 프로젝트 만들기
                    Application.OpenURL("https://console.cloud.google.com/projectcreate");
                }
            }
            else
            {
                // 프로젝트 목록 못 가져옴 → 수동 입력
                using (var so = new SerializedObject(settings))
                {
                    so.Update();
                    EditorGUILayout.PropertyField(so.FindProperty("gcpProjectId"),
                        new GUIContent("Project ID", "GCP 콘솔 상단에서 확인"));
                    so.ApplyModifiedProperties();
                }
            }

            GUILayout.Space(2);
            if (EditorUI.DrawLinkButton("새 프로젝트 만들기"))
                Application.OpenURL("https://console.cloud.google.com/projectcreate");
        }

        static void DrawConfigAndAutoSetup(SupaRunDashboard dashboard,
            SupaRunSettings settings, PrerequisiteChecker.ToolStatus gcloud)
        {
            using (var so = new SerializedObject(settings))
            {
                so.Update();

                // Region 드롭다운
                var regions = new[] {
                    "asia-northeast3", "asia-northeast1", "asia-east1", "asia-southeast1",
                    "us-central1", "us-east1", "europe-west1"
                };
                var regionLabels = new[] {
                    "asia-northeast3 (서울) *", "asia-northeast1 (도쿄)", "asia-east1 (대만)",
                    "asia-southeast1 (싱가포르)", "us-central1 (아이오와)", "us-east1 (버지니아)",
                    "europe-west1 (벨기에)"
                };
                var regionProp = so.FindProperty("gcpRegion");
                var rIdx = System.Array.IndexOf(regions, regionProp.stringValue);
                if (rIdx < 0) rIdx = 0;
                var newRIdx = EditorGUILayout.Popup(
                    new GUIContent("Region", "서버 위치 — 가까울수록 빠름"), rIdx, regionLabels);
                if (newRIdx != rIdx) regionProp.stringValue = regions[newRIdx];

                GUILayout.Space(2);

                // Service Name (자동 소문자 + 검증)
                var svcProp = so.FindProperty("gcpServiceName");
                if (string.IsNullOrEmpty(svcProp.stringValue) && !string.IsNullOrEmpty(settings.githubRepoName))
                    svcProp.stringValue = SanitizeServiceName(settings.githubRepoName);

                var newSvcName = EditorGUILayout.TextField(
                    new GUIContent("Service Name", "Cloud Run 서비스 이름 (URL에 포함)"),
                    svcProp.stringValue);
                var sanitized = SanitizeServiceName(newSvcName);
                if (sanitized != svcProp.stringValue)
                    svcProp.stringValue = sanitized;

                GUILayout.Space(2);

                // Min Instances
                var minOptions = new[] { "0 — 무료 (2~5초 대기)", "1 — 항상 켜짐 (월 ~5만원)" };
                var minProp = so.FindProperty("gcpMinInstances");
                var minVal = Mathf.Clamp(minProp.intValue, 0, 1);
                var newMin = EditorGUILayout.Popup(
                    new GUIContent("Min Instances", "서버 항상 켜둘지 여부"), minVal, minOptions);
                if (newMin != minVal) minProp.intValue = newMin;

                so.ApplyModifiedProperties();
            }

            GUILayout.Space(8);

            // 자동 설정 버튼
            EditorUI.DrawDescription(
                "아래 버튼으로 한 번에 처리합니다:\n" +
                "  \u2022 Cloud Run API 활성화\n" +
                "  \u2022 Service Account 생성 + 권한 부여\n" +
                "  \u2022 GitHub Secret 등록");

            GUILayout.Space(4);

            bool canAutoSetup = settings.IsGitHubConfigured;
            if (!canAutoSetup)
            {
                EditorUI.DrawDescription("GitHub 설정을 먼저 완료하세요.", EditorUI.COL_WARN);
            }

            using (new EditorGUI.DisabledGroupScope(!canAutoSetup))
            {
                if (EditorUI.DrawColorButton("자동 설정 시작", SupaRunDashboard.COL_GCP, 32))
                {
                    var gh = PrerequisiteChecker.CheckGh();
                    var repo = $"{gh.Account}/{settings.githubRepoName}";
                    var (ok, email, err) = PrerequisiteChecker.AutoSetupCloudRun(
                        settings.gcpProjectId, settings.gcpRegion, settings.gcpServiceName, repo);
                    if (ok)
                    {
                        settings.gcpCloudRunApiEnabled = true;
                        settings.gcpServiceAccountEmail = email;
                        settings.Save();
                        dashboard.ShowNotification("GCP 자동 설정 완료!",
                            EditorUI.NotificationType.Success);
                    }
                    else
                    {
                        // 결제 미활성 에러 감지 → 바로 링크 열기
                        if (err != null && err.Contains("결제"))
                            Application.OpenURL($"https://console.cloud.google.com/billing/linkedaccount?project={settings.gcpProjectId}");
                        dashboard.ShowNotification(err, EditorUI.NotificationType.Error);
                    }
                }
            }
        }

    }
}
