using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Supabase
{
    /// <summary>
    /// Supabase Realtime WebSocket 클라이언트.
    /// Phoenix Channel 프로토콜 (vsn=1.0.0) 구현.
    /// </summary>
    public class SupabaseRealtime : IRealtimeClient
    {
        readonly string _url;
        readonly string _anonKey;
        string _accessToken;

        ClientWebSocket _ws;
        CancellationTokenSource _cts;
        readonly Dictionary<string, RealtimeChannel> _channels = new Dictionary<string, RealtimeChannel>();
        int _refCounter;
        string _pendingHeartbeatRef;
        int _reconnectAttempt;
        bool _intentionalDisconnect;

        static readonly int[] ReconnectDelays = { 1000, 2000, 5000, 10000 };
        const int HeartbeatIntervalMs = 25000;
        const int ReceiveBufferSize = 8192;

        // 메인 스레드 디스패치 큐
        internal static readonly ConcurrentQueue<Action> MainThreadQueue = new ConcurrentQueue<Action>();

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public SupabaseRealtime(string supabaseUrl, string anonKey)
        {
            _url = supabaseUrl;
            _anonKey = anonKey;
        }

        /// <summary>액세스 토큰 설정 (RLS 인증용).</summary>
        public void SetAccessToken(string token)
        {
            _accessToken = token;
            // 이미 연결된 채널에 토큰 갱신
            foreach (var ch in _channels.Values)
                ch.PushAccessToken(token);
        }

        internal string AccessToken => _accessToken ?? _anonKey;

        /// <summary>채널 생성. Subscribe() 전까지는 연결하지 않음.</summary>
        public RealtimeChannel Channel(string name)
        {
            var topic = $"realtime:{name}";
            if (_channels.TryGetValue(topic, out var existing))
                return existing;

            var channel = new RealtimeChannel(this, name, topic);
            _channels[topic] = channel;
            return channel;
        }

        /// <summary>채널 제거.</summary>
        internal void RemoveChannel(string topic)
        {
            _channels.Remove(topic);
            // 채널이 없으면 연결 끊기
            if (_channels.Count == 0)
                Disconnect();
        }

        /// <summary>전체 연결 종료.</summary>
        public void Disconnect()
        {
            _intentionalDisconnect = true;
            _cts?.Cancel();

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                try { _ = _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None); }
                catch (System.Exception ex) { UnityEngine.Debug.Log($"[SupaRun:Realtime] WebSocket 닫기: {ex.Message}"); }
            }

            _ws = null;
            Debug.Log("[Realtime] 연결 종료");
        }

        /// <summary>WebSocket 연결 (첫 채널 Subscribe 시 자동 호출).</summary>
        internal async Task EnsureConnected()
        {
            if (IsConnected) return;

            _intentionalDisconnect = false;
            _cts = new CancellationTokenSource();
            _reconnectAttempt = 0;

            await Connect();
        }

        async Task Connect()
        {
            // URL 빌드: wss://xxx.supabase.co/realtime/v1/websocket?apikey=xxx&vsn=1.0.0
            var wsUrl = _url.Replace("https://", "wss://").Replace("http://", "ws://");
            wsUrl = $"{wsUrl}/realtime/v1/websocket?apikey={_anonKey}&vsn=1.0.0";

            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("X-Client-Info", "gameserver-unity");

            try
            {
                await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
                _reconnectAttempt = 0;
                Debug.Log("[Realtime] WebSocket 연결 성공");

                // 백그라운드 수신 루프 + 하트비트 시작
                _ = ReceiveLoop(_cts.Token);
                _ = HeartbeatLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime] 연결 실패: {ex.Message}");
                _ = TryReconnect();
            }
        }

        async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[ReceiveBufferSize];

            try
            {
                while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.Log("[Realtime] 서버가 연결을 닫음");
                        break;
                    }

                    var json = sb.ToString();
                    HandleMessage(json);
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
            catch (WebSocketException ex)
            {
                Debug.LogWarning($"[Realtime] WebSocket 오류: {ex.Message}");
            }

            if (!_intentionalDisconnect)
                _ = TryReconnect();
        }

        async Task HeartbeatLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatIntervalMs, ct);
                    if (ct.IsCancellationRequested) break;

                    // 이전 하트비트 응답 안 옴 → 연결 죽음
                    if (_pendingHeartbeatRef != null)
                    {
                        Debug.LogWarning("[Realtime] 하트비트 타임아웃 — 재연결");
                        _pendingHeartbeatRef = null;
                        _cts?.Cancel();
                        break;
                    }

                    _pendingHeartbeatRef = MakeRef();
                    await Send(new PhoenixMessage
                    {
                        topic = "phoenix",
                        evt = "heartbeat",
                        payload = new { },
                        msgRef = _pendingHeartbeatRef
                    });
                }
            }
            catch (OperationCanceledException) { /* 정상 종료 */ }
        }

        void HandleMessage(string json)
        {
            PhoenixMessage msg;
            try
            {
                msg = JsonConvert.DeserializeObject<PhoenixMessage>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime] 메시지 파싱 실패: {ex.Message}");
                return;
            }

            // 하트비트 응답
            if (msg.topic == "phoenix" && msg.evt == "phx_reply")
            {
                if (msg.msgRef == _pendingHeartbeatRef)
                    _pendingHeartbeatRef = null;
                return;
            }

            // 채널에 메시지 라우팅
            if (_channels.TryGetValue(msg.topic, out var channel))
            {
                EnqueueCallback(() => channel.HandleMessage(msg));
            }
        }

        async Task TryReconnect()
        {
            if (_intentionalDisconnect) return;

            var delay = _reconnectAttempt < ReconnectDelays.Length
                ? ReconnectDelays[_reconnectAttempt]
                : ReconnectDelays[ReconnectDelays.Length - 1];

            _reconnectAttempt++;
            Debug.Log($"[Realtime] {delay}ms 후 재연결 시도 (#{_reconnectAttempt})");

            await Task.Delay(delay);
            if (_intentionalDisconnect) return;

            _cts = new CancellationTokenSource();
            await Connect();

            // 재연결 성공 시 채널 재구독
            if (IsConnected)
            {
                foreach (var ch in _channels.Values)
                    _ = ch.Rejoin();
            }
        }

        // === 내부 헬퍼 ===

        internal string MakeRef() => (++_refCounter).ToString();

        internal async Task Send(PhoenixMessage msg)
        {
            if (_ws?.State != WebSocketState.Open) return;

            var json = JsonConvert.SerializeObject(msg,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            var bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Realtime] 전송 실패: {ex.Message}");
            }
        }

        internal void EnqueueCallback(Action action)
        {
            MainThreadQueue.Enqueue(action);
        }

        // === 메인 스레드 디스패처 ===

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void SetupDispatcher()
        {
            var go = new GameObject("[RealtimeDispatcher]");
            go.AddComponent<RealtimeDispatcher>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }

    /// <summary>메인 스레드에서 Realtime 콜백 실행.</summary>
    internal class RealtimeDispatcher : MonoBehaviour
    {
        void Update()
        {
            while (SupabaseRealtime.MainThreadQueue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
