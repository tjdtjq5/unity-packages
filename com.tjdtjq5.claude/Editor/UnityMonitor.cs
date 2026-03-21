using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Unity 콘솔/컴파일 이벤트를 캡처하여 Channel Bridge로 전달한다.
    /// 필터링, 중복 제거, 쿨다운, 초당 리미터를 적용한다.
    /// </summary>
    [InitializeOnLoad]
    static class UnityMonitor
    {
        // ── 중복 제거 ──
        struct DuplicateEntry
        {
            public int Count;
            public double FirstSeen;
            public double LastSeen;
            public bool Sent;
        }

        static readonly Dictionary<string, DuplicateEntry> _duplicates = new();
        const double DuplicateFlushInterval = 5.0;

        // ── 쿨다운 ──
        static readonly Dictionary<string, double> _cooldowns = new();

        // ── 초당 리미터 ──
        static int _eventsThisSecond;
        static double _currentSecond;
        const int MaxEventsPerSecond = 10;

        // [H2] OnUpdate용 리스트 캐시 (매 프레임 GC 할당 방지)
        static readonly List<string> _tempKeys = new();

        // ── 스택트레이스 파서 ──
        static readonly Regex StackTraceFileRegex = new(
            @"in\s+(.+\.cs):(\d+)",
            RegexOptions.Compiled);

        static UnityMonitor()
        {
            Application.logMessageReceived += OnLogMessage;
            // [H3] compilationFinished 빈 구독 제거 — assemblyCompilationFinished만 사용
            ChannelBridge.OnMessageReceived += OnBridgeMessage;
            EditorApplication.update += OnUpdate;
        }

        // ── 콘솔 로그 처리 ──

        static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (!ClaudeCodeSettings.MonitorEnabled) return;

            // 심각도 필터
            var severity = type switch
            {
                LogType.Error => "error",
                LogType.Exception => "exception",
                LogType.Warning => "warning",
                LogType.Assert => "error",
                _ => null
            };

            if (severity == null) return;

            var minSeverity = ClaudeCodeSettings.MonitorSeverity;
            if (minSeverity == 0 && severity == "warning") return;

            // 자체 로그 제외
            if (message.StartsWith("[ChannelBridge]") || message.StartsWith("[UnityMonitor]")) return;

            // 초당 리미터
            var now = EditorApplication.timeSinceStartup;
            if (Math.Floor(now) != _currentSecond)
            {
                _currentSecond = Math.Floor(now);
                _eventsThisSecond = 0;
            }
            if (_eventsThisSecond >= MaxEventsPerSecond) return;

            // 소스 파일 추출
            var (sourceFile, sourceLine) = ParseStackTrace(stackTrace);

            // 쿨다운 체크
            if (sourceFile != null && _cooldowns.TryGetValue(sourceFile, out var expiry) && now < expiry)
                return;

            // 중복 체크
            var dedupKey = $"{message.GetHashCode()}_{sourceFile}";
            if (_duplicates.TryGetValue(dedupKey, out var entry))
            {
                entry.Count++;
                entry.LastSeen = now;
                _duplicates[dedupKey] = entry;
                return;
            }

            // 첫 발생 — 즉시 전달
            _duplicates[dedupKey] = new DuplicateEntry
            {
                Count = 1,
                FirstSeen = now,
                LastSeen = now,
                Sent = true
            };

            _eventsThisSecond++;
            SendEvent("console", severity, message, stackTrace, sourceFile, sourceLine, 1);
        }

        // ── 컴파일 결과 처리 ──

        [InitializeOnLoadMethod]
        static void RegisterCompileCallback()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (!ClaudeCodeSettings.MonitorEnabled) return;

            foreach (var msg in messages)
            {
                if (msg.type != CompilerMessageType.Error) continue;

                var sourceFile = msg.file;
                var sourceLine = msg.line;
                var message = $"{msg.message} ({msg.file}:{msg.line})";

                // 쿨다운 체크
                if (sourceFile != null)
                {
                    var now = EditorApplication.timeSinceStartup;
                    if (_cooldowns.TryGetValue(sourceFile, out var expiry) && now < expiry)
                        continue;
                }

                SendEvent("compile", "error", message, "", sourceFile, sourceLine, 1);
            }
        }

        // ── Bridge → Unity 메시지 처리 ──

        static void OnBridgeMessage(string json)
        {
            try
            {
                var msg = JsonUtility.FromJson<BridgeMessage>(json);

                switch (msg.type)
                {
                    case "set_cooldown":
                        if (!string.IsNullOrEmpty(msg.sourceFile))
                        {
                            // [H4] 설정의 CooldownSeconds를 기본값으로 사용
                            var seconds = msg.seconds > 0 ? msg.seconds : ClaudeCodeSettings.CooldownSeconds;
                            _cooldowns[msg.sourceFile] = EditorApplication.timeSinceStartup + seconds;
                        }
                        break;

                    case "bridge_status":
                        break;

                    case "mode_changed":
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityMonitor] Bridge 메시지 파싱 실패: {ex.Message}");
            }
        }

        // ── 주기적 처리 ──

        static void OnUpdate()
        {
            if (!ClaudeCodeSettings.MonitorEnabled) return;

            var now = EditorApplication.timeSinceStartup;

            // [H2] 캐시된 리스트 재사용 (GC 할당 방지)
            // 중복 엔트리 플러시
            _tempKeys.Clear();
            foreach (var kv in _duplicates)
            {
                if (now - kv.Value.LastSeen > DuplicateFlushInterval)
                {
                    if (kv.Value.Count > 1)
                    {
                        SendEvent("console", "info",
                            $"이전 에러가 {kv.Value.Count}회 반복되었습니다",
                            "", null, 0, kv.Value.Count);
                    }
                    _tempKeys.Add(kv.Key);
                }
            }
            foreach (var key in _tempKeys)
                _duplicates.Remove(key);

            // 만료된 쿨다운 정리
            _tempKeys.Clear();
            foreach (var kv in _cooldowns)
            {
                if (now > kv.Value) _tempKeys.Add(kv.Key);
            }
            foreach (var key in _tempKeys)
                _cooldowns.Remove(key);
        }

        // ── 이벤트 전송 ──

        static void SendEvent(string category, string severity, string message,
            string stackTrace, string sourceFile, int sourceLine, int repeatCount)
        {
            var evt = new UnityEvent
            {
                type = "unity_event",
                category = category,
                severity = severity,
                message = message,
                stackTrace = stackTrace ?? "",
                sourceFile = sourceFile ?? "",
                sourceLine = sourceLine,
                timestamp = DateTime.UtcNow.ToString("O"),
                repeatCount = repeatCount
            };

            ChannelBridge.Send(JsonUtility.ToJson(evt));
        }

        // ── 스택트레이스 파서 ──

        static (string file, int line) ParseStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return (null, 0);

            var match = StackTraceFileRegex.Match(stackTrace);
            if (!match.Success) return (null, 0);

            var file = match.Groups[1].Value.Replace('\\', '/');
            int.TryParse(match.Groups[2].Value, out var line);
            return (file, line);
        }

        // ── JSON 직렬화용 구조체 ──

        [Serializable]
        struct UnityEvent
        {
            public string type;
            public string category;
            public string severity;
            public string message;
            public string stackTrace;
            public string sourceFile;
            public int sourceLine;
            public string timestamp;
            public int repeatCount;
        }

        [Serializable]
        struct BridgeMessage
        {
            public string type;
            public string status;
            public string message;
            public string sourceFile;
            public int seconds;
        }
    }
}
