using System;

namespace Tjdtjq5.SupaRun
{
    [Serializable]
    public class ServerResponse<T>
    {
        public bool success;
        public T data;
        public string error;
        public ErrorType errorType;
        public int statusCode;

        /// <summary>
        /// 호출 시점에 인증된 세션이 있었는지 여부. false면 anonymous(anon key)로 요청이 나갔다는 뜻.
        /// `success=true, data=[]` 같은 silent failure 진단에 사용.
        /// </summary>
        public bool isAuthenticated;

        /// <summary>
        /// 진단 힌트. 호출자가 프로그램으로 분기 처리할 수 있도록 사람이 읽을 수 있는 메타 정보.
        /// 예: "anonymous 호출 — RLS authenticated 정책에 막힐 수 있음", "LocalDB fallback — 서버 미연결".
        /// 정상 응답에서는 null.
        /// </summary>
        public string hint;
    }

    [Serializable]
    public class ServerResponse
    {
        public bool success;
        public string error;
        public ErrorType errorType;
        public int statusCode;

        /// <inheritdoc cref="ServerResponse{T}.isAuthenticated"/>
        public bool isAuthenticated;

        /// <inheritdoc cref="ServerResponse{T}.hint"/>
        public string hint;
    }
}
