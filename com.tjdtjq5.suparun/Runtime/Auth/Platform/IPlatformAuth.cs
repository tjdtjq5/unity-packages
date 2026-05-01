using System.Threading;
using Cysharp.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>플랫폼 네이티브 인증 인터페이스. GPGS, Game Center 등.</summary>
    public interface IPlatformAuth
    {
        AuthProvider Provider { get; }
        bool IsAvailable { get; }
        UniTask<string> GetToken(CancellationToken ct = default);
    }
}
