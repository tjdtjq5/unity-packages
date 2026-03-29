using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>프로젝트별 EditorPrefs 접두사 유틸리티.</summary>
    public static class EditorPrefUtils
    {
        static string _prefix;

        /// <summary>Application.dataPath 해시 기반 프로젝트 고유 접두사.</summary>
        public static string ProjectPrefix =>
            _prefix ??= $"SupaRun_{(Application.dataPath.GetHashCode() & 0x7FFFFFFF):X8}_";
    }
}
