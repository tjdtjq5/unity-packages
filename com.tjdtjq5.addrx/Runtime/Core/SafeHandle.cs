using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// 로드/인스턴스 핸들의 공통 추상 베이스. 자동 Release를 보장한다.
    /// using 블록 또는 BindTo로 수명을 관리한다.
    /// 구현체: <see cref="AssetHandle{T}"/>(Addressables 로드), <see cref="InstanceHandle{T}"/>(인스턴스/커스텀 해제).
    /// </summary>
    public abstract class SafeHandle<T> : IDisposable, IAsyncDisposable
    {
        protected const string Tag = "SafeHandle";

        protected bool _released;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        protected readonly int _trackingId;
        protected readonly string _debugKey;
        protected readonly string _debugStackTrace;
#endif

        protected SafeHandle(object key)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugKey = key?.ToString();
            if (AddrXSettings.Instance.EnableTracking)
            {
                _trackingId = HandleTracking.NextId();
                _debugStackTrace = Environment.StackTrace;
                HandleTracking.NotifyCreated(_trackingId, _debugKey, typeof(T));
            }
#endif
        }

        /// <summary>로드된 에셋/인스턴스. 해제됨/미완료 시 예외.</summary>
        public abstract T Value { get; }

        /// <summary>핸들 유효 여부 (해제되지 않았고 원본이 유효한 경우 true).</summary>
        public abstract bool IsValid { get; }

        /// <summary>로드 완료 및 성공 상태. Value 접근 안전 보장.</summary>
        public abstract bool IsReady { get; }

        /// <summary>로딩 진행률 (0~1).</summary>
        public abstract float Progress { get; }

        /// <summary>현재 로드 상태.</summary>
        public abstract HandleStatus Status { get; }

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

            ReleaseCore();

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

        /// <summary>서브클래스별 실제 해제 로직. Dispose에서 1회만 호출된다.</summary>
        protected abstract void ReleaseCore();
    }

    /// <summary>
    /// Addressables <see cref="AsyncOperationHandle{T}"/>를 감싼 에셋 로드 핸들.
    /// Dispose 시 Addressables.Release로 핸들을 해제한다.
    /// </summary>
    public sealed class AssetHandle<T> : SafeHandle<T>
    {
        readonly AsyncOperationHandle<T> _handle;

        internal AssetHandle(AsyncOperationHandle<T> handle, object key = null) : base(key)
        {
            _handle = handle;
        }

        public override T Value
        {
            get
            {
                if (_released)
                    throw new ObjectDisposedException(
                        nameof(AssetHandle<T>), "핸들이 이미 해제되었습니다.");
                if (!_handle.IsValid())
                    throw new InvalidOperationException("핸들이 유효하지 않습니다.");
                if (_handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(
                        $"에셋이 아직 로드되지 않았습니다. 상태: {Status}");
                return _handle.Result;
            }
        }

        public override bool IsValid => !_released && _handle.IsValid();

        public override bool IsReady => IsValid && _handle.Status == AsyncOperationStatus.Succeeded;

        public override float Progress => _handle.IsValid() ? _handle.PercentComplete : 0f;

        public override HandleStatus Status
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

        protected override void ReleaseCore()
        {
            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
                AddrXLog.Verbose(Tag, $"핸들 해제: {typeof(T).Name}");
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ~AssetHandle()
        {
            if (!_released && _handle.IsValid())
            {
                var keyInfo = string.IsNullOrEmpty(_debugKey) ? "" : $"\n  Key: {_debugKey}";
                var stackInfo = string.IsNullOrEmpty(_debugStackTrace) ? "" : $"\n  할당 위치:\n{_debugStackTrace}";
                Debug.LogWarning(
                    $"[AddrX] SafeHandle<{typeof(T).Name}>이 Dispose 없이 GC 수집됨. " +
                    $"using 블록이나 BindTo()를 사용하세요.{keyInfo}{stackInfo}");
            }
        }
#endif
    }
}
