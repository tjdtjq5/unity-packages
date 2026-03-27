using System;
using Tjdtjq5.SupaRun.Supabase;

namespace Tjdtjq5.SupaRun
{
    public static partial class ServerAPI
    {
        public static partial class RankingService
        {
            /// <summary>
            /// 특정 보드의 순위 변동 실시간 감지.
            /// Subscribe() 후 rankingentries 변경 시 콜백.
            /// Unsubscribe()로 해제. 비용: ⚡2 (화면 열었을 때만 권장)
            /// </summary>
            public static RealtimeChannel OnChange(string boardId, Action<RankingEntry> callback)
            {
                var ch = SupaRun.Realtime.Channel($"ranking-{boardId}");
                ch.OnPostgresChange<RankingEntry>("ranking_entry", ChangeEvent.All, callback,
                    filter: $"boardid=eq.{boardId}");
                return ch;
            }
        }
    }
}
