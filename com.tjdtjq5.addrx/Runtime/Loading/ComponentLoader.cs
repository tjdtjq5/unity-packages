using System.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// Inspector에서 Addressable 주소를 지정하면
    /// Awake에서 자동 로드, OnDestroy에서 자동 해제하는 컴포넌트.
    /// </summary>
    public class ComponentLoader : MonoBehaviour
    {
        [AddressableRef]
        [SerializeField] string _address;

        SafeHandle<Object> _handle;
        Task _loadTask;

        /// <summary>로드된 에셋. 로드 전이면 null.</summary>
        public Object LoadedAsset => _handle is { IsReady: true }
            ? _handle.Value
            : null;

        /// <summary>로드 완료 여부.</summary>
        public bool IsLoaded => _handle is { IsReady: true };

        /// <summary>현재 상태.</summary>
        public HandleStatus Status => _handle?.Status ?? HandleStatus.None;

        /// <summary>로드 완료를 대기. 외부에서 await 가능.</summary>
        public Task WaitForLoad() => _loadTask ?? Task.CompletedTask;

        void Awake()
        {
            if (string.IsNullOrEmpty(_address)) return;
            _loadTask = LoadAsync();
        }

        async Task LoadAsync()
        {
            try
            {
                _handle = await AddrX.LoadAsync<Object>(_address);
            }
            catch (System.Exception e)
            {
                AddrXLog.Error("ComponentLoader",
                    $"로드 실패: {_address} ({e.Message})");
            }
        }

        void OnDestroy()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
