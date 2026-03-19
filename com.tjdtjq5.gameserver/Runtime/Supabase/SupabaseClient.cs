using System;

namespace Tjdtjq5.GameServer.Supabase
{
    /// <summary>Supabase 클라이언트. Auth/Realtime/Storage 접근점.</summary>
    public class SupabaseClient
    {
        public string Url { get; }
        public string AnonKey { get; }

        public SupabaseAuth Auth { get; }
        public SupabaseRealtime Realtime { get; }
        public SupabaseStorage Storage { get; }

        public SupabaseClient(string url, string anonKey)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            AnonKey = anonKey ?? throw new ArgumentNullException(nameof(anonKey));

            Auth = new SupabaseAuth(this);
            Realtime = new SupabaseRealtime(this);
            Storage = new SupabaseStorage(this);
        }
    }
}
