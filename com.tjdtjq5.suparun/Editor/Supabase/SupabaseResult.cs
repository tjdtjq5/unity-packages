using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// Supabase 호출 실패의 종류. **HTTP 상태코드로만 판정한다.**
    ///
    /// 본문 문구(`"quota exceeded"` 같은)를 매칭하지 않는 이유: Supabase 가 문구를 바꿔도
    /// 컴파일은 통과하고 예외도 안 나면서 **분류만 조용히 틀리게** 된다. 상태코드는 계약이라 안정적이다.
    ///
    /// 그래서 '무료 플랜 한도 초과' 같은 분류는 두지 않는다 — 그게 402 로 올지 403 으로 올지
    /// 코드만으로는 알 수 없다. 판정할 수 없는 이름을 두면 그 자체가 거짓말이 된다.
    /// 대신 <see cref="SupabaseResult{T}.Hint"/> 가 가능성을 언급하고, 원문 메시지를 그대로 보여준다.
    /// </summary>
    public enum SupabaseErrorKind
    {
        Ok,
        /// <summary>401 — 토큰이 없거나 만료됨.</summary>
        Auth,
        /// <summary>403 — 권한 없음. 플랜 한도가 이렇게 오기도 한다.</summary>
        Forbidden,
        /// <summary>402 — 결제·플랜.</summary>
        Payment,
        /// <summary>404 — 대상 없음.</summary>
        NotFound,
        /// <summary>409 — 이미 있음 / 현재 상태에서 불가.</summary>
        Conflict,
        /// <summary>400·422 — 요청 값 문제.</summary>
        Validation,
        /// <summary>429 — 너무 잦음.</summary>
        RateLimit,
        /// <summary>5xx — Supabase 쪽 오류.</summary>
        Server,
        /// <summary>연결 자체가 안 됨(코드 -1).</summary>
        Network,
        Unknown,
    }

    /// <summary>
    /// Supabase Management API 응답 하나.
    ///
    /// 반환값이 없는 호출은 <c>SupabaseResult&lt;bool&gt;</c> 로 표현한다 — 구조체는 상속이 안 되므로
    /// 비제네릭 형제를 따로 두는 것보다 이쪽이 단순하다.
    ///
    /// **재시도하지 않는다.** 에디터 도구라 항상 사람이 보고 있고, 프로젝트 생성처럼 멱등하지 않은
    /// 호출에서 '응답만 유실된' 재시도는 프로젝트를 두 개 만든다.
    /// </summary>
    public readonly struct SupabaseResult<T>
    {
        public readonly bool Ok;
        public readonly T Value;
        /// <summary>HTTP 상태코드. -1 이면 요청 자체가 실패한 것(네트워크·예외).</summary>
        public readonly long HttpStatus;
        public readonly SupabaseErrorKind Kind;
        /// <summary>Supabase 가 준 메시지 원문. 요약하거나 의역하지 않는다.</summary>
        public readonly string Message;
        /// <summary>Kind 에서 유도한 조치 안내. 무엇을 해야 하는지만 말한다.</summary>
        public readonly string Hint;
        /// <summary>응답 본문 전체. 팝업에서 접어 보여주거나 로그로 남긴다.</summary>
        public readonly string Raw;

        SupabaseResult(bool ok, T value, long status, SupabaseErrorKind kind,
            string message, string hint, string raw)
        {
            Ok = ok; Value = value; HttpStatus = status;
            Kind = kind; Message = message; Hint = hint; Raw = raw;
        }

        public static SupabaseResult<T> Success(T value, long status = 200, string raw = null) =>
            new(true, value, status, SupabaseErrorKind.Ok, null, null, raw);

        /// <summary>HTTP 응답에서 실패를 만든다. 메시지는 본문에서 뽑고, 종류는 코드로 정한다.</summary>
        public static SupabaseResult<T> Failure(long status, string body)
        {
            var kind = KindOf(status);
            return new SupabaseResult<T>(
                false, default, status, kind, ExtractMessage(body, status), HintFor(kind), body);
        }

        /// <summary>요청을 보내지도 못한 경우(예외·타임아웃).</summary>
        public static SupabaseResult<T> Failure(Exception ex) =>
            new(false, default, -1, SupabaseErrorKind.Network,
                ex?.Message ?? "알 수 없는 오류", HintFor(SupabaseErrorKind.Network), ex?.ToString());

        /// <summary>
        /// 다른 T 로 실패를 옮긴다 — 안에서 부른 호출이 실패했을 때 그대로 위로 올릴 때 쓴다.
        /// 원문에서 다시 만들므로 메시지·종류가 보존된다.
        /// </summary>
        public SupabaseResult<TOther> CarryFailure<TOther>() =>
            HttpStatus == -1
                ? SupabaseResult<TOther>.Failure(new Exception(Message))
                : SupabaseResult<TOther>.Failure(HttpStatus, Raw);

        static SupabaseErrorKind KindOf(long status) => status switch
        {
            >= 200 and < 300 => SupabaseErrorKind.Ok,
            400 or 422 => SupabaseErrorKind.Validation,
            401 => SupabaseErrorKind.Auth,
            402 => SupabaseErrorKind.Payment,
            403 => SupabaseErrorKind.Forbidden,
            404 => SupabaseErrorKind.NotFound,
            409 => SupabaseErrorKind.Conflict,
            429 => SupabaseErrorKind.RateLimit,
            >= 500 and < 600 => SupabaseErrorKind.Server,
            -1 => SupabaseErrorKind.Network,
            _ => SupabaseErrorKind.Unknown,
        };

        /// <summary>
        /// 본문에서 메시지만 꺼낸다. Supabase 는 `{"message":"..."}` 형태로 주지만
        /// 형식이 다를 수 있으므로 실패하면 본문을 그대로 쓴다(잘라서).
        /// </summary>
        static string ExtractMessage(string body, long status)
        {
            if (string.IsNullOrWhiteSpace(body)) return $"HTTP {status}";
            try
            {
                var token = JToken.Parse(body);
                if (token is JObject obj)
                {
                    var msg = obj["message"] ?? obj["msg"] ?? obj["error_description"] ?? obj["error"];
                    if (msg != null && msg.Type != JTokenType.Object)
                    {
                        var s = msg.ToString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
            }
            catch { /* JSON 이 아니면 본문 그대로 */ }

            return body.Length > 300 ? body.Substring(0, 300) + "…" : body;
        }

        static string HintFor(SupabaseErrorKind kind) => kind switch
        {
            SupabaseErrorKind.Auth =>
                "Access Token 이 만료되었거나 잘못되었습니다. Supabase 계정 > Access Tokens 에서 새로 발급해 Settings 에 넣으세요.",
            SupabaseErrorKind.Forbidden =>
                "권한이 없습니다. 토큰 권한과 조직 소속을 확인하세요. 무료 플랜의 프로젝트 개수 한도가 이 코드로 오기도 합니다.",
            SupabaseErrorKind.Payment =>
                "결제 또는 플랜 문제입니다. 무료 플랜의 프로젝트 개수 한도일 수 있습니다 — Supabase 대시보드에서 플랜과 사용량을 확인하세요.",
            SupabaseErrorKind.NotFound =>
                "대상을 찾지 못했습니다. 프로젝트가 삭제되었거나 참조(ref)가 잘못되었을 수 있습니다.",
            SupabaseErrorKind.Conflict =>
                "이미 존재하거나, 지금 상태에서는 할 수 없는 작업입니다. 이름 중복이나 진행 중인 다른 작업을 확인하세요.",
            SupabaseErrorKind.Validation =>
                "요청 값이 올바르지 않습니다. 아래 메시지에 어느 항목인지 나옵니다.",
            SupabaseErrorKind.RateLimit =>
                "요청이 너무 잦습니다. 잠시 후 다시 시도하세요.",
            SupabaseErrorKind.Server =>
                "Supabase 서버 오류입니다. 잠시 후 다시 시도하고, 계속되면 Supabase 상태 페이지를 확인하세요.",
            SupabaseErrorKind.Network =>
                "네트워크에 연결하지 못했습니다. 인터넷 연결과 방화벽을 확인하세요.",
            _ => "예상하지 못한 응답입니다. 아래 원문을 확인하세요.",
        };
    }

    /// <summary>
    /// 결과를 화면에 알리는 헬퍼. **띄울지 말지는 호출처가 정한다** —
    /// 컴파일 후 자동 스키마 반영처럼 조용히 돌아야 하는 경로에서 모달이 뜨면 에디터를 가로막는다.
    /// </summary>
    public static class SupabaseResultExtensions
    {
        /// <summary>실패면 팝업을 띄우고 false, 성공이면 아무것도 하지 않고 true.</summary>
        public static bool ShowErrorDialog<T>(this SupabaseResult<T> r, string what)
        {
            if (r.Ok) return true;

            var body = $"{what} 실패\n\n{r.Message}\n\n{r.Hint}";
            var detail = string.IsNullOrWhiteSpace(r.Raw) ? null : r.Raw;

            // 원문은 길 수 있어 모달에 다 넣지 않는다. 필요하면 콘솔에서 본다.
            if (detail != null)
                Debug.LogWarning($"[SupaRun:Supabase] {what} 실패 (HTTP {r.HttpStatus} / {r.Kind})\n{detail}");

            EditorUtility.DisplayDialog(
                $"Supabase — {r.Kind}",
                body + (detail != null ? "\n\n자세한 응답은 Console 에 남겼습니다." : ""),
                "확인");
            return false;
        }

        /// <summary>실패를 콘솔에만 남긴다. 배경 작업용.</summary>
        public static bool LogIfFailed<T>(this SupabaseResult<T> r, string what)
        {
            if (r.Ok) return true;
            Debug.LogWarning(
                $"[SupaRun:Supabase] {what} 실패 (HTTP {r.HttpStatus} / {r.Kind}) — {r.Message}\n{r.Hint}");
            return false;
        }

        /// <summary>로그·팝업에 쓸 한 줄 요약.</summary>
        public static string ToShortString<T>(this SupabaseResult<T> r) =>
            r.Ok ? "OK" : $"[{r.Kind}] {r.Message}";
    }
}
