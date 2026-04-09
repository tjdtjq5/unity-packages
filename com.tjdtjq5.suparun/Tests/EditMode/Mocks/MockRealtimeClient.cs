#nullable enable
using System.Collections.Generic;
using Tjdtjq5.SupaRun.Supabase;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>테스트용 Realtime 클라이언트. SetAccessToken 호출을 기록.</summary>
    class MockRealtimeClient : IRealtimeClient
    {
        public bool IsConnected => false;
        public List<string> AccessTokensSet { get; } = new();
        public string? LastAccessToken => AccessTokensSet.Count > 0 ? AccessTokensSet[^1] : null;
        public bool DisconnectCalled { get; private set; }

        public void SetAccessToken(string token) => AccessTokensSet.Add(token);

        public RealtimeChannel Channel(string name)
            => throw new System.NotImplementedException("MockRealtimeClient.Channel는 테스트 미구현");

        public void Disconnect() => DisconnectCalled = true;
    }
}
