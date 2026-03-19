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
        bool _foldGcp = true;
        bool _foldGitHub = true;
        bool _foldDev = true;

        public SettingsView(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            DrawSupabase(settings, so);
            GUILayout.Space(4);
            DrawGcp(settings, so);
            GUILayout.Space(4);
            DrawGitHub(settings, so);
            GUILayout.Space(4);
            DrawDevMode(so);
            GUILayout.Space(8);

            // 하단 버튼
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
                    _dashboard.BackToDashboard(); // mode 재판정 트리거
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

        void DrawGcp(GameServerSettings settings, SerializedObject so)
        {
            var status = settings.IsGcpConfigured ? "● 설정됨" : "○ 미설정";
            if (!EditorTabBase.DrawSectionFoldout(ref _foldGcp,
                $"Google Cloud  {status}", GameServerDashboard.COL_GCP))
                return;

            EditorTabBase.BeginBody();
            EditorGUILayout.PropertyField(so.FindProperty("gcpProjectId"), new GUIContent("Project ID"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpRegion"), new GUIContent("Region"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpServiceName"), new GUIContent("Service Name"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpMinInstances"), new GUIContent("Min Instances"));

            var minInst = so.FindProperty("gcpMinInstances").intValue;
            EditorTabBase.DrawCellLabel(
                minInst == 0
                    ? "  ⓘ 0 = 콜드스타트 2~5초 지연 (무료)"
                    : $"  ⓘ {minInst} = 항상 켜짐 (~{minInst * 5}만원/월 추가)",
                0, minInst == 0 ? EditorTabBase.COL_WARN : EditorTabBase.COL_INFO);

            GUILayout.Space(4);
            var gcloud = PrerequisiteChecker.CheckGcloud();
            EditorTabBase.DrawToolStatus("gcloud", gcloud.Installed, gcloud.Version,
                gcloud.LoggedIn, gcloud.Account);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (EditorTabBase.DrawLinkButton("Cloud Run 콘솔"))
                Application.OpenURL("https://console.cloud.google.com/run");
            EditorGUILayout.EndHorizontal();
            EditorTabBase.EndBody();
        }

        void DrawGitHub(GameServerSettings settings, SerializedObject so)
        {
            var status = settings.IsGitHubConfigured ? "● 설정됨" : "○ 미설정";
            if (!EditorTabBase.DrawSectionFoldout(ref _foldGitHub,
                $"GitHub  {status}", GameServerDashboard.COL_GITHUB))
                return;

            EditorTabBase.BeginBody();

            var token = EditorGUILayout.PasswordField("Token", GameServerSettings.GithubToken);
            if (token != GameServerSettings.GithubToken)
                GameServerSettings.GithubToken = token;

            EditorGUILayout.PropertyField(so.FindProperty("githubRepoName"), new GUIContent("Repo Name"));

            GUILayout.Space(4);
            var gh = PrerequisiteChecker.CheckGh();
            EditorTabBase.DrawToolStatus("gh", gh.Installed, gh.Version, gh.LoggedIn, gh.Account);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (EditorTabBase.DrawLinkButton("GitHub"))
                Application.OpenURL("https://github.com");
            EditorGUILayout.EndHorizontal();
            EditorTabBase.EndBody();
        }

        void DrawDevMode(SerializedObject so)
        {
            if (!EditorTabBase.DrawSectionFoldout(ref _foldDev, "개발 모드", EditorTabBase.COL_WARN))
                return;

            EditorTabBase.BeginBody();
            EditorGUILayout.PropertyField(so.FindProperty("devMode"),
                new GUIContent("개발 모드", "ON: LocalGameDB로 Unity 내 직접 실행 (서버 불필요)\nOFF: Cloud Run 서버에 연결"));
            EditorGUILayout.PropertyField(so.FindProperty("serverLogToConsole"),
                new GUIContent("서버 로그 → Console", "개발 모드: LocalGameDB 로그 표시\n프로덕션: Cloud Run 로그 표시"));
            EditorTabBase.EndBody();
        }
    }
}
