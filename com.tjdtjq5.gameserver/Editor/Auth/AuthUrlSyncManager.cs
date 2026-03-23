using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    /// <summary>Bundle ID / Cloud Run URL 변경 감지 → Supabase Management API로 자동 동기화.</summary>
    public static class AuthUrlSyncManager
    {
        const string PREF = "GameServer_Synced_";
        const string KEY_BUNDLE_ID = PREF + "BundleId";
        const string KEY_CLOUD_RUN_URL = PREF + "CloudRunUrl";
        const string KEY_SUPABASE_URL = PREF + "SupabaseUrl";

        /// <summary>마지막 동기화 결과.</summary>
        public enum SyncState { Unknown, Synced, NoToken, Error }

        public static SyncState LastState { get; private set; } = SyncState.Unknown;
        public static string LastError { get; private set; }
        public static bool IsSyncing { get; private set; }
        /// <summary>Access Token이 만료/무효한 경우 true.</summary>
        public static bool IsTokenExpired { get; private set; }

        /// <summary>현재 값과 캐시 비교. 변경 시 자동 동기화 시도.</summary>
        public static void CheckAndSync(GameServerSettings settings)
        {
            // Access Token + Supabase URL만 있으면 동기화 가능 (AnonKey/DBPassword 불필요)
            if (string.IsNullOrEmpty(settings.supabaseUrl)) return;
            if (string.IsNullOrEmpty(settings.SupabaseProjectId)) return;

            var current = GetCurrentValues(settings);
            var cached = GetCachedValues();

            if (current.bundleId == cached.bundleId &&
                current.cloudRunUrl == cached.cloudRunUrl &&
                current.supabaseUrl == cached.supabaseUrl)
            {
                if (!string.IsNullOrEmpty(cached.bundleId))
                {
                    LastState = SyncState.Synced;
                    return;
                }
            }

            var token = GameServerSettings.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                LastState = SyncState.NoToken;
                return;
            }

            SyncToSupabase(settings, current);
        }

        /// <summary>수동 동기화 트리거.</summary>
        public static void ForceSync(GameServerSettings settings)
        {
            var token = GameServerSettings.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                LastState = SyncState.NoToken;
                return;
            }

            var current = GetCurrentValues(settings);
            SyncToSupabase(settings, current);
        }

        static async void SyncToSupabase(GameServerSettings settings, (string bundleId, string cloudRunUrl, string supabaseUrl) current)
        {
            IsSyncing = true;
            LastState = SyncState.Unknown;

            try
            {
                var projectRef = settings.SupabaseProjectId;
                if (string.IsNullOrEmpty(projectRef))
                {
                    LastState = SyncState.Error;
                    LastError = "Supabase Project ID를 추출할 수 없습니다";
                    return;
                }

                var siteUrl = $"{current.bundleId}://auth";
                var redirectUrls = BuildRedirectUrlList(current);
                var body = $"{{\"site_url\":\"{Escape(siteUrl)}\",\"uri_allow_list\":\"{Escape(redirectUrls)}\"}}";

                var (ok, error) = await SupabaseManagementApi.PatchAuthConfig(
                    projectRef, GameServerSettings.SupabaseAccessToken, body);

                if (ok)
                {
                    SaveCachedValues(current);
                    LastState = SyncState.Synced;
                    LastError = null;
                    IsTokenExpired = false;
                    Debug.Log($"[GameServer:Auth] Auth URL 동기화 완료 — Site: {siteUrl}");
                }
                else
                {
                    LastState = SyncState.Error;
                    LastError = error;
                    // 401/403이면 토큰 만료
                    if (error != null && (error.Contains("HTTP 401") || error.Contains("HTTP 403")))
                        IsTokenExpired = true;
                    Debug.LogWarning($"[GameServer:Auth] 동기화 실패: {error}");
                }
            }
            catch (System.Exception ex)
            {
                LastState = SyncState.Error;
                LastError = ex.Message;
            }
            finally
            {
                IsSyncing = false;
            }
        }

        static string BuildRedirectUrlList((string bundleId, string cloudRunUrl, string supabaseUrl) values)
        {
            var sb = new StringBuilder();
            sb.Append($"{values.bundleId}://auth");
            if (!string.IsNullOrEmpty(values.cloudRunUrl))
            {
                sb.Append(',');
                sb.Append($"{values.cloudRunUrl.TrimEnd('/')}/auth/callback");
            }
            sb.Append(",http://localhost:*/**");
            return sb.ToString();
        }

        static (string bundleId, string cloudRunUrl, string supabaseUrl) GetCurrentValues(GameServerSettings settings)
            => (PlayerSettings.applicationIdentifier ?? "", settings.cloudRunUrl ?? "", settings.supabaseUrl ?? "");

        static (string bundleId, string cloudRunUrl, string supabaseUrl) GetCachedValues()
            => (EditorPrefs.GetString(KEY_BUNDLE_ID, ""),
                EditorPrefs.GetString(KEY_CLOUD_RUN_URL, ""),
                EditorPrefs.GetString(KEY_SUPABASE_URL, ""));

        static void SaveCachedValues((string bundleId, string cloudRunUrl, string supabaseUrl) values)
        {
            EditorPrefs.SetString(KEY_BUNDLE_ID, values.bundleId);
            EditorPrefs.SetString(KEY_CLOUD_RUN_URL, values.cloudRunUrl);
            EditorPrefs.SetString(KEY_SUPABASE_URL, values.supabaseUrl);
        }

        public static void InvalidateCache()
        {
            EditorPrefs.DeleteKey(KEY_BUNDLE_ID);
            EditorPrefs.DeleteKey(KEY_CLOUD_RUN_URL);
            EditorPrefs.DeleteKey(KEY_SUPABASE_URL);
            LastState = SyncState.Unknown;
            IsTokenExpired = false;
        }

        static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }
}
