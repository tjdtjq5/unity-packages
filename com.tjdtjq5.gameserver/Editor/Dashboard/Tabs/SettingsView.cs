using System;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class SettingsView
    {
        readonly GameServerDashboard _dashboard;
        Vector2 _scrollPos;
        bool _foldSupabase = true;
        bool _foldDeploy = true;
        bool _foldLog = true;

        public SettingsView(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            DrawSupabase(settings, so);
            GUILayout.Space(4);
            DrawDeploy(settings, so);
            GUILayout.Space(4);
            DrawLog(so);
            GUILayout.Space(8);

            EditorTabBase.DrawActionBar(new (string, Color, Action)[]
            {
                ("저장", EditorTabBase.COL_SUCCESS, () =>
                {
                    so.ApplyModifiedProperties();
                    settings.Save();
                    _dashboard.ShowNotification("설정 저장 완료", EditorTabBase.NotificationType.Success);
                }),
                ("초기 설정 다시 실행", EditorTabBase.COL_WARN, () =>
                {
                    settings.setupCompleted = false;
                    settings.Save();
                    _dashboard.BackToDashboard();
                }),
            });

            so.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();
        }

        void DrawSupabase(GameServerSettings settings, SerializedObject so)
        {
            var status = settings.IsSupabaseConfigured ? "● Connected" : "○ 미설정";
            if (!EditorTabBase.DrawSectionFoldout(ref _foldSupabase,
                $"Supabase  {status}", GameServerDashboard.COL_SUPABASE))
                return;

            EditorTabBase.BeginBody();
            EditorGUILayout.PropertyField(so.FindProperty("supabaseUrl"), new GUIContent("Project URL"));

            var anonKey = EditorGUILayout.TextField("Anon Key", GameServerSettings.SupabaseAnonKey);
            if (anonKey != GameServerSettings.SupabaseAnonKey)
                GameServerSettings.SupabaseAnonKey = anonKey;

            var dbPw = EditorGUILayout.PasswordField("DB Password", GameServerSettings.SupabaseDbPassword);
            if (dbPw != GameServerSettings.SupabaseDbPassword)
                GameServerSettings.SupabaseDbPassword = dbPw;

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (EditorTabBase.DrawColorBtn("연결 테스트", GameServerDashboard.COL_SUPABASE))
                _dashboard.ShowNotification("연결 테스트 — 구현 예정", EditorTabBase.NotificationType.Info);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(settings.SupabaseProjectId))
            {
                if (EditorTabBase.DrawLinkButton("Supabase 대시보드"))
                    Application.OpenURL(settings.SupabaseDashboardUrl);
            }
            EditorGUILayout.EndHorizontal();
            EditorTabBase.EndBody();
        }

        void DrawDeploy(GameServerSettings settings, SerializedObject so)
        {
            var status = settings.IsDeployConfigured ? "● 설정됨" : "○ 미설정";
            if (!EditorTabBase.DrawSectionFoldout(ref _foldDeploy,
                $"배포 설정  {status}", GameServerDashboard.COL_PRIMARY))
                return;

            EditorTabBase.BeginBody();

            // GitHub
            EditorTabBase.DrawSubLabel("GitHub");
            var token = EditorGUILayout.PasswordField("Token", GameServerSettings.GithubToken);
            if (token != GameServerSettings.GithubToken)
                GameServerSettings.GithubToken = token;
            EditorGUILayout.PropertyField(so.FindProperty("githubRepoName"), new GUIContent("Repo Name"));
            var gh = PrerequisiteChecker.CheckGh();
            EditorTabBase.DrawToolStatus("gh", gh.Installed, gh.Version, gh.LoggedIn, gh.Account);

            if (gh.LoggedIn && !string.IsNullOrEmpty(gh.Account) && !string.IsNullOrEmpty(settings.githubRepoName))
            {
                if (EditorTabBase.DrawLinkButton($"GitHub에서 보기 ({gh.Account}/{settings.githubRepoName})"))
                    Application.OpenURL($"https://github.com/{gh.Account}/{settings.githubRepoName}");
            }

            GUILayout.Space(8);

            // GCP
            EditorTabBase.DrawSubLabel("Google Cloud");
            EditorGUILayout.PropertyField(so.FindProperty("gcpProjectId"), new GUIContent("Project ID"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpRegion"), new GUIContent("Region"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpServiceName"), new GUIContent("Service Name"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpMinInstances"), new GUIContent("Min Instances"));
            var minInst = so.FindProperty("gcpMinInstances").intValue;
            EditorTabBase.DrawDescription(
                minInst == 0
                    ? "ⓘ 0 = 콜드스타트 2~5초 지연 (무료)"
                    : $"ⓘ {minInst} = 항상 켜짐 (~{minInst * 5}만원/월 추가)");
            var gcloud = PrerequisiteChecker.CheckGcloud();
            EditorTabBase.DrawToolStatus("gcloud", gcloud.Installed, gcloud.Version, gcloud.LoggedIn, gcloud.Account);

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (EditorTabBase.DrawLinkButton("Cloud Run 콘솔"))
                Application.OpenURL("https://console.cloud.google.com/run");
            EditorGUILayout.EndHorizontal();
            EditorTabBase.EndBody();
        }

        void DrawLog(SerializedObject so)
        {
            if (!EditorTabBase.DrawSectionFoldout(ref _foldLog, "서버 로그", EditorTabBase.COL_INFO))
                return;

            EditorTabBase.BeginBody();
            EditorGUILayout.PropertyField(so.FindProperty("serverLogToConsole"),
                new GUIContent("Cloud Run 로그 → Console", "배포된 서버의 로그를 Unity Console에 표시합니다."));
            EditorTabBase.EndBody();
        }
    }
}
