# com.tjdtjq5.claude v1.0.0 아키텍처 설계

> 작성일: 2026-03-21
> 기반 문서: upgrade-v1.0.0-brainstorm.md

---

## 1. 시스템 전체 구조

```
┌─────────────────────────────────────────────────────────────────┐
│                       Unity Editor                              │
│                                                                 │
│  ┌──────────────┐  ┌───────────────┐  ┌──────────────────────┐  │
│  │ ClaudeToolbar │  │ ManagerWindow │  │  SettingsWindow      │  │
│  │ [뱃지+상태]   │  │ [워크트리관리] │  │  [모니터/Discord/RC] │  │
│  └──────┬───────┘  └──────┬────────┘  └──────────┬───────────┘  │
│         │                 │                      │              │
│  ┌──────┴─────────────────┴──────────────────────┴───────────┐  │
│  │                  ClaudeCodeLauncher                        │  │
│  │  (기존) CLI 실행 + 워크트리 관리                            │  │
│  │  (신규) --channels, --rc 플래그 관리                        │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌───────────────────┐      ┌──────────────────┐               │
│  │   UnityMonitor    │      │  ChannelBridge   │               │
│  │                   │      │                   │               │
│  │ • Console 캡처    │─────→│ • Bridge 프로세스 │               │
│  │ • Compile 캡처    │ IPC  │   생명주기 관리   │               │
│  │ • 필터/중복제거   │      │ • 연결 상태 추적  │               │
│  └───────────────────┘      └────────┬─────────┘               │
│                                      │ Named Pipe              │
└──────────────────────────────────────┼─────────────────────────┘
                                       │
                    ┌──────────────────┼──────────────────────┐
                    │    Channel Bridge (Node.js)             │
                    │    ══════════════════════════            │
                    │                  │                       │
                    │    ┌─────────────▼────────────┐         │
                    │    │   PipeServer             │         │
                    │    │   Named Pipe 리스너       │         │
                    │    └─────────────┬────────────┘         │
                    │                  │                       │
                    │    ┌─────────────▼────────────┐         │
                    │    │   EventRouter            │         │
                    │    │   이벤트 라우팅 + 모드    │         │
                    │    └──┬──────────────────┬────┘         │
                    │       │                  │               │
                    │  ┌────▼─────┐    ┌──────▼──────┐       │
                    │  │ MCP      │    │ Discord     │       │
                    │  │ Channel  │    │ Client      │       │
                    │  │ (stdio)  │    │ (WebSocket) │       │
                    │  └────┬─────┘    └──────┬──────┘       │
                    │       │                  │               │
                    └───────┼──────────────────┼──────────────┘
                            │                  │
                    ┌───────▼───────┐  ┌──────▼──────┐
                    │  Claude Code  │  │  Discord    │
                    │  Session      │  │  Server     │
                    └───────────────┘  └─────────────┘
```

---

## 2. 컴포넌트 상세 설계

### 2.1 Unity 측 (C# Editor)

#### 2.1.1 UnityMonitor.cs

**역할**: Unity 이벤트를 캡처하여 Channel Bridge로 전달

```
[InitializeOnLoad]
static class UnityMonitor
```

**이벤트 소스:**

| 소스 | Unity API | 캡처 시점 |
|------|----------|----------|
| 콘솔 로그 | `Application.logMessageReceived` | 에러/예외 발생 시 |
| 컴파일 | `CompilationPipeline.compilationFinished` | 컴파일 완료 시 |

**핵심 로직:**

```
이벤트 발생
    │
    ▼
[활성 여부 체크] ─── 비활성 → 무시
    │
    ▼
[심각도 필터] ─── 설정 미만 → 무시
    │
    ▼
[중복 체크] ─── 동일 메시지 최근 N초 내 발생 → 카운트만 증가
    │
    ▼
[쿨다운 체크] ─── 같은 소스 파일 쿨다운 중 → 무시
    │
    ▼
[JSON 직렬화]
    │
    ▼
[Named Pipe 전송] ─── 비동기, fire-and-forget
```

