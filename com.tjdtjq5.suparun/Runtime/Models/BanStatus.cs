#nullable enable
using System;

namespace Tjdtjq5.SupaRun
{
    [Serializable]
    public class BanStatus
    {
        public bool banned;
        public string? reason;
        public long bannedUntil; // 0 = 영구

        public bool IsPermanent => bannedUntil == 0 && banned;
    }
}
