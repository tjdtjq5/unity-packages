using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Channel Bridge Named Pipe 클라이언트.
    /// Unity → Bridge (Node.js) 방향으로 NDJSON 메시지를 전송하고,
    /// Bridge → Unity 방향의 상태/쿨다운 메시지를 수신한다.
    /// </summary>
    public static class ChannelBridge
    {
        public enum State { Stopped, Connecting, Connected, Error }

        // ── 공개 상태 ──
        public static State CurrentState { get; private set; } = State.Stopped;
        public static event Action<State> OnStateChanged;

        // ── 수신 이벤트 ──
        public static event Action<string> OnMessageReceived;

        // ── Pipe 이름 ──
        static string PipeName => $"claude-unity-{PipeHash}";

        static string _pipeHash;
        public static string PipeHash
        {
            get
            {
                if (_pipeHash != null) return _pipeHash;

                var path = Path.GetDirectoryName(Application.dataPath)!.Replace('\\', '/');
                using var md5 = MD5.Create();
                var bytes = Encoding.UTF8.GetBytes(path);
                var hash = md5.ComputeHash(bytes);
                _pipeHash = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLower();
                return _pipeHash;
            }
        }

        // ── 내부 상태 ──
        static readonly object _pipeLock = new();        // [C1] 스레드 안전성
        static NamedPipeClientStream _pipe;
        static StreamWriter _writer;
        static Thread _readThread;
        static readonly ConcurrentQueue<string> _sendQueue = new();
        static readonly AutoResetEvent _sendSignal = new(false);  // [M1] busy-wait 제거
        static Thread _writeThread;
        static volatile bool _running;

        // ── 재연결 ──
        static int _connectFailCount;
        static double _nextConnectAttempt;               // 메인 스레드에서만 쓰기
        static volatile bool _needsReconnectSchedule;    // [M2] 스레드 간 신호용
        static volatile int _connectGeneration;          // [N5] 이전 스레드 무효화용 세대
        const int MaxConnectFails = 10;
        const float ReconnectInterval = 3f;

        // ── 메인 스레드 디스패치 ──
        static readonly ConcurrentQueue<Action> _mainThreadActions = new();

        [InitializeOnLoadMethod]
        static void Init()
        {
            EditorApplication.update += OnUpdate;
            EditorApplication.quitting += Disconnect;

            // 도메인 리로드 후 Discord 모드 활성이면 자동 재연결
            if (ClaudeCodeSettings.DiscordEnabled && !_running)
            {
                EditorApplication.delayCall += () =>
                {
                    Connect();
                    OnStateChanged -= AutoSendConfigOnConnect;
                    OnStateChanged += AutoSendConfigOnConnect;
                };
            }
        }

        /// <summary>도메인 리로드 후 자동 재연결 시 SendConfig 호출</summary>
        static void AutoSendConfigOnConnect(State state)
        {
            if (state == State.Connected)
            {
                OnStateChanged -= AutoSendConfigOnConnect;
                SendConfig();
                Debug.Log("[ChannelBridge] 도메인 리로드 후 자동 재연결 + Discord 설정 전송");
            }
        }

        // ── 공개 API ──

        /// <summary>Named Pipe 연결 시작</summary>
        public static void Connect()
        {
            if (_running) return;

            _running = true;
            _connectFailCount = 0;
            _connectGeneration++;  // [N5] 세대 카운터로 이전 스레드와 구분
            SetState(State.Connecting);

            _writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "ChannelBridge-Write" };
            _writeThread.Start();

            var gen = _connectGeneration;
            _readThread = new Thread(() => ConnectAndRead(gen)) { IsBackground = true, Name = "ChannelBridge-Read" };
            _readThread.Start();
        }

        /// <summary>Named Pipe 연결 종료</summary>
        public static void Disconnect()
        {
            _running = false;
            _sendSignal.Set(); // WriteLoop 깨우기

            // [C1] lock으로 pipe/writer 안전하게 정리
            lock (_pipeLock)
            {
                try { _pipe?.Dispose(); } catch { /* ignore */ }
                _pipe = null;
                _writer = null;
            }

            // 큐 비우기
            while (_sendQueue.TryDequeue(out _)) { }

            SetState(State.Stopped);
        }

        /// <summary>Bridge에 JSON 메시지 전송 (비동기, fire-and-forget)</summary>
        public static void Send(string json)
        {
            if (!_running) return;
            _sendQueue.Enqueue(json);
            _sendSignal.Set(); // [M1] WriteLoop 깨우기
        }

        /// <summary>Bridge에 설정 메시지 전송</summary>
        public static void SendConfig()
        {
            var enabled = ClaudeCodeSettings.DiscordEnabled;
            var token = ClaudeCodeSettings.DiscordBotToken;
            var channelId = ClaudeCodeSettings.DiscordChannelId;

            Debug.Log($"[ChannelBridge] SendConfig: discord={enabled}, token={(!string.IsNullOrEmpty(token) ? "있음" : "없음")}");

            var config = JsonUtility.ToJson(new ConfigMessage
            {
                type = "config",
                discordEnabled = enabled,
                discordBotToken = token,
                discordChannelId = channelId
            });
            Send(config);
        }

        // ── 내부 구현 ──

        static void SetState(State state)
        {
            if (CurrentState == state) return;
            CurrentState = state;
            _mainThreadActions.Enqueue(() => OnStateChanged?.Invoke(state));
        }

        static void OnUpdate()
        {
            // 메인 스레드에서 콜백 실행
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try { action(); } catch (Exception ex) { Debug.LogError($"[ChannelBridge] {ex}"); }
            }

            // [M2] 백그라운드 스레드에서 요청한 재연결 스케줄 처리 (메인 스레드에서)
            if (_needsReconnectSchedule)
            {
                _needsReconnectSchedule = false;
                _nextConnectAttempt = EditorApplication.timeSinceStartup + ReconnectInterval;
            }

            // 자동 재연결
            if (_running && CurrentState == State.Error &&
                EditorApplication.timeSinceStartup > _nextConnectAttempt &&
                _connectFailCount < MaxConnectFails)
            {
                SetState(State.Connecting);
                var gen = _connectGeneration;
                _readThread = new Thread(() => ConnectAndRead(gen)) { IsBackground = true, Name = "ChannelBridge-Read" };
                _readThread.Start();
            }
        }

        static void ConnectAndRead(int generation)
        {
            bool wasConnected = false;

            try
            {
                var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                pipe.Connect(5000); // 5초 타임아웃

                // [C1] lock으로 pipe/writer 안전하게 설정
                lock (_pipeLock)
                {
                    _pipe = pipe;
                    _writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                }

                _connectFailCount = 0; // [C2] 연결 성공 시 리셋
                wasConnected = true;
                SetState(State.Connected);

                // 수신 루프
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                while (_running && pipe.IsConnected)
                {
                    var line = reader.ReadLine();
                    if (line == null) break; // 연결 끊김

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _mainThreadActions.Enqueue(() => OnMessageReceived?.Invoke(line));
                    }
                }
            }
            catch (TimeoutException)
            {
                // Pipe 서버 아직 안 뜸 — 조용히 재시도
            }
            catch (Exception ex)
            {
                if (_running) // 의도적 Disconnect가 아닐 때만 경고
                    Debug.LogWarning($"[ChannelBridge] 연결 실패: {ex.Message}");
            }
            finally
            {
                // [C1][N3] 안전하게 정리 + pipe Dispose (리소스 누수 방지)
                lock (_pipeLock)
                {
                    _writer = null;
                    try { _pipe?.Dispose(); } catch { /* ignore */ }
                    _pipe = null;
                }

                // [N5] 세대가 바뀌었으면 이 스레드는 무효 — 상태 변경하지 않음
                if (generation == _connectGeneration)
                {
                    // [C2] 연결된 적이 있으면 failCount 리셋 (정상 종료)
                    if (wasConnected)
                        _connectFailCount = 0;
                    else
                        _connectFailCount++;

                    // [M2] 메인 스레드에서 timeSinceStartup 접근하도록 신호만 보냄
                    _needsReconnectSchedule = true;

                    if (_running) SetState(State.Error);
                }
            }
        }

        static void WriteLoop()
        {
            while (_running)
            {
                // [M1] 시그널 대기 (최대 100ms) — busy-wait 제거
                _sendSignal.WaitOne(100);

                while (_sendQueue.TryDequeue(out var json))
                {
                    // [C1] lock으로 writer 안전하게 접근
                    lock (_pipeLock)
                    {
                        try
                        {
                            _writer?.WriteLine(json);
                        }
                        catch
                        {
                            // 쓰기 실패 — 메시지 드롭 (블로킹 방지)
                        }
                    }
                }
            }
        }

        // ── JSON 직렬화용 구조체 ──

        [Serializable]
        struct ConfigMessage
        {
            public string type;
            public bool discordEnabled;
            public string discordBotToken;
            public string discordChannelId;
        }
    }
}