**중복 제거 전략:**
- `Dictionary<string, DuplicateEntry>` — key: `{메시지해시}_{소스파일}`
- `DuplicateEntry`: count, firstSeen, lastSeen
- 첫 발생 시 즉시 전달, 이후 동일 에러는 카운트만 증가
- 5초 간 새 발생 없으면 "N회 반복" 요약 전달 후 엔트리 제거

**쿨다운 전략:**
- `Dictionary<string, double>` — key: 소스파일 경로, value: 쿨다운 만료 시각
- 에러 전달 시 해당 소스파일에 쿨다운 설정 (기본 30초)
- 쿨다운 중인 파일의 에러는 무시 (Claude가 수정 중일 가능성)

#### 2.1.2 ChannelBridge.cs

**역할**: Bridge Node.js 프로세스의 생명주기 관리 + Named Pipe 통신

```
static class ChannelBridge
```

**상태 머신:**

```
[Stopped] ──Start()──→ [Starting] ──연결성공──→ [Connected]
    ▲                      │                        │
    │                      │연결실패                  │프로세스종료
    │                      ▼                        │
    └──────────────── [Error] ◄─────────────────────┘
```

**Named Pipe 프로토콜:**
- Pipe 이름: `\\.\pipe\claude-unity-{프로젝트해시}`
  - 프로젝트해시: `ProjectPath`의 MD5 앞 8자리 (워크트리 간 충돌 방지)
- Unity가 클라이언트, Bridge가 서버
- 메시지 형식: 줄바꿈 구분 JSON (NDJSON)

**전송 메시지 스키마:**

```json
{
  "type": "unity_event",
  "category": "console" | "compile",
  "severity": "error" | "warning" | "exception",
  "message": "NullReferenceException: Object reference not set...",
  "stackTrace": "at PlayerController.Update() in PlayerController.cs:42",
  "sourceFile": "Assets/Scripts/PlayerController.cs",
  "sourceLine": 42,
  "timestamp": "2026-03-21T15:30:00.000Z",
  "repeatCount": 1
}
```

**수신 메시지 스키마 (Bridge → Unity):**

```json
{
  "type": "bridge_status",
  "status": "connected" | "discord_connected" | "discord_disconnected" | "error",
  "message": "optional details"
}
```

**프로세스 관리:**
- Bridge 프로세스는 `Process.Start()`로 실행
- stdout/stderr 리다이렉트하여 상태 모니터링
- Unity 에디터 종료 시 `OnApplicationQuit`에서 프로세스 kill
- Bridge 크래시 시 자동 재시작 (최대 3회, 백오프)

#### 2.1.3 ClaudeCodeSettings.cs (확장)

**신규 설정 항목:**

```csharp
// ── 모니터 ──
public static bool MonitorEnabled { get; set; }          // 기본: false
public static int MonitorSeverity { get; set; }           // 0=Error, 1=Warning, 2=All
public static int CooldownSeconds { get; set; }           // 기본: 30

// ── Discord ──
public static int DiscordMode { get; set; }               // 0=Off, 1=Notify, 2=Interactive
public static string DiscordBotToken { get; set; }        // 암호화 저장
public static string DiscordChannelId { get; set; }
public static string DiscordAllowedUsers { get; set; }    // 쉼표 구분 user ID

// ── Remote Control ──
public static bool RemoteControlEnabled { get; set; }     // 기본: false
```

**DiscordBotToken 보안:**
- EditorPrefs에 평문 저장 X
- `System.Security.Cryptography.ProtectedData`로 DPAPI 암호화 (Windows)
- 현재 사용자 범위로 보호 → 다른 Windows 계정에서 복호화 불가

#### 2.1.4 ClaudeCodeLauncher.cs (확장)

**변경 사항:**

`BuildClaudeCommand()` 수정:

```
기존: claude {additionalArgs}
변경: claude {additionalArgs} {--rc} {--channels bridgePath}
```

- `RemoteControlEnabled == true` → `--rc` 추가
- `MonitorEnabled == true` → `--channels "{Bridge~/path}"` 추가
  - 또는 `--dangerously-load-development-channels` (개발 중)

#### 2.1.5 ClaudeToolbar.cs (확장)

