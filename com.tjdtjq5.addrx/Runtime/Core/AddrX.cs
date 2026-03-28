using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    /// <summary>AddrX 진입점. 초기화 + 에셋 로드 API.</summary>
    public static partial class AddrX
    {
        const string Tag = "AddrX";

        static bool _initialized;
        static Task _initTask;

        /// <summary>초기화 완료 여부.</summary>
        public static bool IsInitialized => _initialized;

#if UNITY_EDITOR
        // Enter Play Mode Settings (도메인 리로드 비활성) 대응
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _initialized = false;
            _initTask = null;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoInitialize()
        {
            if (!AddrXSettings.Instance.AutoInitialize) return;
            _initTask = InitializeCore();
        }

        /// <summary>수동 초기화. 이미 초기화됐으면 즉시 반환.</summary>
        public static Task Initialize()
        {
            if (_initialized) return Task.CompletedTask;
            _initTask ??= InitializeCore();
            return _initTask;
        }

        /// <summary>Addressable 에셋을 로드하고 SafeHandle로 감싸서 반환한다.</summary>
        public static async Task<SafeHandle<T>> LoadAsync<T>(object key)
        {
            await EnsureInitialized();

            var op = Addressables.LoadAssetAsync<T>(key);
            await op.Task;

            var handle = new SafeHandle<T>(op, key);

            if (op.Status == AsyncOperationStatus.Failed)
            {
                AddrXLog.Error(Tag,
                    $"에셋 로드 실패: {key} ({op.OperationException?.Message})");
            }
            else
            {
                AddrXLog.Verbose(Tag, $"에셋 로드 완료: {key} ({typeof(T).Name})");
            }

            return handle;
        }

        static async Task InitializeCore()
        {
            try
            {
                AddrXSettings.Instance.Apply();
                AddrXLog.Info(Tag, "Addressables 초기화 시작");

                var op = Addressables.InitializeAsync();
                await op.Task;

                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    _initialized = true;
                    AddrXLog.Info(Tag, "Addressables 초기화 완료");
                }
                else
                {
                    AddrXLog.Error(Tag, "Addressables 초기화 실패");
                    throw new Exception("Addressables 초기화에 실패했습니다.");
                }
            }
            catch (Exception e)
            {
                AddrXLog.Error(Tag, $"초기화 중 예외: {e.Message}");
                _initTask = null; // 재시도 허용
                throw;
            }
        }
    }
}
