using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// AsyncOperationHandle&lt;T&gt;를 감싸서 자동 Release를 보장하는 래퍼.
    /// using 블록 또는 BindTo로 수명을 관리한다.
    /// </summary>
    public sealed class SafeHandle<T> : IDisposable, IAsyncDisposable
    {
        const string Tag = "SafeHandle";

        readonly AsyncOperationHandle<T> _handle;
        bool _released;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly int _trackingId;
#endif

        internal SafeHandle(AsyncOperationHandle<T> handle, object key = null)
        {
            _handle = handle;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (AddrXSettings.Instance.EnableTracking)
            {
                _trackingId = HandleTracking.NextId();
                HandleTracking.NotifyCreated(_trackingId, key?.ToString(), typeof(T));
            }
#endif
        }

        /// <summary>로드된 에셋. 해제됨/미완료 시 예외.</summary>
        public T Value
        {
            get
            {
                if (_released)
                    throw new ObjectDisposedException(
                        nameof(SafeHandle<T>), "핸들이 이미 해제되었습니다.");
                if (!_handle.IsValid())
                    throw new InvalidOperationException("핸들이 유효하지 않습니다.");
                if (_handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(
                        $"에셋이 아직 로드되지 않았습니다. 상태: {Status}");
                return _handle.Result;
            }
        }

        /// <summary>핸들 유효 여부 (해제되지 않았고 원본이 유효한 경우 true).</summary>
        public bool IsValid => !_released && _handle.IsValid();

        /// <summary>에셋 로드 완료 및 성공 상태. Value 접근 안전 보장.</summary>
        public bool IsReady => IsValid && _handle.Status == AsyncOperationStatus.Succeeded;

        /// <summary>로딩 진행률 (0~1).</summary>
        public float Progress => _handle.IsValid() ? _handle.PercentComplete : 0f;

        /// <summary>현재 로드 상태.</summary>
        public HandleStatus Status
        {
            get
            {
                if (_released || !_handle.IsValid()) return HandleStatus.None;
                return _handle.Status switch
                {
                    AsyncOperationStatus.None => HandleStatus.Loading,
                    AsyncOperationStatus.Succeeded => HandleStatus.Succeeded,
                    AsyncOperationStatus.Failed => HandleStatus.Failed,
                    _ => HandleStatus.None
                };
            }
        }

        /// <summary>GO 파괴 시 자동 Dispose. 체이닝 반환.</summary>
        public SafeHandle<T> BindTo(GameObject go)
        {
            if (go == null)
                throw new ArgumentNullException(nameof(go));
            HandleReleaser.Bind(go, this);
            return this;
        }

        public void Dispose()
        {
            if (_released) return;
            _released = true;

            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
                AddrXLog.Verbose(Tag, $"핸들 해제: {typeof(T).Name}");
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_trackingId > 0)
                HandleTracking.NotifyReleased(_trackingId);
#endif

            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ~SafeHandle()
        {
            if (!_released && _handle.IsValid())
            {
                Debug.LogWarning(
                    $"[AddrX] SafeHandle<{typeof(T).Name}>이 Dispose 없이 GC 수집됨. " +
                    "using 블록이나 BindTo()를 사용하세요.");
            }
        }
#endif
    }
}