**뱃지 표시 변경:**

```
현재: ✦ Claude [N]       (N = 워크트리 수)
변경: ✦ Claude [N] ●     (● = Channel 연결 상태)
       또는
      ✦ Claude [N] ○     (○ = Channel 미연결)
```

- `●` 색상: 초록(정상) / 노랑(Discord 미연결) / 빨강(에러)

#### 2.1.6 ClaudeCodeManagerWindow.cs (확장)

**신규 섹션: 모니터 상태**

```
┌─────────────────────────────────┐
│ Channel 상태                     │
│ ┌─────────────────────────────┐ │
│ │ Bridge: ● 연결됨             │ │
│ │ Discord: ● 알림 모드         │ │
│ │ 모니터: ● 활성 (Error만)     │ │
│ │                              │ │
│ │ [모니터 ON/OFF]  [Discord ▼] │ │
│ └─────────────────────────────┘ │
└─────────────────────────────────┘
```

- Bridge 시작/중지 버튼
- 모니터 on/off 토글
- Discord 모드 드롭다운 (없음/알림/적극적 사용)

#### 2.1.7 ClaudeCodeSettingsWindow.cs (확장)

**신규 섹션:**

```
┌─────────────────────────────────┐
│ ◆ 모니터 설정                    │
│ ┌─────────────────────────────┐ │
│ │ 전달 심각도: [Error ▼]      │ │
│ │ 쿨다운:      [30]초         │ │
│ └─────────────────────────────┘ │
│                                  │
│ ◆ Discord 설정                   │
│ ┌─────────────────────────────┐ │
│ │ Bot Token:  [••••••••] [표시]│ │
│ │ Channel ID: [1234567890]    │ │
│ │ 허용 사용자: [user1, user2] │ │
│ └─────────────────────────────┘ │
│                                  │
│ ◆ Remote Control                 │
│ ┌─────────────────────────────┐ │
│ │ [✓] 기본 활성화              │ │
│ └─────────────────────────────┘ │
└─────────────────────────────────┘
```

---

### 2.2 Bridge 측 (Node.js)

#### 2.2.1 프로젝트 구조

```
Bridge~/
├── package.json
├── .gitignore              ← node_modules/
├── src/
│   ├── index.js            ← 진입점 (MCP 서버 초기화)
│   ├── pipe-server.js      ← Named Pipe 서버
│   ├── event-router.js     ← 이벤트 라우팅 + 모드 관리
│   ├── mcp-channel.js      ← MCP Channel 프로토콜 구현
│   └── discord-client.js   ← Discord 봇 클라이언트
└── config.schema.json      ← 설정 스키마
```

#### 2.2.2 package.json

```json
{
  "name": "claude-unity-bridge",
  "version": "1.0.0",
  "type": "module",
  "main": "src/index.js",
  "dependencies": {
    "@modelcontextprotocol/sdk": "^1.x",
    "discord.js": "^14.x"
  }
}
```

#### 2.2.3 index.js — 진입점

```
시작 흐름:
1. MCP 서버 초기화 (stdio 통신)
2. claude/channel capability 선언
3. Named Pipe 서버 시작
4. (설정에 따라) Discord 클라이언트 연결
5. 이벤트 라우팅 시작
```

**MCP 서버 초기화 시 선언하는 것:**

```javascript
// Capabilities
{
  "claude/channel": {
    "instructions": "Unity 에디터와 연결된 채널입니다. Unity에서 발생하는 에러, 컴파일 결과, 성능 이벤트를 수신합니다. Discord에서 사용자의 작업 지시도 전달됩니다."
  }
}

// Tools (Claude가 호출 가능)
{
  "reply": {
    "description": "Discord 채널에 메시지를 보냅니다",
    "parameters": { "message": "string" }
  },
  "send_notification": {
    "description": "Discord에 알림을 보냅니다 (알림 모드에서도 동작)",
    "parameters": { "message": "string", "category": "string" }
  },
  "set_cooldown": {
    "description": "특정 소스 파일의 에러 쿨다운을 설정합니다",
    "parameters": { "sourceFile": "string", "seconds": "number" }
  }
}
```

