using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Supabase
{
    /// <summary>
    /// Realtime 채널. Broadcast/Presence/PostgresChanges 기능 제공.
    /// 빌더 패턴: channel.OnBroadcast(...).OnPresenceSync(...).Subscribe()
    /// </summary>
    public class RealtimeChannel
    {
        readonly SupabaseRealtime _realtime;
        readonly string _name;
        readonly string _topic;

        ChannelState _state = ChannelState.Closed;
        string _joinRef;

        // 설정
        bool _broadcastSelf;
        bool _broadcastAck;
        string _presenceKey = "";

        // Broadcast 핸들러: eventName → callbacks
        readonly Dictionary<string, List<Action<Dictionary<string, object>>>> _broadcastHandlers
            = new Dictionary<string, List<Action<Dictionary<string, object>>>>();

        // Presence
        readonly Dictionary<string, PresenceUser> _presenceState = new Dictionary<string, PresenceUser>();
        Action<List<PresenceUser>> _onPresenceSync;
        Action<PresenceUser> _onPresenceJoin;
        Action<PresenceUser> _onPresenceLeave;

        // Postgres Changes: 등록 시점의 config + 핸들러
        readonly List<(PgChangeConfig config, Action<PgChangeData> handler)> _pgRegistrations
            = new List<(PgChangeConfig, Action<PgChangeData>)>();
        // 서버 할당 ID → 핸들러 인덱스
        readonly Dictionary<int, int> _pgIdMap = new Dictionary<int, int>();

        // Join 완료 대기
        TaskCompletionSource<bool> _joinTcs;

        public ChannelState State => _state;
        public string Name => _name;

        internal RealtimeChannel(SupabaseRealtime realtime, string name, string topic)
        {
            _realtime = realtime;
            _name = name;
            _topic = topic;
        }

        // === Broadcast ===

        /// <summary>Broadcast 이벤트 수신 콜백 등록.</summary>
        public RealtimeChannel OnBroadcast(string eventName, Action<Dictionary<string, object>> callback)
        {
            if (!_broadcastHandlers.TryGetValue(eventName, out var list))
            {
                list = new List<Action<Dictionary<string, object>>>();
                _broadcastHandlers[eventName] = list;
            }
            list.Add(callback);
            return this;
        }

        /// <summary>Broadcast 메시지 전송.</summary>
        public async Task SendBroadcast(string eventName, object payload)
        {
            if (_state != ChannelState.Joined)
            {
                Debug.LogWarning($"[Realtime:{_name}] 채널에 조인되지 않았습니다.");
                return;
            }

            await _realtime.Send(new PhoenixMessage
            {
                topic = _topic,
                evt = "broadcast",
                payload = new BroadcastPayload
                {
                    type = "broadcast",
                    evt = eventName,
                    payload = payload
                },
                msgRef = _realtime.MakeRef()
            });
        }

        /// <summary>자기 메시지도 수신할지 설정.</summary>
        public RealtimeChannel SetBroadcastSelf(bool self)
        {
            _broadcastSelf = self;
            return this;
        }

        /// <summary>서버 확인 응답을 받을지 설정.</summary>
        public RealtimeChannel SetBroadcastAck(bool ack)
        {
            _broadcastAck = ack;
            return this;
        }

        // === Presence ===

        /// <summary>Presence 추적 시작.</summary>
        public async Task TrackPresence(object metadata)
        {
            if (_state != ChannelState.Joined)
            {
                Debug.LogWarning($"[Realtime:{_name}] 채널에 조인되지 않았습니다.");
                return;
            }

            await _realtime.Send(new PhoenixMessage
            {
                topic = _topic,
                evt = "presence",
                payload = new PresenceTrackPayload
                {
                    type = "presence",
                    evt = "track",
                    payload = metadata
                },
                msgRef = _realtime.MakeRef()
            });
        }

        /// <summary>Presence 추적 중지.</summary>
        public async Task UntrackPresence()
        {
            await _realtime.Send(new PhoenixMessage
            {
                topic = _topic,
                evt = "presence",
                payload = new PresenceTrackPayload
                {
                    type = "presence",
                    evt = "untrack"
                },
                msgRef = _realtime.MakeRef()
            });
        }

        /// <summary>전체 접속자 목록 변경 시 콜백.</summary>
        public RealtimeChannel OnPresenceSync(Action<List<PresenceUser>> callback)
        {
            _onPresenceSync = callback;
            return this;
        }

        /// <summary>접속자 입장 콜백.</summary>
        public RealtimeChannel OnPresenceJoin(Action<PresenceUser> callback)
        {
            _onPresenceJoin = callback;
            return this;
        }

        /// <summary>접속자 퇴장 콜백.</summary>
        public RealtimeChannel OnPresenceLeave(Action<PresenceUser> callback)
        {
            _onPresenceLeave = callback;
            return this;
        }

        // === Postgres Changes ===

        /// <summary>DB 테이블 변경 감지 (raw).</summary>
        public RealtimeChannel OnPostgresChange(string table, ChangeEvent evt,
            Action<PgChangeData> callback, string filter = null)
        {
            _pgRegistrations.Add((new PgChangeConfig
            {
                evt = ChangeEventToString(evt),
                schema = "public",
                table = table,
                filter = filter
            }, callback));
            return this;
        }

        /// <summary>DB 테이블 변경 감지 (타입 안전). newRecord를 T로 역직렬화.</summary>
        public RealtimeChannel OnPostgresChange<T>(string table, ChangeEvent evt,
            Action<T> callback, string filter = null) where T : new()
        {
            OnPostgresChange(table, evt, data =>
            {
                if (data.newRecord == null) return;
                var json = JsonConvert.SerializeObject(data.newRecord);
                var obj = JsonConvert.DeserializeObject<T>(json);
                callback?.Invoke(obj);
            }, filter);
            return this;
        }

        // === Subscribe / Unsubscribe ===

        /// <summary>채널 구독 시작.</summary>
        public async Task Subscribe()
        {
            if (_state == ChannelState.Joined || _state == ChannelState.Joining)
                return;

            await _realtime.EnsureConnected();
            await Join();
        }

        /// <summary>채널 구독 해제.</summary>
        public async Task Unsubscribe()
        {
            if (_state == ChannelState.Closed) return;

            _state = ChannelState.Leaving;
            await _realtime.Send(new PhoenixMessage
            {
                topic = _topic,
                evt = "phx_leave",
                payload = new { },
                msgRef = _realtime.MakeRef()
            });

            _state = ChannelState.Closed;
            _realtime.RemoveChannel(_topic);
            Debug.Log($"[Realtime:{_name}] 채널 떠남");
        }

        /// <summary>재연결 시 자동 재구독.</summary>
        internal async Task Rejoin()
        {
            if (_state == ChannelState.Closed) return;
            _state = ChannelState.Closed;
            await Join();
        }

        /// <summary>액세스 토큰 갱신.</summary>
        internal async void PushAccessToken(string token)
        {
            if (_state != ChannelState.Joined) return;
            await _realtime.Send(new PhoenixMessage
            {
                topic = _topic,
                evt = "access_token",
                payload = new { access_token = token },
                msgRef = _realtime.MakeRef()
            });
        }

        // === 내부: Join ===

        async Task Join()
        {
            _state = ChannelState.Joining;
            _joinRef = _realtime.MakeRef();

            var joinPayload = new JoinPayload
            {
                access_token = _realtime.AccessToken,
                config = new JoinConfig
                {
                    broadcast = new BroadcastConfig { self = _broadcastSelf, ack = _broadcastAck },
                    presence = new PresenceConfig { key = _presenceKey },
                    postgres_changes = _pgRegistrations.Select(r => r.config).ToList()
                }
            };

            _joinTcs = new TaskCompletionSource<bool>();

            await _realtime.Send(new PhoenixMessage
            {
                topic = _topic,
                evt = "phx_join",
                payload = joinPayload,
                msgRef = _joinRef,
                join_ref = _joinRef
            });

            // join reply 대기 (10초 타임아웃)
            var timeout = Task.Delay(10000);
            var completed = await Task.WhenAny(_joinTcs.Task, timeout);

            if (completed == timeout)
            {
                _state = ChannelState.Closed;
                Debug.LogWarning($"[Realtime:{_name}] Join 타임아웃");
            }
        }

        // === 내부: 메시지 처리 ===

        internal void HandleMessage(PhoenixMessage msg)
        {
            switch (msg.evt)
            {
                case "phx_reply":
                    HandleReply(msg);
                    break;
                case "broadcast":
                    HandleBroadcast(msg);
                    break;
                case "presence_state":
                    HandlePresenceState(msg);
                    break;
                case "presence_diff":
                    HandlePresenceDiff(msg);
                    break;
                case "postgres_changes":
                    HandlePostgresChanges(msg);
                    break;
                case "system":
                    // 시스템 메시지 (구독 확인 등)
                    break;
                case "phx_error":
                    Debug.LogWarning($"[Realtime:{_name}] 에러: {msg.payload}");
                    _state = ChannelState.Closed;
                    break;
                case "phx_close":
                    _state = ChannelState.Closed;
                    break;
            }
        }

        void HandleReply(PhoenixMessage msg)
        {
            if (msg.msgRef != _joinRef) return;

            var payload = JsonConvert.DeserializeObject<PhxReplyPayload>(
                JsonConvert.SerializeObject(msg.payload));

            if (payload?.status == "ok")
            {
                _state = ChannelState.Joined;
                Debug.Log($"[Realtime:{_name}] 채널 조인 성공");

                // Postgres Changes ID 매핑
                MapPgChangeIds(payload.response);

                _joinTcs?.TrySetResult(true);
            }
            else
            {
                _state = ChannelState.Closed;
                Debug.LogWarning($"[Realtime:{_name}] 조인 실패: {payload?.status}");
                _joinTcs?.TrySetResult(false);
            }
        }

        void MapPgChangeIds(object response)
        {
            if (response == null || _pgRegistrations.Count == 0) return;

            try
            {
                var jobj = JObject.FromObject(response);
                var pgArray = jobj["postgres_changes"] as JArray;
                if (pgArray == null) return;

                _pgIdMap.Clear();
                for (int i = 0; i < pgArray.Count && i < _pgRegistrations.Count; i++)
                {
                    var id = pgArray[i]["id"]?.Value<int>() ?? -1;
                    if (id >= 0)
                        _pgIdMap[id] = i;
                }
            }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning($"[SupaRun:Realtime] 메시지 파싱 실패: {ex.Message}"); }
        }

        void HandleBroadcast(PhoenixMessage msg)
        {
            try
            {
                var payload = JObject.FromObject(msg.payload);
                var eventName = payload["event"]?.ToString();
                var data = payload["payload"]?.ToObject<Dictionary<string, object>>();

                if (eventName != null && _broadcastHandlers.TryGetValue(eventName, out var handlers))
                {
                    foreach (var handler in handlers)
                        handler?.Invoke(data ?? new Dictionary<string, object>());
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime:{_name}] Broadcast 처리 실패: {ex.Message}");
            }
        }

        void HandlePresenceState(PhoenixMessage msg)
        {
            try
            {
                var state = JObject.FromObject(msg.payload);
                _presenceState.Clear();

                foreach (var prop in state.Properties())
                {
                    var user = ParsePresenceUser(prop.Name, prop.Value);
                    if (user != null)
                        _presenceState[prop.Name] = user;
                }

                _onPresenceSync?.Invoke(_presenceState.Values.ToList());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime:{_name}] Presence state 처리 실패: {ex.Message}");
            }
        }

        void HandlePresenceDiff(PhoenixMessage msg)
        {
            try
            {
                var diff = JObject.FromObject(msg.payload);

                // Joins
                var joins = diff["joins"] as JObject;
                if (joins != null)
                {
                    foreach (var prop in joins.Properties())
                    {
                        var user = ParsePresenceUser(prop.Name, prop.Value);
                        if (user != null)
                        {
                            _presenceState[prop.Name] = user;
                            _onPresenceJoin?.Invoke(user);
                        }
                    }
                }

                // Leaves
                var leaves = diff["leaves"] as JObject;
                if (leaves != null)
                {
                    foreach (var prop in leaves.Properties())
                    {
                        if (_presenceState.TryGetValue(prop.Name, out var user))
                        {
                            _presenceState.Remove(prop.Name);
                            _onPresenceLeave?.Invoke(user);
                        }
                    }
                }

                _onPresenceSync?.Invoke(_presenceState.Values.ToList());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime:{_name}] Presence diff 처리 실패: {ex.Message}");
            }
        }

        PresenceUser ParsePresenceUser(string key, JToken value)
        {
            var metas = value?["metas"] as JArray;
            if (metas == null || metas.Count == 0) return null;

            var meta = metas[0].ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            return new PresenceUser { key = key, metadata = meta };
        }

        void HandlePostgresChanges(PhoenixMessage msg)
        {
            try
            {
                var payload = JsonConvert.DeserializeObject<PgChangeEventPayload>(
                    JsonConvert.SerializeObject(msg.payload));

                if (payload?.data == null) return;

                // ID 매핑으로 정확한 핸들러 찾기
                if (payload.ids != null)
                {
                    foreach (var id in payload.ids)
                    {
                        if (_pgIdMap.TryGetValue(id, out var idx) && idx < _pgRegistrations.Count)
                        {
                            _pgRegistrations[idx].handler?.Invoke(payload.data);
                            return;
                        }
                    }
                }

                // ID 매핑 실패 시 테이블 이름으로 매칭
                foreach (var reg in _pgRegistrations)
                {
                    if (reg.config.table == payload.data.table)
                    {
                        reg.handler?.Invoke(payload.data);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime:{_name}] PgChanges 처리 실패: {ex.Message}");
            }
        }

        // === 유틸 ===

        static string ChangeEventToString(ChangeEvent evt)
        {
            return evt switch
            {
                ChangeEvent.Insert => "INSERT",
                ChangeEvent.Update => "UPDATE",
                ChangeEvent.Delete => "DELETE",
                _ => "*"
            };
        }
    }
}
