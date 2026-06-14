using NUnit.Framework;
using Tjdtjq5.SupaRun.Editor;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>PrerequisiteChecker에서 추출한 ToolPathFinder(경로탐색 3중 중복 통합) 검증.</summary>
    class ToolPathFinderTests
    {
        [Test]
        public void Returns_Path_From_PATH_Lookup()
        {
            var path = ToolPathFinder.Find("dotnet", new[] { "/known/dotnet" },
                (exe, args) => (0, "/usr/bin/dotnet\n"),
                p => p == "/usr/bin/dotnet",
                isWindows: false);
            Assert.AreEqual("/usr/bin/dotnet", path);
        }

        [Test]
        public void Falls_Back_To_KnownPaths_When_PATH_Misses()
        {
            var path = ToolPathFinder.Find("gh", new[] { "/missing", "/opt/gh" },
                (exe, args) => (1, ""),                 // PATH miss
                p => p == "/opt/gh",
                isWindows: false);
            Assert.AreEqual("/opt/gh", path);
        }

        [Test]
        public void Falls_Back_When_PATH_Result_File_Missing()
        {
            var path = ToolPathFinder.Find("gcloud", new[] { "/opt/gcloud" },
                (exe, args) => (0, "/stale/gcloud\n"),  // PATH가 경로를 주지만 파일이 없음
                p => p == "/opt/gcloud",                // knownPath만 존재
                isWindows: false);
            Assert.AreEqual("/opt/gcloud", path);
        }

        [Test]
        public void Returns_Null_When_Nowhere_Found()
        {
            var path = ToolPathFinder.Find("dotnet", new[] { "/missing" },
                (exe, args) => (1, ""),
                p => false,
                isWindows: false);
            Assert.IsNull(path);
        }

        [Test]
        public void Uses_Where_On_Windows_CommandV_On_Unix()
        {
            string captured = null;
            ToolPathFinder.Find("gh", new string[0],
                (exe, args) => { captured = exe + " " + args; return (1, ""); },
                p => false, isWindows: true);
            Assert.AreEqual("cmd.exe /c where gh", captured);

            ToolPathFinder.Find("gh", new string[0],
                (exe, args) => { captured = exe + " " + args; return (1, ""); },
                p => false, isWindows: false);
            Assert.AreEqual("/bin/sh -lc \"command -v gh\"", captured);
        }
    }
}
