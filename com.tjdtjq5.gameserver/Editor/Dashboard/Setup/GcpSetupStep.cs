using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class GcpSetupStep : ISetupStep
    {
        readonly GameServerDashboard _dashboard;

        public string Title => "Google Cloud";
        public string Description => "서버를 Cloud Run에 배포할 때 필요합니다.\n개발은 LocalGameDB로 가능하므로, 배포할 때 설정해도 됩니다.";
        public Color AccentColor => GameServerDashboard.COL_GCP;
        public bool IsRequired => false;
        public bool IsCompleted => GameServerSettings.Instance.IsGcpConfigured;
        public bool IsSkipped { get; private set; }

        public GcpSetupStep(GameServerDashboard dashboard) => _dashboard = dashboard;

        public void OnDraw()
        {
            var settings = GameServerSettings.Instance;
            var so = new SerializedObject(settings);
            so.Update();

            // 설정하면?/안하면?
            EditorTabBase.DrawInfoBox(
                new[]
                {
                    "서버를 인터넷에 배포 가능",
                    "다른 사람이 게임에 접속 가능",
                    "테스트 단계 무료 (월 200만 요청)",
                },
                new[]
                {
                    "내 PC에서만 서버 실행 (개발/테스트)",
                    "나중에 ⚙ 버튼에서 언제든 설정 가능",
                });

            GUILayout.Space(8);

            // ① GCP 가입
            EditorTabBase.DrawSubLabel("① GCP 가입 + 결제 활성화");
            EditorTabBase.BeginBody();
            EditorTabBase.DrawDescription("결제 활성화 = 유료가 아닙니다.\n무료 할당량 내에서는 과금이 발생하지 않습니다.");
            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (EditorTabBase.DrawLinkBtn("GCP 콘솔", GameServerDashboard.COL_GCP))
                Application.OpenURL("https://console.cloud.google.com");
            if (EditorTabBase.DrawLinkBtn("무료 체험 ($300 크레딧)", GameServerDashboard.COL_GCP))
                Application.OpenURL("https://cloud.google.com/free");
            EditorGUILayout.EndHorizontal();
            EditorTabBase.EndBody();

            GUILayout.Space(4);

            // ② Project ID
            EditorTabBase.DrawSubLabel("② 프로젝트 생성 / 선택");
            EditorTabBase.BeginBody();
            if (EditorTabBase.DrawLinkBtn("프로젝트 만들기", GameServerDashboard.COL_GCP))
                Application.OpenURL("https://console.cloud.google.com/projectcreate");
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(so.FindProperty("gcpProjectId"), new GUIContent("Project ID"));
            EditorGUILayout.PropertyField(so.FindProperty("gcpRegion"), new GUIContent("Region"));
            EditorTabBase.DrawDescription("추천: asia-northeast3 (서울)");
            EditorTabBase.EndBody();

            GUILayout.Space(4);

            // ③ gcloud CLI
            EditorTabBase.DrawSubLabel("③ gcloud CLI");
            EditorTabBase.BeginBody();
            var gcloud = PrerequisiteChecker.CheckGcloud();
            EditorTabBase.DrawToolStatus("gcloud", gcloud.Installed, gcloud.Version,
                gcloud.LoggedIn, gcloud.Account);

            if (!gcloud.Installed)
            {
                if (EditorTabBase.DrawLinkBtn("gcloud CLI 설치하기"))
                    Application.OpenURL("https://cloud.google.com/sdk/docs/install");
            }
            else if (!gcloud.LoggedIn)
            {
                if (EditorTabBase.DrawColorBtn("로그인 실행", GameServerDashboard.COL_GCP))
                    PrerequisiteChecker.RunGcloudLogin();
            }
            else if (!string.IsNullOrEmpty(settings.gcpProjectId) &&
                     gcloud.Project != settings.gcpProjectId)
            {
                if (EditorTabBase.DrawColorBtn("프로젝트 설정", GameServerDashboard.COL_GCP))
                    PrerequisiteChecker.SetGcloudProject(settings.gcpProjectId);
            }

            if (gcloud.Installed && gcloud.LoggedIn && !string.IsNullOrEmpty(gcloud.Project))
                EditorTabBase.DrawDescription($"✓ 프로젝트: {gcloud.Project}", EditorTabBase.COL_SUCCESS);

            EditorTabBase.EndBody();

            so.ApplyModifiedProperties();
        }

        public void OnSkip() => IsSkipped = true;
    }
}
