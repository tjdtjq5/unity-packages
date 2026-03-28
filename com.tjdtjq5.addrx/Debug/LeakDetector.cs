#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tjdtjq5.AddrX.Debug
{
    /// <summary>씬 전환 시 미해제 핸들을 감지하고 경고한다.</summary>
    public static class LeakDetector
    {
        static bool _autoCheck;


        /// <summary>씬 전환 시 자동 누수 체크 활성화 여부.</summary>
        public static bool AutoCheckOnSceneChange
        {
            get => _autoCheck;
            set
            {
                if (_autoCheck == value) return;
                _autoCheck = value;
                if (value)
                    SceneManager.sceneUnloaded += OnSceneUnloaded;
                else
                    SceneManager.sceneUnloaded -= OnSceneUnloaded;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _autoCheck = false;

            if (AddrXSettings.Instance.EnableLeakDetection)
                AutoCheckOnSceneChange = true;
        }

        static void OnSceneUnloaded(Scene scene)
        {
            var report = CheckForLeaks();
            if (report.LeakCount == 0) return;

            AddrXLog.Warning("LeakDetector",
                $"씬 '{scene.name}' 언로드 시 {report.LeakCount}개 미해제 핸들 감지");

            foreach (var leak in report.Leaks)
            {
                var msg = $"  [{leak.Id}] {leak.Address} ({leak.AssetType?.Name}, {leak.Age:F1}초 전)";
                if (!string.IsNullOrEmpty(leak.StackTrace))
                    msg += $"\n    할당 위치:\n{leak.StackTrace}";
                AddrXLog.Warning("LeakDetector", msg);
            }
        }

        /// <summary>현재 활성 핸들 기준 누수 리포트를 생성한다.</summary>
        public static LeakReport CheckForLeaks()
        {
            var handles = HandleTracker.ActiveHandles;
            var snapshot = new List<HandleInfo>(handles);
            return new LeakReport(snapshot);
        }
    }

    /// <summary>누수 체크 결과.</summary>
    public readonly struct LeakReport
    {
        readonly List<HandleInfo> _leaks;

        internal LeakReport(List<HandleInfo> leaks)
        {
            _leaks = leaks;
        }

        public int LeakCount => _leaks?.Count ?? 0;
        public IReadOnlyList<HandleInfo> Leaks => _leaks;
    }
}
#endif