#### 2.2.4 pipe-server.js — Named Pipe 서버

```
역할: Unity로부터 NDJSON 메시지 수신

동작:
1. Named Pipe 서버 생성 (\\.\pipe\claude-unity-{hash})
2. Unity 연결 대기
3. 연결 시 줄 단위로 JSON 파싱
4. 파싱된 이벤트를 EventRouter로 전달
5. 연결 끊김 시 재연결 대기

특이사항:
- Unity가 여러 번 연결/해제할 수 있음 (에디터 재시작 등)
- 한 번에 하나의 연결만 허용
```

#### 2.2.5 event-router.js — 이벤트 라우팅

```
역할: 이벤트의 출처와 현재 모드에 따라 적절한 대상으로 라우팅

소스별 라우팅:

[Unity 이벤트] ──→ 항상 Claude에 전달 (MCP notification)
                ──→ Discord 모드가 notify 이상이면 Discord에도 전달

[Discord 메시지] ──→ 모드가 interactive일 때만 Claude에 전달
                 ──→ 모드가 notify이면 무시 + Discord에 "알림 모드입니다" 응답
                 ──→ 모드가 off이면 무시

[Claude 응답] ──→ reply 도구 호출 시 Discord에 전달
              ──→ send_notification 호출 시 Discord에 전달 (모드 무관)
```

**모드 전환:**
- Discord에서 `!mode` 명령으로 변경
- Unity 에디터 설정에서 변경 → Named Pipe로 모드 변경 메시지 전달
- Bridge 내부 상태로 관리

#### 2.2.6 mcp-channel.js — MCP Channel 구현

```
역할: Claude Code와의 MCP 프로토콜 통신

핵심 동작:
- Unity 이벤트 → mcp.notification() 으로 Claude에 push
- Claude의 tool 호출 → 해당 핸들러 실행

알림 포맷 (Claude에게 전달되는 형태):

<channel source="unity" category="console">
[ERROR] NullReferenceException: Object reference not set to an instance of an object
at PlayerController.Update () in Assets/Scripts/PlayerController.cs:42

소스 파일: Assets/Scripts/PlayerController.cs:42
이 에러를 분석하고 수정해주세요.
</channel>

<channel source="discord" user="tjdtjq5">
PlayerController에 대시 기능 추가해줘
</channel>
```

#### 2.2.7 discord-client.js — Discord 봇

```
역할: Discord 서버와의 양방향 통신

초기화:
1. Bot token으로 Discord 로그인
2. 지정된 채널 리스닝 시작
3. 허용된 사용자 목록 로드

메시지 수신:
1. 허용된 사용자인지 확인
2. !명령어인지 확인 → 명령어 처리
3. 일반 메시지 → EventRouter로 전달

메시지 발신:
1. EventRouter에서 전달받은 메시지를 채널에 전송
2. 긴 메시지는 분할 (Discord 2000자 제한)
3. 코드 블록은 적절히 포맷팅

Discord 명령어 처리:
!mode off      → EventRouter.setMode('off')
!mode notify   → EventRouter.setMode('notify')
!mode active   → EventRouter.setMode('interactive')
!mute          → EventRouter.setMute(true)
!unmute        → EventRouter.setMute(false)
!status        → 현재 상태를 채널에 전송
```

---

## 3. 데이터 흐름 시퀀스

### 3.1 Unity 에러 → Claude 자동 수정

```
Unity Editor          Named Pipe          Bridge             Claude Code
    │                     │                  │                    │
    │ NullRefException    │                  │                    │
    │ 발생                │                  │                    │
    ├──── filter/dedup ───┤                  │                    │
    │                     │                  │                    │
    │     JSON 전송 ──────┼─────────────────→│                    │
    │                     │                  │                    │
    │                     │                  ├── mcp.notify() ───→│
    │                     │                  │                    │
    │                     │                  │                    │ 코드 분석
    │                     │                  │                    │ 파일 수정
    │                     │                  │                    │ git commit
    │                     │                  │                    │
    │                     │                  │◄── set_cooldown ──│
    │                     │                  │    (30초)          │
    │                     │                  │                    │
    │                     │                  │◄── send_notif ────│
    │                     │                  │    "수정 완료"      │
    │                     │                  │                    │
    │                     │                  ├───→ Discord        │
    │                     │                  │    "🔧 수정 완료"   │
    │                     │                  │                    │
```

