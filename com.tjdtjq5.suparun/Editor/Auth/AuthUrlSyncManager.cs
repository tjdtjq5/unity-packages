using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>Bundle ID / Cloud Run URL 변경 감지 → Supabase Management API로 자동 동기화.</summary>
    public static class AuthUrlSyncManager
    {
        static string PREF => EditorPrefUtils.ProjectPrefix;

        static string KEY_BUNDLE_ID => PREF + "Synced_BundleId";
        static string KEY_CLOUD_RUN_URL => PREF + "Synced_CloudRunUrl";
        static string KEY_SUPABASE_URL => PREF + "Synced_SupabaseUrl";
        /// <summary>반영한 허용 목록 원문. 규칙이 바뀌었는지 판단하려면 값이 아니라 결과를 비교해야 한다.</summary>
        static string KEY_REDIRECT_LIST => PREF + "Synced_RedirectList";

        /// <summary>마지막 동기화 결과.</summary>
        public enum SyncState { Unknown, Synced, NoToken, Error }

        public static SyncState LastState { get; private set; } = SyncState.Unknown;
        public static string LastError { get; private set; }
        public static bool IsSyncing { get; private set; }
        /// <summary>Access Token이 만료/무효한 경우 true.</summary>
        public static bool IsTokenExpired { get; private set; }

        /// <summary>현재 값과 캐시 비교. 변경 시 자동 동기화 시도.</summary>
        public static void CheckAndSync(SupaRunSettings settings)
        {
            // Access Token + Supabase URL만 있으면 동기화 가능 (AnonKey/DBPassword 불필요)
            if (string.IsNullOrEmpty(settings.supabaseUrl)) return;
            if (string.IsNullOrEmpty(settings.SupabaseProjectId)) return;

            var current = GetCurrentValues(settings);
            var cached = GetCachedValues();

            // 허용 목록 자체도 비교한다. 값 셋이 그대로여도 **목록 규칙이 바뀌면**(항목 추가 등)
            // 반영해야 하는데, 셋만 보면 그 변경이 조용히 묻힌다.
            var currentList = BuildRedirectUrlList(current);
            var cachedList = EditorPrefs.GetString(KEY_REDIRECT_LIST, "");

            if (current.bundleId == cached.bundleId &&
                current.cloudRunUrl == cached.cloudRunUrl &&
                current.supabaseUrl == cached.supabaseUrl &&
                currentList == cachedList)
            {
                if (!string.IsNullOrEmpty(cached.bundleId))
                {
                    LastState = SyncState.Synced;
                    return;
                }
            }

            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                LastState = SyncState.NoToken;
                return;
            }

            _ = SyncToSupabase(settings, current);
        }

        /// <summary>수동 동기화 트리거.</summary>
        public static void ForceSync(SupaRunSettings settings)
        {
            var token = SupaRunSettings.Instance.SupabaseAccessToken;
            if (string.IsNullOrEmpty(token))
            {
                LastState = SyncState.NoToken;
                return;
            }

            var current = GetCurrentValues(settings);
            _ = SyncToSupabase(settings, current);
        }

        static async UniTaskVoid SyncToSupabase(SupaRunSettings settings, (string bundleId, string cloudRunUrl, string supabaseUrl) current)
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

                var r = await SupabaseManagementApi.PatchAuthConfig(
                    projectRef, SupaRunSettings.Instance.SupabaseAccessToken, body);

                if (r.Ok)
                {
                    SaveCachedValues(current);
                    LastState = SyncState.Synced;
                    LastError = null;
                    IsTokenExpired = false;
                    Debug.Log($"[SupaRun:Auth] Auth URL 동기화 완료 — Site: {siteUrl}");
                }
                else
                {
                    LastState = SyncState.Error;
                    LastError = r.Message;
                    // 예전에는 에러 문자열에서 "HTTP 401" 을 찾았다. 문구가 바뀌면 조용히 틀리는 방식이라
                    // 상태코드로 분류된 Kind 를 본다.
                    IsTokenExpired = r.Kind is SupabaseErrorKind.Auth or SupabaseErrorKind.Forbidden;
                    Debug.LogWarning($"[SupaRun:Auth] 동기화 실패: {r.ToShortString()}");
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
                // 도메인 전체를 연다. 예전에는 `/auth/callback` 하나만 있었는데, 어드민은
                // `/admin/index.html` 에 있어서 목록에 없었다. 목록에 없으면 Supabase 가
                // **site_url 로 폴백**하고, 그게 게임용 커스텀 스킴이라 브라우저가 열지 못한다
                // ("scheme does not have a registered handler"). 우리 도메인이므로 열어도 된다.
                sb.Append(',');
                sb.Append($"{values.cloudRunUrl.TrimEnd('/')}/**");
            }
            // localhost 와 127.0.0.1 은 Supabase 허용 목록에서 **다른 문자열**이다.
            // 에디터 로그인 콜백을 받는 로컬 브리지가 127.0.0.1 에 바인딩하므로 둘 다 넣는다.
            sb.Append(",http://localhost:*/**");
            sb.Append(",http://127.0.0.1:*/**");
            return sb.ToString();
        }

        static (string bundleId, string cloudRunUrl, string supabaseUrl) GetCurrentValues(SupaRunSettings settings)
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
            EditorPrefs.SetString(KEY_REDIRECT_LIST, BuildRedirectUrlList(values));
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
