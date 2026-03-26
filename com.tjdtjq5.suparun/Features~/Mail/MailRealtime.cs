using System;
using Tjdtjq5.SupaRun.Supabase;

namespace Tjdtjq5.SupaRun
{
    public static partial class ServerAPI
    {
        public static partial class MailService
        {
            /// <summary>
            /// 새 우편 도착 실시간 감지.
            /// Subscribe() 후 mails 테이블에 INSERT 시 콜백.
            /// Unsubscribe()로 해제. 비용: ⚡1
            /// </summary>
            public static RealtimeChannel OnChange(string playerId, Action<Mail> callback)
            {
                var ch = SupaRun.Realtime.Channel($"mail-{playerId}");
                ch.OnPostgresChange<Mail>("mails", ChangeEvent.Insert, callback,
                    filter: $"playerid=eq.{playerId}");
                return ch;
            }
        }
    }
}