### 3.2 Discord → Claude 작업 지시 (Interactive 모드)

```
Discord              Bridge              Claude Code          Unity Editor
    │                    │                    │                    │
    │ "대시 기능 추가"    │                    │                    │
    ├───────────────────→│                    │                    │
    │                    │                    │                    │
    │                    │ mcp.notify() ─────→│                    │
    │                    │ <channel source=   │                    │
    │                    │  "discord">        │                    │
    │                    │                    │                    │
    │                    │                    │ 코드 수정           │
    │                    │                    │ 파일 저장           │
    │                    │                    │                    │
    │                    │◄── reply ─────────│                    │
    │                    │ "✅ 완료: ..."     │                    │
    │                    │                    │                    │
    │◄───────────────────│                    │                    │
    │ "✅ 완료: ..."      │                    │                    │
    │                    │                    │    컴파일 트리거     │
    │                    │                    │         │          │
    │                    │◄──────────────────┼─────────┘          │
    │                    │ compile_success    │                    │
    │                    │                    │                    │
```

---

## 4. 실행 방식

### 4.1 Claude Code 실행 시 Channel 연결

**현재:**
```
claude {additionalArgs}
```

**변경:**
```
claude {additionalArgs} --channels "C:/.../com.tjdtjq5.claude/Bridge~/src/index.js"
```

- Claude Code가 Bridge를 서브프로세스로 spawn
- Bridge가 stdio로 MCP 프로토콜 통신
- Bridge가 동시에 Named Pipe 서버 + Discord 봇 시작

### 4.2 Unity 측 시작 흐름

```
1. Unity Editor 시작
2. [InitializeOnLoad] UnityMonitor 초기화
   → Application.logMessageReceived 구독
   → CompilationPipeline 구독
3. 사용자가 "메인 Claude 실행" 또는 "워크트리 열기" 클릭
4. ClaudeCodeLauncher가 --channels 플래그 포함하여 Claude Code 실행
5. Claude Code가 Bridge 서브프로세스 spawn
6. Bridge가 Named Pipe 서버 시작
7. UnityMonitor가 Named Pipe에 연결 시도 (주기적 재시도)
8. 연결 성공 → ChannelBridge 상태를 Connected로 변경
9. 이후 이벤트 발생 시 자동 전달
```

### 4.3 환경 변수를 통한 설정 전달

Bridge가 Claude Code의 서브프로세스이므로, Unity에서 직접 Bridge에 설정을 전달할 수 없다.
대신 Named Pipe를 통해 설정 변경 메시지를 보낸다.

```json
{
  "type": "config",
  "discordMode": "notify",
  "discordBotToken": "...",
  "discordChannelId": "...",
  "discordAllowedUsers": ["user1", "user2"]
}
```

**주의**: Bot Token은 Named Pipe로 전달되므로 로컬 통신이라도 주의 필요.
Named Pipe가 로컬 전용이므로 네트워크 노출은 없지만,
다른 로컬 프로세스가 같은 파이프에 연결할 수 있는 이론적 위험이 있다.
→ Bridge 측에서 첫 연결만 허용 (단일 클라이언트 모드)

---

## 5. 에러 처리 및 복원력

### 5.1 Bridge 프로세스 크래시

```
Bridge 크래시 감지 (stdout/stderr EOF 또는 Process.HasExited)
    │
    ▼
ChannelBridge 상태 → Error
    │
    ▼
재시도 카운터 < 3?
    ├─ Yes → 5초 대기 후 재시작
    └─ No  → 상태를 Error로 유지, 툴바에 빨간 ● 표시
            사용자가 수동 재시작 가능
```

### 5.2 Named Pipe 연결 끊김

