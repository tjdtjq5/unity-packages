using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
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
        // _initTask는 Task로 유지 — 다중 호출자가 같은 작업을 await하는 캐싱 패턴.
        // UniTask는 struct + 1회 await 제약이라 캐싱 불안전. 외부 API는 UniTask로 노출.
        static Task _initTask;
        static readonly object _initLock = new();
        static int _initFailCount;
        const int MaxInitAttempts = 3;

        /// <summary>초기화 완료 여부.</summary>
        public static bool IsInitialized => _initialized;

#if UNITY_EDITOR
        // Enter Play Mode Settings (도메인 리로드 비활성) 대응
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _initialized = false;
            _initTask = null;
            _initFailCount = 0;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInitialize()
        {
            if (!AddrXSettings.Instance.AutoInitialize) return;
            _initTask ??= InitializeCore().AsTask();
        }

        /// <summary>수동 초기화. 이미 초기화됐으면 즉시 반환.</summary>
        public static UniTask Initialize()
        {
            if (_initialized) return UniTask.CompletedTask;
            lock (_initLock)
            {
                if (_initialized) return UniTask.CompletedTask;
                _initTask ??= InitializeCore().AsTask();
                return _initTask.AsUniTask();
            }
        }

        /// <summary>Addressable 에셋을 로드하고 SafeHandle로 감싸서 반환한다.</summary>
        public static async UniTask<SafeHandle<T>> LoadAsync<T>(object key)
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

        static async UniTask InitializeCore()
        {
            if (_initFailCount >= MaxInitAttempts)
                throw new InvalidOperationException(
                    $"AddrX 초기화 최대 재시도 {MaxInitAttempts}회 초과");

            try
            {
                AddrXSettings.Instance.Apply();
                AddrXLog.Info(Tag, "Addressables 초기화 시작");

                // 1) 카탈로그가 이미 로드되었으면 즉시 완료
                if (HasResourceLocators())
                {
                    _initialized = true;
                    AddrXLog.Info(Tag, "Addressables 이미 초기화됨 (스킵)");
                    return;
                }

                // 2) 직접 초기화 시도
                //    Unity 자동 초기화가 이미 진행 중이면 invalid handle이 발생할 수 있음
                try
                {
                    var op = Addressables.InitializeAsync();
                    await op.Task;

                    if (op.Status == AsyncOperationStatus.Succeeded)
                    {
                        _initialized = true;
                        _initFailCount = 0;
                        AddrXLog.Info(Tag, "Addressables 초기화 완료");
                        return;
                    }
                }
                catch (Exception e)
                {
                    AddrXLog.Verbose(Tag,
                        $"InitializeAsync 경합 감지, 자동 초기화 완료 대기: {e.Message}");
                }

                // 3) 폴백: Unity 자동 초기화 완료 대기
                await WaitForResourceLocators();
                _initialized = true;
                _initFailCount = 0;
                AddrXLog.Info(Tag, "Addressables 초기화 완료 (자동 초기화 대기)");
            }
            catch (Exception e)
            {
                _initFailCount++;
                _initTask = null;
                AddrXLog.Error(Tag, $"초기화 실패 ({_initFailCount}/{MaxInitAttempts}): {e.Message}");
                throw;
            }
        }

        static bool HasResourceLocators()
        {
            return Addressables.ResourceLocators.GetEnumerator().MoveNext();
        }

        static async UniTask WaitForResourceLocators()
        {
            const int maxFrames = 300; // ~5초 @60fps
            for (int i = 0; i < maxFrames; i++)
            {
                if (HasResourceLocators()) return;
                await UniTask.Yield();
            }
            throw new TimeoutException(
                "Addressables 자동 초기화 대기 시간 초과 (300 frames)");
        }
    }
}
