#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tjdtjq5.AddrX.Debug
{
    /// <summary>모든 SafeHandle 생성/해제를 추적한다.</summary>
    public static class HandleTracker
    {
        static readonly Dictionary<int, HandleInfo> _active = new();
        static readonly List<HandleInfo> _activeList = new();
        static bool _listDirty = true;

        static int _totalLoaded;
        static int _totalReleased;

        /// <summary>현재 활성 핸들 목록 (읽기 전용).</summary>
        public static IReadOnlyList<HandleInfo> ActiveHandles
        {
            get
            {
                if (_listDirty)
                {
                    _activeList.Clear();
                    _activeList.AddRange(_active.Values);
                    _listDirty = false;
                }
                return _activeList;
            }
        }

        public static int ActiveCount => _active.Count;
        public static int TotalLoaded => _totalLoaded;
        public static int TotalReleased => _totalReleased;

        /// <summary>핸들 생성 시 발생.</summary>
        public static event Action<HandleInfo> OnHandleCreated;

        /// <summary>핸들 해제 시 발생.</summary>
        public static event Action<HandleInfo> OnHandleReleased;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            _active.Clear();
            _activeList.Clear();
            _listDirty = true;
            _totalLoaded = 0;
            _totalReleased = 0;
            OnHandleCreated = null;
            OnHandleReleased = null;

            HandleTracking.Created -= OnCreated;
            HandleTracking.Released -= OnReleased;
            HandleTracking.Created += OnCreated;
            HandleTracking.Released += OnReleased;
        }

        static void OnCreated(int id, string address, Type type, string stackTrace)
        {
            var info = new HandleInfo(id, address, type, Time.realtimeSinceStartup, stackTrace);
            _active[id] = info;
            _listDirty = true;
            _totalLoaded++;
            OnHandleCreated?.Invoke(info);
        }

        static void OnReleased(int id)
        {
            if (_active.TryGetValue(id, out var info))
            {
                _active.Remove(id);
                _listDirty = true;
                _totalReleased++;
                OnHandleReleased?.Invoke(info);
            }
        }

        /// <summary>특정 주소의 활성 핸들을 찾는다.</summary>
        public static HandleInfo? FindByAddress(string address)
        {
            foreach (var kvp in _active)
            {
                if (kvp.Value.Address == address)
                    return kvp.Value;
            }
            return null;
        }
    }
}
#endif
