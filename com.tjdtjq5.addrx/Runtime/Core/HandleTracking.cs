#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>핸들 생성/해제 이벤트를 방송하는 정적 이벤트 버스. Debug/Editor에서 구독.</summary>
    public static class HandleTracking
    {
        static int _nextId;

        /// <summary>핸들 생성 시 (id, address, assetType, stackTrace).</summary>
        public static event Action<int, string, Type, string> Created;

        /// <summary>핸�� 해제 시 (id).</summary>
        public static event Action<int> Released;

        internal static int NextId() => ++_nextId;

        internal static void NotifyCreated(int id, string key, Type type)
        {
            var stack = AddrXSettings.Instance.EnableTracking
                ? Environment.StackTrace
                : null;
            Created?.Invoke(id, key, type, stack);
        }

        internal static void NotifyReleased(int id)
        {
            Released?.Invoke(id);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            _nextId = 0;
            Created = null;
            Released = null;
        }
    }
}
#endif
