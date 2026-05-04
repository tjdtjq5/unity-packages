#if UNITY_EDITOR
using System.Collections.Generic;
using Tjdtjq5.Codemagic.Editor.Git;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup
{
    /// <summary>Step 1 사전 체크 — Unity 6+ / Git 저장소 / GitHub remote 확인.</summary>
    public static class PreflightChecker
    {
        /// <summary>한 항목의 검증 결과.</summary>
        public sealed class CheckItem
        {
            public string Name;
            public bool Passed;
            public string Detail;
        }

        /// <summary>모든 항목 검증 결과 리스트.</summary>
        public static List<CheckItem> RunAll()
        {
            var items = new List<CheckItem>
            {
                CheckUnityVersion(),
                CheckGitRepo(),
                CheckGitHubRemote(),
            };
            return items;
        }

        /// <summary>모두 통과했는지.</summary>
        public static bool AllPassed(IList<CheckItem> items)
        {
            if (items == null) return false;
            foreach (var i in items) if (!i.Passed) return false;
            return true;
        }

        // ── 개별 체크 ──

        static CheckItem CheckUnityVersion()
        {
            var version = Application.unityVersion;  // 예: "6000.3.10f1"
            var passed = version.StartsWith("6") || version.StartsWith("2024") || version.StartsWith("2025") || version.StartsWith("2026");
            return new CheckItem
            {
                Name = "Unity 6+",
                Passed = passed,
                Detail = passed ? version : $"Unity 6 이상 필요 (현재 {version})",
            };
        }

        static CheckItem CheckGitRepo()
        {
            var root = GitHelpers.GetRepoRoot();
            var passed = !string.IsNullOrEmpty(root);
            return new CheckItem
            {
                Name = "Git 저장소",
                Passed = passed,
                Detail = passed ? root : "현재 폴더가 git 저장소가 아닙니다. `git init` 후 다시 시도하세요.",
            };
        }

        static CheckItem CheckGitHubRemote()
        {
            var repo = GitHelpers.GetGitHubRepo();
            var passed = !string.IsNullOrEmpty(repo);
            return new CheckItem
            {
                Name = "GitHub remote",
                Passed = passed,
                Detail = passed ? repo : "GitHub remote가 origin에 연결되어 있지 않습니다. Codemagic은 GitHub 통합으로 동작합니다.",
            };
        }
    }
}
#endif
