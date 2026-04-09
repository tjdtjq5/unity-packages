#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class CallbackAuthRefresherTests
    {
        [Test]
        public async Task Callback_Returns_Session_Returns_True()
        {
            var session = new AuthSession { accessToken = "new-token" };
            var refresher = new CallbackAuthRefresher(() => Task.FromResult(session));

            Assert.IsTrue(await refresher.TryRefreshAsync());
        }

        [Test]
        public async Task Callback_Returns_Null_Returns_False()
        {
            var refresher = new CallbackAuthRefresher(() => Task.FromResult<AuthSession>(null!));

            Assert.IsFalse(await refresher.TryRefreshAsync());
        }

        [Test]
        public void Null_Callback_Throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CallbackAuthRefresher(null!));
        }
    }
}