```
Unity 측: Pipe 쓰기 실패 감지
    │
    ▼
연결 상태 → Disconnected
    │
    ▼
3초 간격으로 재연결 시도 (백그라운드)
    │
    ▼
재연결 성공 → Connected, 이벤트 전달 재개
```

### 5.3 Discord 연결 끊김

```
Bridge 측: Discord WebSocket 끊김 감지
    │
    ▼
discord.js 자동 재연결 시도 (내장)
    │
    ▼
Unity에 상태 메시지: discord_disconnected
    │
    ▼
재연결 성공 → Unity에 상태 메시지: discord_connected
```

### 5.4 에디터 성능 보호

- UnityMonitor의 이벤트 처리는 메인 스레드에서 최소 작업만 수행
- JSON 직렬화 + Pipe 쓰기는 ThreadPool으로 위임
- Pipe 버퍼 가득 참 → 메시지 드롭 (블로킹 X)
- 초당 이벤트 리미터: 최대 10개/초 (버스트 보호)

---

## 6. 파일 변경 목록

### 수정 파일

| 파일 | 변경 내용 |
|------|----------|
| `ClaudeCodeLauncher.cs` | `BuildClaudeCommand()`에 --channels, --rc 플래그 추가 |
| `ClaudeCodeSettings.cs` | Monitor, Discord, RC 설정 속성 추가 |
| `ClaudeCodeSettingsWindow.cs` | 모니터/Discord/RC 설정 UI 섹션 추가 |
| `ClaudeCodeManagerWindow.cs` | Channel 상태 섹션 추가, 모니터 토글, Discord 모드 드롭다운 |
| `ClaudeToolbar.cs` | 뱃지에 Channel 연결 상태 인디케이터 추가 |
| `package.json` | 버전 0.3.0 → 1.0.0 |

### 신규 파일 (Unity)

| 파일 | 역할 |
|------|------|
| `Editor/UnityMonitor.cs` | 콘솔/컴파일 이벤트 캡처 + 필터 + Pipe 전송 |
| `Editor/ChannelBridge.cs` | Bridge 프로세스 생명주기 + Named Pipe 클라이언트 |

### 신규 파일 (Bridge)

| 파일 | 역할 |
|------|------|
| `Bridge~/package.json` | Node.js 프로젝트 정의 |
| `Bridge~/.gitignore` | node_modules 제외 |
| `Bridge~/src/index.js` | 진입점, MCP 서버 초기화 |
| `Bridge~/src/pipe-server.js` | Named Pipe 서버 |
| `Bridge~/src/event-router.js` | 이벤트 라우팅 + 모드 관리 |
| `Bridge~/src/mcp-channel.js` | MCP Channel 프로토콜 |
| `Bridge~/src/discord-client.js` | Discord 봇 |

---

## 7. 구현 순서 (권장)

```
Phase 1: P1 기반 (Unity → Claude)
  1-1. ChannelBridge.cs — Named Pipe 클라이언트 (Unity → Bridge 전송)
  1-2. Bridge~/src/pipe-server.js — Named Pipe 서버
  1-3. Bridge~/src/mcp-channel.js — MCP Channel 기본 구현
  1-4. Bridge~/src/index.js — 진입점
  1-5. UnityMonitor.cs — 이벤트 캡처 + 필터
  1-6. ClaudeCodeLauncher.cs — --channels 플래그 추가
  1-7. 통합 테스트: Unity 에러 → Claude 수신 확인

Phase 2: P2 (Discord)
  2-1. Bridge~/src/discord-client.js — Discord 봇 기본
  2-2. Bridge~/src/event-router.js — 모드별 라우팅
  2-3. ClaudeCodeSettings.cs — Discord 설정
  2-4. Settings/Manager UI — Discord 설정 + 상태 표시
  2-5. 통합 테스트: Discord 양방향 통신 확인

Phase 3: P3 (Remote Control) + UI 마무리
  3-1. ClaudeCodeSettings.cs — RC 설정
  3-2. ClaudeCodeLauncher.cs — --rc 플래그
  3-3. Settings UI — RC 토글
  3-4. ClaudeToolbar.cs — 상태 인디케이터
  3-5. 전체 통합 테스트
```
