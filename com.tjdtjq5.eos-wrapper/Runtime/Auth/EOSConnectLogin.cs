using System;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace Tjdtjq5.EOS.Auth
{
    /// <summary>
    /// EOS Connect login.
    /// Supports OpenID token (Supabase JWT) with DeviceId fallback.
    /// Plain class — VContainer DI, awaited from GameInitializer.
    /// </summary>
    public class EOSConnectLogin
    {
        public ProductUserId LocalUserId { get; private set; }
        public bool IsLoggedIn => LocalUserId != null && LocalUserId.IsValid();

        /// <summary>
        /// Login to EOS Connect.
        /// If openIdToken is provided, uses OpenID auth (e.g. Supabase JWT).
        /// Falls back to DeviceId if token is null or OpenID login fails.
        /// </summary>
        public async Awaitable<bool> LoginAsync(string openIdToken = null)
        {
            if (EOSManager.Instance == null)
            {
                Debug.LogError("[EOSConnectLogin] EOSManager.Instance is null");
                return false;
            }

            // OpenID token available — try Supabase JWT login
            if (!string.IsNullOrEmpty(openIdToken))
            {
                bool ok = await LoginWithOpenIdAsync(openIdToken);
                if (ok)
                {
                    Debug.Log($"[EOSConnectLogin] OpenID login success — ProductUserId: {LocalUserId}");
                    return true;
                }
                Debug.LogWarning("[EOSConnectLogin] OpenID login failed, falling back to DeviceId");
            }

            // Fallback: DeviceId (anonymous, same per device)
            return await LoginWithDeviceIdAsync();
        }

        // ── OpenID (Supabase JWT) ──

        async Awaitable<bool> LoginWithOpenIdAsync(string token)
        {
            var tcs = new TaskCompletionSource<bool>();

            EOSManager.Instance.StartConnectLoginWithOptions(
                ExternalCredentialType.OpenidAccessToken,
                token,
                onloginCallback: loginInfo =>
                {
                    if (loginInfo.ResultCode == Result.Success)
                    {
                        LocalUserId = EOSManager.Instance.GetProductUserId();
                        tcs.SetResult(true);
                    }
                    else if (loginInfo.ResultCode == Result.InvalidUser)
                    {
                        // First login for this Supabase user — create EOS ProductUser, then re-login
                        CreateUserThenReLogin(loginInfo.ContinuanceToken, token, tcs);
                    }
                    else
                    {
                        Debug.LogWarning($"[EOSConnectLogin] OpenID: {loginInfo.ResultCode}");
                        tcs.SetResult(false);
                    }
                });

            return await tcs.Task;
        }

        void CreateUserThenReLogin(ContinuanceToken continuanceToken, string originalToken,
            TaskCompletionSource<bool> tcs)
        {
            // EOSManager.CreateConnectUserWithContinuanceToken handles both:
            // 1. CreateUser (EOS ProductUser creation)
            // 2. SetLocalProductUserId (EOSManager internal registration)
            // No re-login needed.
            EOSManager.Instance.CreateConnectUserWithContinuanceToken(continuanceToken,
                info =>
                {
                    if (info.ResultCode == Result.Success)
                    {
                        LocalUserId = EOSManager.Instance.GetProductUserId();
                        Debug.Log($"[EOSConnectLogin] New ProductUser created: {LocalUserId}");
                        tcs.SetResult(true);
                    }
                    else
                    {
                        Debug.LogError($"[EOSConnectLogin] CreateUser failed: {info.ResultCode}");
                        tcs.SetResult(false);
                    }
                });
        }

        // ── DeviceId (fallback) ──

        async Awaitable<bool> LoginWithDeviceIdAsync()
        {
            bool deviceOk = await CreateDeviceIdAsync();
            if (!deviceOk)
            {
                Debug.LogError("[EOSConnectLogin] CreateDeviceId failed");
                return false;
            }

            bool loginOk = await ConnectLoginDeviceIdAsync();
            if (!loginOk)
            {
                Debug.LogError("[EOSConnectLogin] DeviceId login failed");
                return false;
            }

            Debug.Log($"[EOSConnectLogin] DeviceId login success — ProductUserId: {LocalUserId}");
            return true;
        }

        async Awaitable<bool> CreateDeviceIdAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            var connectInterface = EOSManager.Instance.GetEOSConnectInterface();
            var options = new CreateDeviceIdOptions
            {
                DeviceModel = SystemInfo.deviceModel
            };

            connectInterface.CreateDeviceId(ref options, null,
                (ref CreateDeviceIdCallbackInfo info) =>
                {
                    if (info.ResultCode == Result.Success ||
                        info.ResultCode == Result.DuplicateNotAllowed)
                    {
                        tcs.SetResult(true);
                    }
                    else
                    {
                        Debug.LogWarning($"[EOSConnectLogin] CreateDeviceId: {info.ResultCode}");
                        tcs.SetResult(false);
                    }
                });

            return await tcs.Task;
        }

        async Awaitable<bool> ConnectLoginDeviceIdAsync()
        {
            var tcs = new TaskCompletionSource<bool>();

            string displayName = Environment.UserName;
            EOSManager.Instance.StartConnectLoginWithDeviceToken(displayName,
                loginInfo =>
                {
                    if (loginInfo.ResultCode == Result.Success)
                    {
                        LocalUserId = EOSManager.Instance.GetProductUserId();
                        tcs.SetResult(true);
                    }
                    else
                    {
                        Debug.LogWarning($"[EOSConnectLogin] DeviceId login: {loginInfo.ResultCode}");
                        tcs.SetResult(false);
                    }
                });

            return await tcs.Task;
        }
    }
}
