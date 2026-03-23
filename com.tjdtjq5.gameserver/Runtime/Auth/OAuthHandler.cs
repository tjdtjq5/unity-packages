using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Tjdtjq5.GameServer
{
    /// <summary>OAuth 브라우저 열기 + 토큰 수신. 모바일: 딥링크, PC: localhost.</summary>
    public class OAuthHandler
    {
        readonly string _supabaseUrl;
        readonly string _cloudRunUrl;

        TaskCompletionSource<string> _pendingAuth;
        HttpListener _httpListener;

        public OAuthHandler(string supabaseUrl, string cloudRunUrl = null)
        {
            _supabaseUrl = supabaseUrl?.TrimEnd('/');
            _cloudRunUrl = cloudRunUrl?.TrimEnd('/');

            Application.deepLinkActivated += OnDeepLink;
        }

        /// <summary>현재 플랫폼에 맞는 Redirect URL.</summary>
        public static string GetRedirectUrl(string cloudRunUrl)
        {
            if (Application.isMobilePlatform)
                return $"{Application.identifier}://auth";
            return $"{cloudRunUrl?.TrimEnd('/')}/auth/callback";
        }

        /// <summary>Supabase Redirect URLs에 등록해야 할 URL 목록.</summary>
        public static string[] GetRequiredRedirectUrls(string cloudRunUrl, string bundleId)
        {
            return new[]
            {
                $"{bundleId}://auth",
                $"{cloudRunUrl?.TrimEnd('/')}/auth/callback",
                "http://localhost:*/**"
            };
        }

        /// <summary>OAuth 로그인. 브라우저 열고 토큰 대기.</summary>
        public async Task<string> Authenticate(AuthProvider provider)
        {
            var providerName = provider switch
            {
                AuthProvider.Google => "google",
                AuthProvider.Apple => "apple",
                AuthProvider.Facebook => "facebook",
                AuthProvider.Discord => "discord",
                AuthProvider.Twitter => "twitter",
                AuthProvider.Kakao => "kakao",
                AuthProvider.Twitch => "twitch",
                AuthProvider.Spotify => "spotify",
                AuthProvider.Slack => "slack",
                AuthProvider.GitHub => "github",
                _ => provider.ToString().ToLower()
            };

            _pendingAuth = new TaskCompletionSource<string>();

            if (Application.isMobilePlatform)
            {
                // 모바일: Site URL로 리디렉션 (Supabase가 Site URL로 보냄 → 딥링크 수신)
                var url = $"{_supabaseUrl}/auth/v1/authorize?provider={providerName}";
                Application.OpenURL(url);
                Debug.Log($"[GameServer:Auth] OAuth 시작 (모바일): {providerName}");
            }
            else
            {
                // PC: localhost HTTP 서버
                await StartLocalServer(providerName);
            }

            // 토큰 대기 (120초 타임아웃)
            var timeout = Task.Delay(120000);
            var completed = await Task.WhenAny(_pendingAuth.Task, timeout);

            StopLocalServer();

            if (completed == timeout)
            {
                _pendingAuth = null;
                Debug.LogWarning("[GameServer:Auth] OAuth 타임아웃");
                return null;
            }

            return _pendingAuth.Task.Result;
        }

        /// <summary>게스트 → 소셜 연결용. Supabase Identity Link API 사용.</summary>
        public async Task<string> AuthenticateForLink(AuthProvider provider, string accessToken)
        {
            var providerName = provider switch
            {
                AuthProvider.Google => "google",
                AuthProvider.Apple => "apple",
                AuthProvider.Facebook => "facebook",
                AuthProvider.Discord => "discord",
                AuthProvider.Twitter => "twitter",
                AuthProvider.Kakao => "kakao",
                AuthProvider.Twitch => "twitch",
                AuthProvider.Spotify => "spotify",
                AuthProvider.Slack => "slack",
                AuthProvider.GitHub => "github",
                _ => provider.ToString().ToLower()
            };

            // Supabase Identity Link API: 기존 유저에 소셜 identity 연결
            var linkUrl = $"{_supabaseUrl}/auth/v1/user/identities/authorize?provider={providerName}";
            Debug.Log($"[GameServer:Auth] Identity Link 요청: {providerName}");

            string redirectUrl;
            try
            {
                using var request = new UnityWebRequest(linkUrl, "GET");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                request.timeout = 15;

                var op = request.SendWebRequest();
                while (!op.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[GameServer:Auth] Identity Link API 실패: {request.responseCode} {request.error}");
                    return null;
                }

                var json = JObject.Parse(request.downloadHandler.text);
                redirectUrl = json["url"]?.ToString();
                if (string.IsNullOrEmpty(redirectUrl))
                {
                    Debug.LogWarning("[GameServer:Auth] Identity Link URL을 받지 못했습니다.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameServer:Auth] Identity Link 요청 실패: {ex.Message}");
                return null;
            }

            // 받은 URL로 브라우저 열기 + 콜백 대기
            _pendingAuth = new TaskCompletionSource<string>();

            if (Application.isMobilePlatform)
            {
                Application.OpenURL(redirectUrl);
            }
            else
            {
                // PC: localhost 서버로 리디렉션 추가
                var port = UnityEngine.Random.Range(49152, 65535);
                var callbackUrl = Uri.EscapeDataString($"http://localhost:{port}/callback");
                var urlWithRedirect = redirectUrl.Contains("?")
                    ? $"{redirectUrl}&redirect_to={callbackUrl}"
                    : $"{redirectUrl}?redirect_to={callbackUrl}";

                try
                {
                    _httpListener = new HttpListener();
                    _httpListener.Prefixes.Add($"http://localhost:{port}/");
                    _httpListener.Start();
                    _ = Task.Run(() => ListenForCallback());
                    Application.OpenURL(urlWithRedirect);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GameServer:Auth] localhost 서버 시작 실패: {ex.Message}");
                    _pendingAuth?.TrySetResult(null);
                }
            }

            var timeout = Task.Delay(120000);
            var completed = await Task.WhenAny(_pendingAuth.Task, timeout);

            StopLocalServer();

            if (completed == timeout)
            {
                _pendingAuth = null;
                Debug.LogWarning("[GameServer:Auth] Identity Link 타임아웃");
                return null;
            }

            return _pendingAuth.Task.Result;
        }

        // ── 모바일: 딥링크 수신 ──

        void OnDeepLink(string url)
        {
            var scheme = $"{Application.identifier.ToLower()}://auth";
            if (!url.ToLower().StartsWith(scheme)) return;

            Debug.Log("[GameServer:Auth] 딥링크 수신");
            _pendingAuth?.TrySetResult(url);
        }

        // ── PC: localhost HTTP 서버 ──

        async Task StartLocalServer(string providerName)
        {
            // 랜덤 포트
            var port = UnityEngine.Random.Range(49152, 65535);
            var callbackUrl = $"http://localhost:{port}/callback";

            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{port}/");
                _httpListener.Start();

                Debug.Log($"[GameServer:Auth] localhost:{port} 대기 중");

                // 브라우저 열기
                var redirectUrl = Uri.EscapeDataString(callbackUrl);
                var url = $"{_supabaseUrl}/auth/v1/authorize?provider={providerName}&redirect_to={redirectUrl}";
                Application.OpenURL(url);
                Debug.Log($"[GameServer:Auth] OAuth 시작 (localhost): {providerName}");

                // 백그라운드에서 요청 대기
                _ = Task.Run(() => ListenForCallback());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameServer:Auth] localhost 서버 시작 실패: {ex.Message}");
                _pendingAuth?.TrySetResult(null);
            }
        }

        void ListenForCallback()
        {
            try
            {
                // 1차 요청: 브라우저가 #fragment 포함하여 도착
                // → JS로 fragment를 읽어서 2차 요청으로 전달하는 HTML 반환
                var context1 = _httpListener.GetContext();
                var path = context1.Request.Url.AbsolutePath;

                if (path == "/callback" || path == "/")
                {
                    // fragment를 query로 변환하는 중간 페이지
                    var html = @"<html><body><h2>Logging in...</h2><script>
                        var hash = window.location.hash.substring(1);
                        if (hash) {
                            fetch('/token?' + hash).then(function() {
                                document.body.innerHTML = '<h2>Login successful!</h2><p>You can close this window.</p>';
                                window.close();
                            });
                        } else {
                            document.body.innerHTML = '<h2>Login failed</h2><p>No token received.</p>';
                        }
                    </script></body></html>";
                    var buffer = System.Text.Encoding.UTF8.GetBytes(html);
                    context1.Response.ContentType = "text/html";
                    context1.Response.ContentLength64 = buffer.Length;
                    context1.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context1.Response.Close();

                    // 2차 요청: JS가 /token?access_token=xxx&refresh_token=yyy로 전달
                    var context2 = _httpListener.GetContext();
                    var tokenUrl = context2.Request.Url.ToString();

                    var okBuffer = System.Text.Encoding.UTF8.GetBytes("ok");
                    context2.Response.ContentLength64 = okBuffer.Length;
                    context2.Response.OutputStream.Write(okBuffer, 0, okBuffer.Length);
                    context2.Response.Close();

                    _pendingAuth?.TrySetResult(tokenUrl);
                }
                else
                {
                    // /token?... 직접 도착한 경우
                    var tokenUrl = context1.Request.Url.ToString();
                    var okBuffer = System.Text.Encoding.UTF8.GetBytes("ok");
                    context1.Response.ContentLength64 = okBuffer.Length;
                    context1.Response.OutputStream.Write(okBuffer, 0, okBuffer.Length);
                    context1.Response.Close();

                    _pendingAuth?.TrySetResult(tokenUrl);
                }
            }
            catch (Exception ex)
            {
                if (_httpListener?.IsListening == true)
                    Debug.LogWarning($"[GameServer:Auth] 콜백 수신 실패: {ex.Message}");
            }
        }

        void StopLocalServer()
        {
            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }
            _httpListener = null;
        }

        /// <summary>URL에서 토큰 추출 (fragment # 또는 query ? 모두 지원).</summary>
        public static (string accessToken, string refreshToken) ParseTokensFromUrl(string url)
        {
            // #fragment 또는 ?query 모두 파싱
            var data = "";
            if (url.Contains("#"))
                data = url.Substring(url.IndexOf('#') + 1);
            else if (url.Contains("?"))
                data = url.Substring(url.IndexOf('?') + 1);

            string accessToken = null, refreshToken = null;

            foreach (var pair in data.Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                switch (kv[0])
                {
                    case "access_token": accessToken = Uri.UnescapeDataString(kv[1]); break;
                    case "refresh_token": refreshToken = Uri.UnescapeDataString(kv[1]); break;
                }
            }

            return (accessToken, refreshToken);
        }

        public void Dispose()
        {
            Application.deepLinkActivated -= OnDeepLink;
            StopLocalServer();
        }
    }
}
