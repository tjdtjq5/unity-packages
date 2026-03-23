using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.GameServer
{
    /// <summary>Google Play Games 로그인. Reflection으로 SDK 감지.</summary>
    public class GPGSAuthHandler : IPlatformAuth
    {
        public AuthProvider Provider => AuthProvider.GPGS;
        public bool IsAvailable => Application.platform == RuntimePlatform.Android && GetPlatformType() != null;

        static Type GetPlatformType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "Google.Play.Games")
                    return asm.GetType("GooglePlayGames.PlayGamesPlatform");
            }
            return null;
        }

        public async Task<string> GetToken()
        {
            var platformType = GetPlatformType();
            if (platformType == null)
            {
                Debug.LogWarning("[GameServer:Auth] GPGS SDK가 설치되지 않았습니다.");
                return null;
            }

            try
            {
                // PlayGamesPlatform.Activate()
                var activateMethod = platformType.GetMethod("Activate", BindingFlags.Public | BindingFlags.Static);
                activateMethod?.Invoke(null, null);

                // PlayGamesPlatform.Instance
                var instanceProp = platformType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instanceProp?.GetValue(null);
                if (instance == null)
                {
                    Debug.LogWarning("[GameServer:Auth] GPGS Instance null");
                    return null;
                }

                // Authenticate
                var loginTcs = new TaskCompletionSource<bool>();
                var signInStatusType = platformType.Assembly.GetType("GooglePlayGames.BasicApi.SignInStatus");

                if (signInStatusType != null)
                {
                    // 새 API: Authenticate(Action<SignInStatus>)
                    var callbackType = typeof(Action<>).MakeGenericType(signInStatusType);
                    var authMethod = platformType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "Authenticate"
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType == callbackType);

                    if (authMethod != null)
                    {
                        var successValue = Enum.Parse(signInStatusType, "Success");

                        // Action<SignInStatus> 콜백: status == Success → true
                        var callback = CreateSignInCallback(signInStatusType, successValue, loginTcs);
                        authMethod.Invoke(instance, new object[] { callback });
                    }
                    else
                    {
                        Debug.LogWarning("[GameServer:Auth] GPGS Authenticate(Action<SignInStatus>) 메서드를 찾을 수 없습니다.");
                        return null;
                    }
                }
                else
                {
                    // 구 API: Authenticate(Action<bool>)
                    var authMethod = platformType.GetMethod("Authenticate", new[] { typeof(Action<bool>) });
                    if (authMethod != null)
                    {
                        authMethod.Invoke(instance, new object[] { new Action<bool>(success => loginTcs.TrySetResult(success)) });
                    }
                    else
                    {
                        Debug.LogWarning("[GameServer:Auth] GPGS Authenticate 메서드를 찾을 수 없습니다.");
                        return null;
                    }
                }

                var loggedIn = await loginTcs.Task;
                if (!loggedIn)
                {
                    Debug.LogWarning("[GameServer:Auth] GPGS 로그인 실패");
                    return null;
                }

                // RequestServerSideAccess
                var codeTcs = new TaskCompletionSource<string>();
                var requestMethod = platformType.GetMethod("RequestServerSideAccess",
                    new[] { typeof(bool), typeof(Action<string>) });

                if (requestMethod != null)
                {
                    requestMethod.Invoke(instance, new object[] { false, new Action<string>(code => codeTcs.TrySetResult(code)) });
                }
                else
                {
                    Debug.LogWarning("[GameServer:Auth] RequestServerSideAccess 메서드를 찾을 수 없습니다.");
                    return null;
                }

                var authCode = await codeTcs.Task;
                if (string.IsNullOrEmpty(authCode))
                    Debug.LogWarning("[GameServer:Auth] GPGS Server Auth Code 획득 실패");

                return authCode;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameServer:Auth] GPGS 에러: {ex.Message}");
                return null;
            }
        }

        /// <summary>Reflection으로 Action&lt;SignInStatus&gt; 콜백 생성. status == Success → loginTcs.SetResult(true)</summary>
        static Delegate CreateSignInCallback(Type signInStatusType, object successValue, TaskCompletionSource<bool> loginTcs)
        {
            // Action<object>로 래핑 후 변환
            var helperMethod = typeof(GPGSAuthHandler)
                .GetMethod(nameof(MakeTypedCallback), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(signInStatusType);

            return (Delegate)helperMethod.Invoke(null, new object[] { successValue, loginTcs });
        }

        static Action<T> MakeTypedCallback<T>(object successValue, TaskCompletionSource<bool> loginTcs)
        {
            var success = (T)successValue;
            return status =>
            {
                Debug.Log($"[GameServer:Auth] GPGS Authenticate 결과: {status}");
                loginTcs.TrySetResult(status.Equals(success));
            };
        }
    }
}
