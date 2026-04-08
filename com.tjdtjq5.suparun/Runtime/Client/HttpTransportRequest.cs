using System.Collections.Generic;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// HTTP 요청 DTO. IHttpTransport.SendAsync에 전달.
    /// 헤더는 호출자가 직접 채우거나 IAuthStrategy가 채운다.
    /// </summary>
    public class HttpTransportRequest
    {
        /// <summary>절대 URL.</summary>
        public string Url;

        /// <summary>HTTP 메서드 ("GET", "POST", "PUT", "DELETE" 등).</summary>
        public string Method;

        /// <summary>요청 헤더. 빈 dict로 초기화. IAuthStrategy가 호출 직전에 인증 헤더를 추가.</summary>
        public Dictionary<string, string> Headers = new Dictionary<string, string>();

        /// <summary>요청 body (POST/PUT 시). null이면 body 없음.</summary>
        public byte[] Body;

        /// <summary>타임아웃 (초). 기본 15초.</summary>
        public int TimeoutSeconds = 15;
    }
}
