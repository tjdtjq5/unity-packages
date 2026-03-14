#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>
    /// UGS Manager 설정. EditorPrefs에 저장. 프로젝트/사용자별 설정.
    /// </summary>
    public static class UGSConfig
    {
        // ─── EditorPrefs 키 ─────────────────────────
        const string KEY_ORG_ID = "UGS_OrganizationId";
        const string KEY_CLOUD_CODE_PATH = "UGS_CloudCodePath";
        const string KEY_DEPLOY_PATH = "UGS_DeployPath";
        const string KEY_COL_KEY_W = "UGS_RC_ColKeyWidth";
        const string KEY_COL_TYPE_W = "UGS_RC_ColTypeWidth";

        // ─── Dashboard ─────────────────────────────
        const string DASHBOARD_BASE = "https://cloud.unity.com/home/organizations";

        /// <summary>조직 ID (Dashboard URL에 사용)</summary>
        public static string OrganizationId
        {
            get => EditorPrefs.GetString(KEY_ORG_ID, "");
            set => EditorPrefs.SetString(KEY_ORG_ID, value);
        }

        /// <summary>Cloud Code 스크립트 경로</summary>
        public static string CloudCodePath
        {
            get => EditorPrefs.GetString(KEY_CLOUD_CODE_PATH, "Assets/UGS/CloudCode");
            set => EditorPrefs.SetString(KEY_CLOUD_CODE_PATH, value);
        }

        /// <summary>Deploy 기본 경로</summary>
        public static string DeployPath
        {
            get => EditorPrefs.GetString(KEY_DEPLOY_PATH, "Assets/UGS");
            set => EditorPrefs.SetString(KEY_DEPLOY_PATH, value);
        }

        /// <summary>Remote Config 컬럼 너비 (키)</summary>
        public static float ColKeyWidth
        {
            get => EditorPrefs.GetFloat(KEY_COL_KEY_W, 160f);
            set => EditorPrefs.SetFloat(KEY_COL_KEY_W, value);
        }

        /// <summary>Remote Config 컬럼 너비 (타입)</summary>
        public static float ColTypeWidth
        {
            get => EditorPrefs.GetFloat(KEY_COL_TYPE_W, 60f);
            set => EditorPrefs.SetFloat(KEY_COL_TYPE_W, value);
        }

        /// <summary>설정이 유효한지 (최소한 조직 ID가 있는지)</summary>
        public static bool IsConfigured => !string.IsNullOrEmpty(OrganizationId);

        /// <summary>Dashboard URL 생성</summary>
        public static string GetDashboardUrl(string projectId, string envId, string path)
        {
            if (string.IsNullOrEmpty(OrganizationId) || string.IsNullOrEmpty(projectId))
                return null;

            if (!string.IsNullOrEmpty(envId) && !string.IsNullOrEmpty(path))
                return $"{DASHBOARD_BASE}/{OrganizationId}/projects/{projectId}/environments/{envId}/{path}";

            if (!string.IsNullOrEmpty(path))
                return $"{DASHBOARD_BASE}/{OrganizationId}/projects/{projectId}/{path}";

            return $"{DASHBOARD_BASE}/{OrganizationId}/projects/{projectId}";
        }

        /// <summary>모든 설정 초기화</summary>
        public static void Reset()
        {
            EditorPrefs.DeleteKey(KEY_ORG_ID);
            EditorPrefs.DeleteKey(KEY_CLOUD_CODE_PATH);
            EditorPrefs.DeleteKey(KEY_DEPLOY_PATH);
            EditorPrefs.DeleteKey(KEY_COL_KEY_W);
            EditorPrefs.DeleteKey(KEY_COL_TYPE_W);
        }
    }
}
#endif
