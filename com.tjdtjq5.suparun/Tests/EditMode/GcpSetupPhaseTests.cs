using NUnit.Framework;
using Tjdtjq5.SupaRun.Editor;
using PC = Tjdtjq5.SupaRun.Editor.PrerequisiteChecker;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>GcpSetupUI.GetPhase(순수 결정 함수) — GCP 설정 진행 단계 판정 검증.</summary>
    class GcpSetupPhaseTests
    {
        static PC.ToolStatus Gcloud(bool installed, bool loggedIn)
            => new PC.ToolStatus { Installed = installed, LoggedIn = loggedIn };

        [Test]
        public void NoCli_When_Not_Installed()
            => Assert.AreEqual(GcpSetupUI.Phase.NoCli,
                GcpSetupUI.GetPhase(Gcloud(false, false), "proj", true, "sa@x.iam"));

        [Test]
        public void NotLoggedIn_When_Installed_But_Not_LoggedIn()
            => Assert.AreEqual(GcpSetupUI.Phase.NotLoggedIn,
                GcpSetupUI.GetPhase(Gcloud(true, false), "proj", true, "sa@x.iam"));

        [Test]
        public void NoProject_When_ProjectId_Empty()
            => Assert.AreEqual(GcpSetupUI.Phase.NoProject,
                GcpSetupUI.GetPhase(Gcloud(true, true), "", true, "sa@x.iam"));

        [Test]
        public void NoApi_When_Api_Disabled()
            => Assert.AreEqual(GcpSetupUI.Phase.NoApi,
                GcpSetupUI.GetPhase(Gcloud(true, true), "proj", false, "sa@x.iam"));

        [Test]
        public void NoApi_When_ServiceAccount_Empty()
            => Assert.AreEqual(GcpSetupUI.Phase.NoApi,
                GcpSetupUI.GetPhase(Gcloud(true, true), "proj", true, ""));

        [Test]
        public void Complete_When_All_Set()
            => Assert.AreEqual(GcpSetupUI.Phase.Complete,
                GcpSetupUI.GetPhase(Gcloud(true, true), "proj", true, "sa@x.iam"));
    }
}
