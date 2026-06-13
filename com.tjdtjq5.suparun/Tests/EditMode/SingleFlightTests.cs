#nullable enable
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>SingleFlight — 동시 호출 dedup + 동기 완료 안전(이전 EnsureLoggedIn NRE 회귀 방지).</summary>
    class SingleFlightTests
    {
        // ── void SingleFlight ──

        [Test]
        public async Task Run_Completes_When_Work_Is_Synchronous()
        {
            // work가 동기 완료(CompletedTask)해도 NRE 없이 완료된 Task를 반환해야 한다.
            // (이전 SupaRunAuth.EnsureLoggedIn이 launch 후 _loginUcs *필드*를 다시 읽다가 NRE 나던 회귀)
            var flight = new SingleFlight();
            int calls = 0;

            await flight.Run(() => { calls++; return UniTask.CompletedTask; });

            Assert.AreEqual(1, calls);
        }

        [Test]
        public async Task Run_Dedups_Concurrent_Calls()
        {
            var flight = new SingleFlight();
            var gate = new UniTaskCompletionSource();
            int calls = 0;

            async UniTask Work() { calls++; await gate.Task; }

            var t1 = flight.Run(Work);   // 진입 후 gate 대기 (in-flight)
            var t2 = flight.Run(Work);   // 합류 — Work 재실행 없음
            gate.TrySetResult();
            await UniTask.WhenAll(t1, t2);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public async Task Run_Reruns_After_Completion()
        {
            var flight = new SingleFlight();
            int calls = 0;

            await flight.Run(() => { calls++; return UniTask.CompletedTask; });
            await flight.Run(() => { calls++; return UniTask.CompletedTask; });

            Assert.AreEqual(2, calls);   // 완료 후 새 호출은 다시 실행
        }

        [Test]
        public void Run_Propagates_Exception_To_All_Awaiters()
        {
            var flight = new SingleFlight();
            var gate = new UniTaskCompletionSource();

            async UniTask Work() { await gate.Task; throw new InvalidOperationException("boom"); }

            var t1 = flight.Run(Work);
            var t2 = flight.Run(Work);
            gate.TrySetResult();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await t1);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await t2);
        }

        [Test]
        public async Task Run_Recovers_After_Exception()
        {
            var flight = new SingleFlight();

            try { await flight.Run(() => throw new InvalidOperationException()); }
            catch (InvalidOperationException) { }

            // 예외 후에도 _inflight가 리셋되어 다음 호출이 정상 동작
            int calls = 0;
            await flight.Run(() => { calls++; return UniTask.CompletedTask; });
            Assert.AreEqual(1, calls);
        }

        // ── SingleFlight<T> ──

        [Test]
        public async Task Generic_Concurrent_Callers_Share_Result()
        {
            var flight = new SingleFlight<int>();
            var gate = new UniTaskCompletionSource();
            int calls = 0;

            async UniTask<int> Work() { calls++; await gate.Task; return 42; }

            var t1 = flight.Run(Work);
            var t2 = flight.Run(Work);
            gate.TrySetResult();
            var (r1, r2) = await UniTask.WhenAll(t1, t2);

            Assert.AreEqual(1, calls);
            Assert.AreEqual(42, r1);
            Assert.AreEqual(42, r2);
        }
    }
}
