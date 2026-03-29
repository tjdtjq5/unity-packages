using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// Management API로 pg_authid의 SCRAM-SHA-256 해시를 조회하여
    /// DB 비밀번호를 로컬에서 검증합니다. TCP 직접 연결 불필요 (IPv6 문제 회피).
    /// </summary>
    public static class PostgresConnectionTester
    {
        /// <summary>DB 비밀번호 검증. Management API access token 필요.</summary>
        public static async Task<(bool ok, string error)> VerifyPassword(
            string projectRef, string accessToken, string password)
        {
            if (string.IsNullOrEmpty(accessToken))
                return (false, "Access Token이 필요합니다");

            // pg_authid에서 SCRAM 해시 조회
            var (queryOk, result, queryErr) = await SupabaseManagementApi.RunQuery(
                projectRef, accessToken,
                "SELECT rolpassword FROM pg_authid WHERE rolname = 'postgres'");

            if (!queryOk)
                return (false, $"해시 조회 실패: {queryErr}");

            // JSON 파싱: [{"rolpassword":"SCRAM-SHA-256$4096:salt$StoredKey:ServerKey"}]
            var hash = ExtractField(result, "rolpassword");
            if (string.IsNullOrEmpty(hash) || !hash.StartsWith("SCRAM-SHA-256$"))
                return (false, "SCRAM 해시를 가져올 수 없습니다");

            // SCRAM-SHA-256$iterations:salt$StoredKey:ServerKey
            var match = VerifyScramHash(password, hash);
            return match
                ? (true, null)
                : (false, "비밀번호가 일치하지 않습니다");
        }

        // ── SCRAM-SHA-256 검증 ──

        static bool VerifyScramHash(string password, string hash)
        {
            try
            {
                // SCRAM-SHA-256$4096:base64salt$base64StoredKey:base64ServerKey
                var parts = hash.Split('$');
                if (parts.Length < 3) return false;

                var iterSalt = parts[1].Split(':');
                int iterations = int.Parse(iterSalt[0]);
                byte[] salt = Convert.FromBase64String(iterSalt[1]);

                var keys = parts[2].Split(':');
                string expectedStoredKey = keys[0];

                // 비밀번호로 StoredKey 계산
                var saltedPassword = Pbkdf2Sha256(password, salt, iterations);
                var clientKey = HmacSha256(saltedPassword, "Client Key");
                var computedStoredKey = Sha256(clientKey);

                return Convert.ToBase64String(computedStoredKey) == expectedStoredKey;
            }
            catch
            {
                return false;
            }
        }

        // ── Crypto ──

        static byte[] Pbkdf2Sha256(string password, byte[] salt, int iterations)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password));

            var saltPlus = new byte[salt.Length + 4];
            Buffer.BlockCopy(salt, 0, saltPlus, 0, salt.Length);
            saltPlus[salt.Length + 3] = 1; // big-endian INT(1)

            var u = hmac.ComputeHash(saltPlus);
            var result = (byte[])u.Clone();

            for (int i = 1; i < iterations; i++)
            {
                u = hmac.ComputeHash(u);
                for (int j = 0; j < result.Length; j++)
                    result[j] ^= u[j];
            }

            return result;
        }

        static byte[] HmacSha256(byte[] key, string message)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        }

        static byte[] Sha256(byte[] input)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(input);
        }

        // ── JSON 헬퍼 ──

        static string ExtractField(string json, string key)
        {
            var pattern = $"\"{key}\":\"";
            var idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += pattern.Length;
            var end = json.IndexOf('"', idx);
            return end < 0 ? null : json.Substring(idx, end - idx);
        }
    }
}
