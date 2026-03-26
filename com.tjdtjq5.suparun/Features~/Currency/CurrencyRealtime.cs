using System;
using Tjdtjq5.SupaRun.Supabase;

namespace Tjdtjq5.SupaRun
{
    public static partial class ServerAPI
    {
        public static partial class CurrencyService
        {
            /// <summary>
            /// 재화 잔액 변동 실시간 감지.
            /// Subscribe() 후 currencybalances 변경 시 콜백.
            /// Unsubscribe()로 해제. 비용: ⚡1
            /// </summary>
            public static RealtimeChannel OnChange(string playerId, Action<CurrencyBalance> callback)
            {
                var ch = SupaRun.Realtime.Channel($"currency-{playerId}");
                ch.OnPostgresChange<CurrencyBalance>("currencybalances", ChangeEvent.All, callback,
                    filter: $"playerid=eq.{playerId}");
                return ch;
            }
        }
    }
}
