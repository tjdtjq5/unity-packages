using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tjdtjq5.SupaRun.Supabase
{
    // === Phoenix 메시지 (송수신 공통, vsn=1.0.0) ===

    [Serializable]
    public class PhoenixMessage
    {
        public string topic;
        [JsonProperty("event")]
        public string evt;
        public object payload;
        [JsonProperty("ref")]
        public string msgRef;
        public string join_ref;
    }

    // === Join 요청 ===

    [Serializable]
    public class JoinPayload
    {
        public JoinConfig config = new JoinConfig();
        public string access_token;
    }

    [Serializable]
    public class JoinConfig
    {
        public BroadcastConfig broadcast = new BroadcastConfig();
        public PresenceConfig presence = new PresenceConfig();
        public List<PgChangeConfig> postgres_changes = new List<PgChangeConfig>();
    }

    [Serializable]
    public class BroadcastConfig
    {
        public bool self;
        public bool ack;
    }

    [Serializable]
    public class PresenceConfig
    {
        public string key = "";
    }

    [Serializable]
    public class PgChangeConfig
    {
        [JsonProperty("event")]
        public string evt;
        public string schema = "public";
        public string table;
        public string filter;
    }

    // === 서버 응답 ===

    [Serializable]
    public class PhxReplyPayload
    {
        public string status;
        public object response;
    }

    // === Broadcast ===

    [Serializable]
    public class BroadcastPayload
    {
        public string type = "broadcast";
        [JsonProperty("event")]
        public string evt;
        public object payload;
    }

    // === Presence ===

    [Serializable]
    public class PresenceTrackPayload
    {
        public string type = "presence";
        [JsonProperty("event")]
        public string evt;
        public object payload;
    }

    public class PresenceUser
    {
        public string key;
        public Dictionary<string, object> metadata = new Dictionary<string, object>();
    }

    // === Postgres Changes ===

    [Serializable]
    public class PgChangeEventPayload
    {
        public PgChangeData data;
        public List<int> ids;
    }

    [Serializable]
    public class PgChangeData
    {
        public string schema;
        public string table;
        public string commit_timestamp;
        public string eventType;
        [JsonProperty("new")]
        public Dictionary<string, object> newRecord;
        public Dictionary<string, object> old;
    }

    // === Enums ===

    public enum ChangeEvent
    {
        Insert,
        Update,
        Delete,
        All
    }

    public enum ChannelState
    {
        Closed,
        Joining,
        Joined,
        Leaving
    }
}
