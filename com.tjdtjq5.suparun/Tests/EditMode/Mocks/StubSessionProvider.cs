#nullable enable
namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>테스트용 ISessionProvider — CurrentSession을 직접 세팅해 pull 동작을 검증.</summary>
    class StubSessionProvider : ISessionProvider
    {
        public AuthSession? CurrentSession { get; set; }
    }
}
