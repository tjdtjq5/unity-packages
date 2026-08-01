using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 브리지 응답 헬퍼. 라우트가 두 파일로 갈리면서 공용이 됐다
    /// (<see cref="SupaRunBridge"/> 의 PAT 대행, <see cref="BridgeDeployRoutes"/> 의 배포).
    /// </summary>
    static class BridgeIo
    {
        public static JObject Err(string message, string hint = null)
        {
            var o = new JObject { ["error"] = message };
            if (!string.IsNullOrEmpty(hint)) o["hint"] = hint;
            return o;
        }

        public static void Write(HttpListenerResponse res, int status, JObject body)
        {
            var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        /// <summary>실패를 그대로 전달한다. 원인이 어드민 화면까지 도달해야 한다.</summary>
        public static void Fail(HttpListenerResponse res, int status, string message, string hint = null) =>
            Write(res, status, Err(message, hint));

        public static JObject ReadBody(HttpListenerRequest req)
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
        }
    }
}
