using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 어드민 대행 Edge Function 을 배포한다.
    ///
    /// 왜 Edge Function 인가: 어드민 웹은 `api.supabase.com` 을 직접 못 부르고(CORS 가
    /// `https://supabase.com` 만 허용), PAT 를 브라우저에 내려보낼 수도 없다. 누군가 대신
    /// 불러야 하는데 그 자리를 Cloud Run 이 맡으면 **첫 배포 전에는 존재하지 않는다** —
    /// 배포에 필요한 값을 어드민에서 받으려는데 어드민을 띄울 서버가 없는 순환이 생긴다.
    /// Edge Function 은 Supabase 프로젝트가 생기는 순간 존재하므로 그 순환이 끊긴다.
    ///
    /// **해시를 비교해 바뀌었을 때만 올린다.** 이유는 오늘 dist 에서 겪은 것과 같다 —
    /// 소스를 고쳐도 다시 올리지 않으면 옛것이 계속 돌고, 배포는 성공했는데 동작만 안 바뀐다.
    /// 그 실패는 로그가 깨끗해서 원인을 엉뚱한 데서 찾게 된다.
    /// </summary>
    public static class EdgeFunctionDeployer
    {
        public const string Slug = "suparun-admin";
        const string DisplayName = "SupaRun Admin";

        /// <summary>
        /// JWT 게이트를 Supabase 에 맡기지 않는다. 켜면 anon key 도 통과하므로 실질 방어가 안 되는데,
        /// 대신 401 응답 형태를 우리가 못 정해서 화면이 이유를 설명할 수 없게 된다.
        /// 판정은 함수 안에서 하고(`identify`), 그쪽이 사유까지 돌려준다.
        /// </summary>
        const bool VerifyJwt = false;

        static string HashFileFor(string envName) =>
            $"ProjectSettings/SupaRunEdgeFnHash.{Sanitize(envName)}.txt";

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "default";
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        /// <summary>패키지 안의 함수 소스. 없으면 null.</summary>
        public static string ReadSource()
        {
            var path = Path.Combine(TemplateRoot(), $"EdgeFunctionTemplate~/{Slug}.ts");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        static string TemplateRoot()
        {
            var guids = AssetDatabase.FindAssets("t:DefaultAsset",
                new[] { "Packages/com.tjdtjq5.suparun/Templates" });
            if (guids.Length > 0)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[0]);
                return Path.GetDirectoryName(Path.GetDirectoryName(p));
            }
            return "Packages/com.tjdtjq5.suparun/Templates";
        }

        static string Sha256(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        /// <summary>이 환경에 올라간 소스가 지금 것과 같은가.</summary>
        public static bool IsUpToDate(SupaRunSettings.EnvironmentData env)
        {
            var source = ReadSource();
            if (source == null || env == null) return false;
            var file = HashFileFor(env.name);
            return File.Exists(file) && File.ReadAllText(file).Trim() == Sha256(source);
        }

        /// <summary>
        /// 편집 환경에 배포한다. 이미 같은 소스가 올라가 있으면 아무것도 하지 않는다.
        /// <paramref name="force"/> 는 해시를 무시하고 올린다 — 함수를 손으로 지운 뒤 복구할 때 쓴다.
        /// </summary>
        public static async UniTask<SupabaseResult<bool>> DeployAsync(
            SupaRunSettings.EnvironmentData env = null, bool force = false)
        {
            env ??= SupaRunSettings.Instance.Current;

            var source = ReadSource();
            if (source == null)
                return SupabaseResult<bool>.Local(
                    $"함수 소스를 찾지 못했습니다 — Templates/EdgeFunctionTemplate~/{Slug}.ts");

            var projectRef = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(projectRef) || string.IsNullOrEmpty(token))
                return SupabaseResult<bool>.Local(
                    $"환경 '{env.name}' 에 Supabase URL 또는 Access Token 이 없습니다.");

            // PAT 등록을 **먼저** 한다. 함수 소스가 최신이라 아래에서 빠져나가더라도
            // 토큰은 들어가야 한다 — 둘은 조건이 다르다(소스는 그대로인데 PAT 만 재발급하는 경우가 있다).
            // 뒤에 두면 "함수는 최신인데 모든 호출이 409" 인 상태가 만들어지고 원인이 안 드러난다.
            var stored = await StoreTokenAsync(projectRef, token);
            if (!stored.Ok) return stored.CarryFailure<bool>();

            var hash = Sha256(source);
            var hashFile = HashFileFor(env.name);
            if (!force && File.Exists(hashFile) && File.ReadAllText(hashFile).Trim() == hash)
                return SupabaseResult<bool>.Success(false);   // false = 올릴 것이 없었다

            // 있으면 PATCH, 없으면 POST. `GET` 이 404 면 없는 것이다 —
            // 없는데 PATCH 하면 404 를 "배포 실패" 로 오해하게 된다.
            var existing = await SupabaseManagementApi.GetFunction(projectRef, token, Slug);
            SupabaseResult<bool> result;

            if (existing.Ok)
                result = await SupabaseManagementApi.UpdateFunction(
                    projectRef, token, Slug, DisplayName, source, VerifyJwt);
            else if (existing.Kind == SupabaseErrorKind.NotFound)
                result = await SupabaseManagementApi.CreateFunction(
                    projectRef, token, Slug, DisplayName, source, VerifyJwt);
            else
                return existing.CarryFailure<bool>();   // 권한·네트워크 문제를 생성 시도로 덮지 않는다

            if (!result.Ok) return result;

            File.WriteAllText(hashFile, hash);
            Debug.Log($"[SupaRun:EdgeFn] '{Slug}' 배포 완료 — 환경 {env.name}");
            return SupabaseResult<bool>.Success(true);
        }

        /// <summary>
        /// PAT 를 `suparun_secret` 에 넣는다. **PAT 로 실행한다**(에디터 로그인이 아니라) —
        /// 이 시점에는 아직 관리자가 없을 수 있고, 없으면 로그인해도 RLS 에 막힌다.
        /// </summary>
        static async UniTask<SupabaseResult<string>> StoreTokenAsync(string projectRef, string token)
        {
            // 달러 인용 — 토큰에 따옴표가 있어도 SQL 이 깨지지 않는다.
            var sql =
                "INSERT INTO suparun_secret(key, value, updated_at, updated_by) " +
                $"VALUES ('supabase_access_token', $tok${token}$tok$, " +
                "(extract(epoch from now()) * 1000)::bigint, 'editor') " +
                "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, " +
                "updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by;";

            return await SupabaseManagementApi.RunQuery(projectRef, token, sql);
        }

        /// <summary>배포된 함수가 응답하는지 확인한다. 반환은 함수가 알려준 소스 버전.</summary>
        public static async UniTask<SupabaseResult<string>> PingAsync(
            SupaRunSettings.EnvironmentData env = null)
        {
            env ??= SupaRunSettings.Instance.Current;
            var url = $"{env.supabaseUrl?.TrimEnd('/')}/functions/v1/{Slug}/ping";

            using var req = UnityEngine.Networking.UnityWebRequest.Get(url);
            req.SetRequestHeader("apikey", env.supabaseAnonKey ?? "");
            req.SetRequestHeader("Authorization", $"Bearer {env.supabaseAnonKey}");
            req.timeout = 20;

            var op = req.SendWebRequest();
            while (!op.isDone) await UniTask.Yield();

            var text = req.downloadHandler?.text ?? "";
            return req.responseCode is >= 200 and < 300
                ? SupabaseResult<string>.Success(text, req.responseCode, text)
                : SupabaseResult<string>.Failure(req.responseCode, text);
        }
    }
}
