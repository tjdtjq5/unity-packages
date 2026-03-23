using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Tjdtjq5.GameServer
{
    /// <summary>Apple Game Center 로그인.</summary>
    public class GameCenterAuthHandler : IPlatformAuth
    {
        public AuthProvider Provider => AuthProvider.GameCenter;

        public bool IsAvailable =>
#if UNITY_IOS
            true;
#else
            false;
#endif

        public Task<string> GetToken()
        {
#if UNITY_IOS
            var tcs = new TaskCompletionSource<string>();

            Social.localUser.Authenticate(success =>
            {
                if (success)
                {
                    var identity = JsonConvert.SerializeObject(new
                    {
                        playerId = Social.localUser.id,
                        alias = Social.localUser.userName
                    });
                    tcs.TrySetResult(identity);
                }
                else
                {
                    Debug.LogWarning("[GameServer:Auth] Game Center 로그인 실패");
                    tcs.TrySetResult(null);
                }
            });

            return tcs.Task;
#else
            Debug.LogWarning("[GameServer:Auth] Game Center는 iOS에서만 지원됩니다.");
            return Task.FromResult<string>(null);
#endif
        }
    }
}
