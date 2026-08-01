using NUnit.Framework;
using Tjdtjq5.SupaRun.Editor;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>
    /// `BridgeDeployRoutes.BlockedReason`(순수 결정 함수) — 자동 설정을 못 누르는 이유 판정.
    ///
    /// 옛 `GcpSetupUI.GetPhase` 테스트를 이어받은 것이다. 카드가 어드민 체크리스트로 옮겨가면서
    /// 판정도 옮겨갔지만, **원시값만 받는 순수 함수**라는 성질은 그대로 지켰다.
    ///
    /// 순서가 곧 사람이 밟는 순서다 — 위에서부터 막힌 첫 이유 하나만 나와야 한다.
    /// </summary>
    class DeployBlockedReasonTests
    {
        /// <summary>전부 갖춰진 상태. 각 테스트가 하나씩만 무너뜨린다.</summary>
        static string Reason(
            bool gcloud = true, bool gh = true,
            string project = "proj", string repo = "MyGame-server",
            string service = "mygame-server", bool billing = true)
            => BridgeDeployRoutes.BlockedReason(gcloud, gh, project, repo, service, billing);

        [Test]
        public void Null_When_All_Set() => Assert.IsNull(Reason());

        [Test]
        public void Blocks_On_Gcloud_Login()
            => StringAssert.Contains("gcloud", Reason(gcloud: false));

        [Test]
        public void Blocks_On_Gh_Login()
            => StringAssert.Contains("gh", Reason(gh: false));

        [Test]
        public void Blocks_On_Empty_Project()
            => StringAssert.Contains("GCP 프로젝트", Reason(project: ""));

        [Test]
        public void Blocks_On_Empty_Repo()
            => StringAssert.Contains("GitHub 레포", Reason(repo: ""));

        [Test]
        public void Blocks_On_Empty_ServiceName()
            => StringAssert.Contains("서비스명", Reason(service: ""));

        [Test]
        public void Blocks_On_Billing()
            => StringAssert.Contains("결제", Reason(billing: false));

        /// <summary>
        /// 여러 개가 동시에 막혀 있어도 **가장 앞의 것 하나**만 말해야 한다.
        /// 한 번에 여러 이유를 늘어놓으면 무엇부터 해야 할지 알 수 없다.
        /// </summary>
        [Test]
        public void Reports_Only_The_First_Blocker()
            => StringAssert.Contains("gcloud", Reason(gcloud: false, project: "", billing: false));
    }
}
