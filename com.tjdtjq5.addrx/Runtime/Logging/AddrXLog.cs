using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>AddrX 통합 로그 시스템. 태그 + 레벨 기반 필터링.</summary>
    public static class AddrXLog
    {
        const string Prefix = "[AddrX]";

        static LogLevel _level = LogLevel.Info;

        /// <summary>현재 로그 레벨. 이 레벨 미만의 로그는 무시된다.</summary>
        public static LogLevel Level
        {
            get => _level;
            set => _level = value;
        }

        public static void Verbose(string tag, string message)
        {
            if (_level <= LogLevel.Verbose)
                Debug.Log($"{Prefix}[{tag}] {message}");
        }

        public static void Info(string tag, string message)
        {
            if (_level <= LogLevel.Info)
                Debug.Log($"{Prefix}[{tag}] {message}");
        }

        public static void Warning(string tag, string message)
        {
            if (_level <= LogLevel.Warning)
                Debug.LogWarning($"{Prefix}[{tag}] {message}");
        }

        public static void Error(string tag, string message)
        {
            if (_level <= LogLevel.Error)
                Debug.LogError($"{Prefix}[{tag}] {message}");
        }
    }
}
