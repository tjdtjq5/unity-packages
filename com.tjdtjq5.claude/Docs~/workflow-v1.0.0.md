# com.tjdtjq5.claude v1.0.0 구현 워크플로우

> 작성일: 2026-03-21
> 기반 문서: architecture-v1.0.0.md, upgrade-v1.0.0-brainstorm.md

---

## 구현 개요

```
Phase 1: 코어 파이프라인 (Unity → Bridge → Claude)     ← 가장 먼저, 나머지의 기반
Phase 2: Discord 연동                                   ← Phase 1 완료 후
Phase 3: Remote Control + UI 마무리                     ← Phase 2와 병렬 가능
```

총 작업: 19개 태스크, 3개 체크포인트

---

## Phase 1: 코어 파이프라인

> Unity 에디터 이벤트가 Channel Bridge를 거쳐 Claude Code에 도달하는 핵심 경로

### 작업 의존성

```
1-1 Bridge 프로젝트 스캐폴딩
 │
 ├──→ 1-2 pipe-server.js ──→ 1-4 mcp-channel.js ──→ 1-5 index.js (진입점)
 │                                                         │
 └──→ 1-3 ChannelBridge.cs (Named Pipe 클라이언트)          │
       │                                                    │
       └──→ 1-6 UnityMonitor.cs ──→ 1-7 Launcher 수정 ─────┘
                                         │
                                    [체크포인트 1]
```

---

### 1-1. Bridge 프로젝트 스캐폴딩

**작업**: `Bridge~/` 디렉토리 생성 및 Node.js 프로젝트 초기화

**파일:**
- `Bridge~/package.json`
- `Bridge~/.gitignore`
- `Bridge~/src/` (빈 디렉토리)

**package.json:**
```json
{
  "name": "claude-unity-bridge",
  "version": "1.0.0",
  "type": "module",
  "main": "src/index.js",
  "scripts": {
    "start": "node src/index.js"
  },
  "dependencies": {
    "@modelcontextprotocol/sdk": "^1.0.0"
  }
}
```

**.gitignore:**
```
node_modules/
```

**주의**: discord.js는 Phase 2에서 추가. Phase 1에서는 MCP SDK만.

**완료 조건**: `cd Bridge~ && npm install` 성공

**의존**: 없음

---

### 1-2. pipe-server.js — Named Pipe 서버

**작업**: Node.js에서 Named Pipe 서버 구현

**파일**: `Bridge~/src/pipe-server.js`

**핵심 구현:**
- `net.createServer()`로 Named Pipe 서버 생성
- Pipe 이름: `\\.\pipe\claude-unity-{hash}` (hash는 환경변수로 수신)
- NDJSON 파싱 (줄바꿈 구분 JSON)
- 이벤트 콜백: `onMessage(json)`, `onConnect()`, `onDisconnect()`
- 단일 클라이언트 모드: 기존 연결 있으면 새 연결 거부

**인터페이스:**
```javascript
export class PipeServer {
  constructor(pipeName)
  start()                          // 서버 시작, Promise 반환
  stop()                           // 서버 종료
  on('message', callback)          // Unity에서 메시지 수신
  on('connect', callback)          // Unity 연결
  on('disconnect', callback)       // Unity 연결 끊김
  sendToUnity(json)                // Unity에 메시지 전송 (Bridge → Unity)
}
```

**검증**: 단위 테스트 — Node.js 클라이언트로 Pipe 연결 → JSON 전송 → 수신 확인

**의존**: 1-1

---

### 1-3. ChannelBridge.cs — Named Pipe 클라이언트

**작업**: Unity C#에서 Named Pipe 클라이언트 구현

**파일**: `Editor/ChannelBridge.cs`

**핵심 구현:**
```csharp
static class ChannelBridge
{
    // 상태
    public enum State { Stopped, Connecting, Connected, Error }
    public static State CurrentState { get; }
    public static event Action<State> OnStateChanged;

    // Pipe 이름 생성
    static string PipeName => $"claude-unity-{GetProjectHash()}";

    // 연결 관리
    public static void Connect()      // 비동기 연결 시도 시작
    public static void Disconnect()   // 연결 종료
    public static void Send(string json)  // 메시지 전송 (fire-and-forget)

    // 수신 (Bridge → Unity)
    public static event Action<string> OnMessageReceived;

    // 내부
    static NamedPipeClientStream _pipe;
    static StreamWriter _writer;
    static Thread _readThread;         // 수신 전용 스레드
    static ConcurrentQueue<string> _sendQueue;  // 전송 큐
}
```

**프로젝트 해시:**
```csharp
static string GetProjectHash()
{
    using var md5 = System.Security.Cryptography.MD5.Create();
    var bytes = Encoding.UTF8.GetBytes(ProjectPath);
    var hash = md5.ComputeHash(bytes);
    return BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLower();
}
```

**전송 큐 처리:**
- `Send()`는 큐에 넣기만 함 (메인 스레드 블로킹 방지)
- 별도 스레드에서 큐 drain → Pipe 쓰기
- Pipe 에러 시 상태를 Error로 변경, 3초 후 재연결

**자동 재연결:**
- `EditorApplication.update`에서 주기적 체크
- 상태가 Error/Stopped이고 MonitorEnabled이면 3초마다 연결 시도
- 최대 연속 실패 10회 시 중지 (수동 재시작 필요)

**검증**: Named Pipe 서버(Node.js) 수동 실행 → Unity에서 Connect() → Send() → 서버에서 수신 확인

**의존**: 1-1 (Pipe 이름 규약 공유)

---

### 1-4. mcp-channel.js — MCP Channel 프로토콜

**작업**: MCP Channel 서버 구현

**파일**: `Bridge~/src/mcp-channel.js`

**핵심 구현:**
```javascript
export class McpChannel {
  constructor(server)  // @modelcontextprotocol/sdk의 Server 인스턴스

  // Unity 이벤트를 Claude에 전달
  async sendToClaudeFromUnity(event) {
    // <channel source="unity" category="{event.category}">
    // {formatted message}
    // </channel>
  }

  // Discord 메시지를 Claude에 전달 (Phase 2)
  async sendToClaudeFromDiscord(user, message) { }

  // Claude의 도구 호출 핸들러 등록
  registerTools() {
    // reply: Discord에 메시지 전송
    // send_notification: Discord에 알림
    // set_cooldown: Unity 쿨다운 설정
  }
}
```

**MCP 서버 설정:**
```javascript
const server = new Server({
  name: "claude-unity-bridge",
  version: "1.0.0"
}, {
  capabilities: {
    "claude/channel": {
      instructions: `Unity 에디터와 연결된 채널입니다.
Unity에서 발생하는 에러, 컴파일 결과를 수신합니다.
에러 메시지를 분석하고 해당 파일을 수정해주세요.
수정 후 set_cooldown 도구로 쿨다운을 설정해주세요.`
    }
  }
});
```

**도구 정의:**
```javascript
// Phase 1에서는 set_cooldown만 구현
server.setRequestHandler(CallToolRequestSchema, async (request) => {
  if (request.params.name === "set_cooldown") {
    // PipeServer를 통해 Unity에 쿨다운 메시지 전달
    pipeServer.sendToUnity({
      type: "set_cooldown",
      sourceFile: request.params.arguments.sourceFile,
      seconds: request.params.arguments.seconds
    });
    return { content: [{ type: "text", text: "쿨다운 설정됨" }] };
  }
});
```

**검증**: MCP Inspector 도구로 서버 연결 → capability 확인 → 알림 수신 테스트

**의존**: 1-1, 1-2 (PipeServer 참조)

---

### 1-5. index.js — 진입점

**작업**: 모든 컴포넌트를 조합하는 메인 진입점

**파일**: `Bridge~/src/index.js`

**핵심 구현:**
```javascript
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { PipeServer } from "./pipe-server.js";
import { McpChannel } from "./mcp-channel.js";

// 1. 환경변수에서 설정 읽기
const PIPE_HASH = process.env.CLAUDE_UNITY_PIPE_HASH || "default";
const PIPE_NAME = `\\\\.\\pipe\\claude-unity-${PIPE_HASH}`;

// 2. MCP 서버 생성
const mcpServer = new Server(/* ... */);
const mcpChannel = new McpChannel(mcpServer);

// 3. Named Pipe 서버 시작
const pipeServer = new PipeServer(PIPE_NAME);
pipeServer.on('message', (event) => {
  if (event.type === 'unity_event') {
    mcpChannel.sendToClaudeFromUnity(event);
  } else if (event.type === 'config') {
    // Phase 2: Discord 설정 적용
  }
});

// 4. MCP stdio 전송 시작
const transport = new StdioServerTransport();
await mcpServer.connect(transport);

// 5. Pipe 서버 시작
await pipeServer.start();
```

**환경변수:**
- `CLAUDE_UNITY_PIPE_HASH`: Pipe 이름에 사용할 프로젝트 해시

**검증**: `node src/index.js` 실행 → 프로세스가 stdio 대기 + Pipe 서버 리스닝 확인

**의존**: 1-2, 1-4

---

### 1-6. UnityMonitor.cs — 이벤트 캡처

**작업**: Unity 콘솔/컴파일 이벤트를 캡처하여 Bridge로 전달

**파일**: `Editor/UnityMonitor.cs`

**핵심 구현:**
```csharp
[InitializeOnLoad]
static class UnityMonitor
{
    // 중복 제거
    static Dictionary<string, DuplicateEntry> _duplicates = new();

    // 쿨다운
    static Dictionary<string, double> _cooldowns = new();

    // 초당 리미터
    static int _eventsThisSecond;
    static double _currentSecond;
    const int MaxEventsPerSecond = 10;

    static UnityMonitor()
    {
        Application.logMessageReceived += OnLogMessage;
        CompilationPipeline.compilationFinished += OnCompilationFinished;
        ChannelBridge.OnMessageReceived += OnBridgeMessage;
        EditorApplication.update += OnUpdate;
    }
}
```

**OnLogMessage 처리:**
```
1. MonitorEnabled 체크
2. 심각도 필터 (설정에 따라 Error/Warning/All)
3. 초당 리미터 체크
4. 스택트레이스에서 소스파일:라인 추출
5. 쿨다운 체크 (소스파일 기준)
6. 중복 체크 (메시지 + 소스파일 해시)
7. JSON 직렬화
8. ChannelBridge.Send(json)
```

**OnCompilationFinished 처리:**
```
1. MonitorEnabled 체크
2. CompilerMessage[] 에서 에러만 추출
3. 각 에러를 JSON으로 직렬화
4. ChannelBridge.Send(json)
```

**OnBridgeMessage 처리 (Bridge → Unity):**
```
1. JSON 파싱
2. type == "set_cooldown" → 쿨다운 딕셔너리 업데이트
3. type == "bridge_status" → ChannelBridge 상태 업데이트
```

**스택트레이스 파서:**
```csharp
// "at PlayerController.Update () in Assets/Scripts/PlayerController.cs:42"
// → sourceFile: "Assets/Scripts/PlayerController.cs", sourceLine: 42
static (string file, int line) ParseStackTrace(string stackTrace)
```

**검증**: Unity에서 의도적으로 Debug.LogError → ChannelBridge.Send 호출 확인 (Mock Pipe)

**의존**: 1-3 (ChannelBridge)

---

### 1-7. ClaudeCodeLauncher.cs 수정

**작업**: `--channels` 플래그 추가 및 환경변수 전달

**파일**: `Editor/ClaudeCodeLauncher.cs` (수정)

**변경 대상**: `BuildClaudeCommand()` + `LaunchMain()` + `LaunchClaudeAt()`

**BuildClaudeCommand 변경:**
```csharp
internal static string BuildClaudeCommand()
{
    var sb = new StringBuilder("claude");

    // 기존 추가 인자
    var extra = ClaudeCodeSettings.AdditionalArgs.Trim();
    if (!string.IsNullOrEmpty(extra))
        sb.Append(' ').Append(extra);

    // Channel 플래그 (신규)
    if (ClaudeCodeSettings.MonitorEnabled)
    {
        var bridgePath = GetBridgePath();
        if (!string.IsNullOrEmpty(bridgePath))
            sb.Append($" --channels \"{bridgePath}\"");
    }

    // Remote Control 플래그 (신규)
    if (ClaudeCodeSettings.RemoteControlEnabled)
        sb.Append(" --rc");

    return sb.ToString();
}
```

**Bridge 경로 탐색:**
```csharp
static string GetBridgePath()
{
    // 패키지 경로에서 Bridge~/src/index.js 찾기
    // Packages/com.tjdtjq5.claude/Bridge~/src/index.js
    var packagePath = Path.GetFullPath("Packages/com.tjdtjq5.claude");
    var bridgePath = Path.Combine(packagePath, "Bridge~", "src", "index.js");
    return File.Exists(bridgePath) ? bridgePath.Replace('\\', '/') : null;
}
```

**환경변수 전달:**
LaunchMain/LaunchClaudeAt에서 `CLAUDE_UNITY_PIPE_HASH` 환경변수 설정:
```csharp
// PowerShell 명령에 환경변수 prefix 추가
var envPrefix = $"$env:CLAUDE_UNITY_PIPE_HASH='{ChannelBridge.PipeHash}'; ";
var cmd = envPrefix + BuildClaudeCommand();
```

**검증**: Claude Code 실행 → `--channels` 플래그 포함 확인 → Bridge 프로세스 spawn 확인

**의존**: 1-5 (index.js 존재해야 경로 참조 가능), 1-3 (PipeHash)

---

### 체크포인트 1: 코어 파이프라인 E2E

```
검증 시나리오:
1. Unity 에디터에서 Claude Code 실행 (모니터 활성)
2. Claude Code가 --channels 플래그로 Bridge spawn
3. Bridge가 Named Pipe 서버 시작
4. Unity의 UnityMonitor가 Pipe에 연결
5. Unity에서 Debug.LogError("test error") 실행
6. Bridge가 이벤트 수신 → MCP notification으로 Claude에 전달
7. Claude가 에러를 인식하고 분석 시작

성공 기준:
- Claude Code 세션에서 Unity 에러 메시지가 표시됨
- Claude가 해당 파일을 찾아 분석을 시도함
```

---

## Phase 2: Discord 연동

> Phase 1의 파이프라인에 Discord 양방향 통신을 추가

### 작업 의존성

```
[Phase 1 완료]
     │
     ├──→ 2-1 discord.js 의존성 추가
     │     │
     │     └──→ 2-2 discord-client.js
     │           │
     │           └──→ 2-3 event-router.js ──→ 2-4 index.js 통합
     │
     └──→ 2-5 ClaudeCodeSettings 확장 ──→ 2-6 Settings UI ──→ 2-7 Manager UI
                                                                    │
                                                              [체크포인트 2]
```

---

### 2-1. discord.js 의존성 추가

**작업**: Bridge 프로젝트에 discord.js 추가

**파일**: `Bridge~/package.json` 수정

**변경:**
```json
"dependencies": {
  "@modelcontextprotocol/sdk": "^1.0.0",
  "discord.js": "^14.0.0"
}
```

**완료 조건**: `npm install` 성공

**의존**: Phase 1 완료

---

### 2-2. discord-client.js

**작업**: Discord 봇 클라이언트 구현

**파일**: `Bridge~/src/discord-client.js`

**인터페이스:**
```javascript
export class DiscordClient {
  constructor(options)  // { token, channelId, allowedUsers }

  async connect()
  async disconnect()

  // 이벤트
  on('message', callback)     // 허용된 사용자 메시지
  on('command', callback)     // !명령어
  on('connected', callback)
  on('disconnected', callback)

  // 발신
  async sendMessage(text)
  async sendNotification(text, category)

  // 상태
  get isConnected()
}
```

**메시지 처리:**
```javascript
client.on('messageCreate', (msg) => {
  // 봇 자신 무시
  if (msg.author.bot) return;

  // 지정 채널만
  if (msg.channelId !== this.channelId) return;

  // 허용된 사용자만
  if (!this.allowedUsers.includes(msg.author.id)) return;

  // 명령어 처리
  if (msg.content.startsWith('!')) {
    this.emit('command', parseCommand(msg.content));
    return;
  }

  // 일반 메시지
  this.emit('message', {
    user: msg.author.username,
    userId: msg.author.id,
    content: msg.content,
    attachments: msg.attachments.map(a => a.url)
  });
});
```

**메시지 발신 (2000자 제한 처리):**
```javascript
async sendMessage(text) {
  const chunks = splitMessage(text, 2000);
  for (const chunk of chunks) {
    await this.channel.send(chunk);
  }
}
```

**검증**: Bot Token으로 로그인 → 테스트 채널에 메시지 전송/수신 확인

**의존**: 2-1

---

### 2-3. event-router.js

**작업**: 이벤트 소스별 라우팅 + 모드 관리

**파일**: `Bridge~/src/event-router.js`

**인터페이스:**
```javascript
export class EventRouter {
  constructor(mcpChannel, discordClient, pipeServer)

  // 모드
  get mode()                    // 'off' | 'notify' | 'interactive'
  setMode(mode)
  get isMuted()
  setMute(muted)

  // 이벤트 수신 (각 소스에서 호출)
  handleUnityEvent(event)       // Unity → Claude + (Discord if notify+)
  handleDiscordMessage(msg)     // Discord → Claude (interactive only)
  handleClaudeReply(text)       // Claude reply 도구 → Discord
  handleClaudeNotification(text, category)  // Claude send_notification → Discord
}
```

**라우팅 매트릭스:**

```javascript
handleUnityEvent(event) {
  // 항상 Claude에 전달
  this.mcpChannel.sendToClaudeFromUnity(event);

  // Discord 모드가 notify 이상이고 mute가 아니면 Discord에도 전달
  if (this.mode !== 'off' && !this.isMuted) {
    const formatted = formatUnityEventForDiscord(event);
    this.discordClient.sendNotification(formatted, event.category);
  }
}

handleDiscordMessage(msg) {
  if (this.mode === 'interactive') {
    this.mcpChannel.sendToClaudeFromDiscord(msg.user, msg.content);
  } else if (this.mode === 'notify') {
    this.discordClient.sendMessage("현재 알림 모드입니다. `!mode active`로 전환하세요.");
  }
  // mode === 'off': 무시
}
```

**명령어 처리:**
```javascript
handleCommand(cmd) {
  switch (cmd.name) {
    case 'mode':
      this.setMode(cmd.args[0]);  // off, notify, active
      this.discordClient.sendMessage(`모드 변경: ${this.mode}`);
      // Unity에도 상태 전달
      this.pipeServer.sendToUnity({ type: 'mode_changed', mode: this.mode });
      break;
    case 'mute':
      this.setMute(true);
      this.discordClient.sendMessage("음소거 활성화");
      break;
    case 'unmute':
      this.setMute(false);
      this.discordClient.sendMessage("음소거 해제");
      break;
    case 'status':
      this.sendStatus();
      break;
  }
}
```

**검증**: 각 모드에서 이벤트 라우팅이 올바른지 단위 테스트

**의존**: 2-2, 1-4 (McpChannel)

---

### 2-4. index.js 통합 (Discord 추가)

**작업**: index.js에 Discord 클라이언트 + EventRouter 연결

**파일**: `Bridge~/src/index.js` (수정)

**추가 코드:**
```javascript
import { DiscordClient } from "./discord-client.js";
import { EventRouter } from "./event-router.js";

// Named Pipe에서 config 메시지 수신 시 Discord 초기화
let discordClient = null;
let eventRouter = null;

pipeServer.on('message', async (event) => {
  if (event.type === 'config' && event.discordBotToken) {
    // Discord 클라이언트 (재)초기화
    if (discordClient) await discordClient.disconnect();

    discordClient = new DiscordClient({
      token: event.discordBotToken,
      channelId: event.discordChannelId,
      allowedUsers: event.discordAllowedUsers || []
    });

    eventRouter = new EventRouter(mcpChannel, discordClient, pipeServer);
    eventRouter.setMode(event.discordMode || 'off');

    await discordClient.connect();
  }
  else if (event.type === 'unity_event') {
    if (eventRouter) {
      eventRouter.handleUnityEvent(event);
    } else {
      // Discord 없으면 Claude에만 직접 전달
      mcpChannel.sendToClaudeFromUnity(event);
    }
  }
});
```

**검증**: Unity에서 Discord 설정 전달 → Bridge가 Discord 연결 → 양방향 통신

**의존**: 2-2, 2-3

---

### 2-5. ClaudeCodeSettings.cs 확장

**작업**: Discord 관련 설정 속성 추가

**파일**: `Editor/ClaudeCodeSettings.cs` (수정)

**추가 속성:**
```csharp
// ── Discord ──
public static int DiscordMode
{
    get => EditorPrefs.GetInt(Prefix + "DiscordMode", 0);  // 0=Off
    set => EditorPrefs.SetInt(Prefix + "DiscordMode", value);
}

public static string DiscordBotToken
{
    get => DecryptToken(EditorPrefs.GetString(Prefix + "DiscordToken", ""));
    set => EditorPrefs.SetString(Prefix + "DiscordToken", EncryptToken(value));
}

public static string DiscordChannelId
{
    get => EditorPrefs.GetString(Prefix + "DiscordChannelId", "");
    set => EditorPrefs.SetString(Prefix + "DiscordChannelId", value);
}

public static string DiscordAllowedUsers
{
    get => EditorPrefs.GetString(Prefix + "DiscordUsers", "");
    set => EditorPrefs.SetString(Prefix + "DiscordUsers", value);
}
```

**토큰 암호화:**
```csharp
static string EncryptToken(string token)
{
    if (string.IsNullOrEmpty(token)) return "";
    var bytes = Encoding.UTF8.GetBytes(token);
    var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
    return Convert.ToBase64String(encrypted);
}

static string DecryptToken(string encrypted)
{
    if (string.IsNullOrEmpty(encrypted)) return "";
    try
    {
        var bytes = Convert.FromBase64String(encrypted);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
    catch { return ""; }
}
```

**의존**: Phase 1 완료

---

### 2-6. Settings UI 확장

**작업**: SettingsWindow에 Discord 설정 섹션 추가

**파일**: `Editor/ClaudeCodeSettingsWindow.cs` (수정)

**추가 UI 섹션:**
```
◆ Discord 설정
┌──────────────────────────────┐
│ Bot Token:  [••••••••] [표시] │
│ Channel ID: [___________]    │
│ 허용 사용자: [___________]    │
│ (쉼표로 구분된 Discord ID)    │
└──────────────────────────────┘
```

**구현 포인트:**
- Bot Token은 password 형태 (•••)로 표시, [표시] 토글로 평문 확인
- Channel ID 입력 필드
- 허용 사용자 목록 (쉼표 구분)
- 설정 변경 시 `ChannelBridge.SendConfig()` 호출하여 Bridge에 전달

**의존**: 2-5

---

### 2-7. Manager UI 확장

**작업**: ManagerWindow에 Channel 상태 섹션 추가

**파일**: `Editor/ClaudeCodeManagerWindow.cs` (수정)

**추가 UI 섹션:**
```
◆ Channel 상태
┌──────────────────────────────┐
│ Bridge:  ● 연결됨             │
│ Discord: ● 알림 모드          │
│ 모니터:  ● 활성 (Error만)     │
│                               │
│ [모니터 ON/OFF]  [Discord ▼]  │
└──────────────────────────────┘
```

**구현 포인트:**
- `ChannelBridge.CurrentState`에 따른 상태 인디케이터 (●/○, 색상)
- 모니터 토글 버튼
- Discord 모드 드롭다운 (없음/알림/적극적 사용)
- Discord 모드 변경 시 `ChannelBridge.SendConfig()` → Bridge에 전달 → Bridge에서 모드 전환

**의존**: 2-5, 2-6

---

### 체크포인트 2: Discord E2E

```
검증 시나리오 A (알림 모드):
1. Discord 봇 설정 완료, 모드를 "알림"으로 설정
2. Unity에서 Claude Code 실행
3. Unity에서 Debug.LogError() 발생
4. Discord 채널에 에러 알림이 표시됨
5. Discord에서 메시지 전송 → "현재 알림 모드입니다" 응답

검증 시나리오 B (적극적 사용 모드):
1. Discord에서 !mode active 전송
2. "PlayerController에 점프 기능 추가해줘" 전송
3. Claude가 작업 시작 → 코드 수정
4. Discord에 결과 메시지 표시

검증 시나리오 C (모드 전환):
1. Unity Manager 윈도우에서 Discord 모드를 "적극적 사용"으로 변경
2. Bridge에 즉시 반영됨
3. Discord에서 !mode notify 전송
4. Unity Manager 윈도우에도 상태 반영됨

성공 기준:
- 양방향 모드 전환이 Unity/Discord 양쪽에서 동작
- 알림 모드에서 Unity 이벤트가 Discord에 표시됨
- 적극적 모드에서 Discord → Claude 작업 지시가 동작함
```

---

## Phase 3: Remote Control + 마무리

> Phase 2와 병렬로 진행 가능

### 작업 의존성

```
[Phase 1 완료]
     │
     ├──→ 3-1 RC 설정 추가
     │     │
     │     └──→ 3-2 Launcher --rc 플래그
     │           │
     │           └──→ 3-3 Settings UI RC 토글
     │
     └──→ 3-4 Toolbar 상태 인디케이터 (Phase 2 이후)
           │
      [체크포인트 3]
```

---

### 3-1. RC 설정 추가

**작업**: RemoteControl 설정 속성 추가

**파일**: `Editor/ClaudeCodeSettings.cs` (수정)

```csharp
public static bool RemoteControlEnabled
{
    get => EditorPrefs.GetBool(Prefix + "RC", false);
    set => EditorPrefs.SetBool(Prefix + "RC", value);
}
```

**의존**: Phase 1 완료

---

### 3-2. Launcher --rc 플래그

**작업**: BuildClaudeCommand에 --rc 추가

**파일**: `Editor/ClaudeCodeLauncher.cs` (수정)

1-7에서 이미 설계된 내용 적용.

**의존**: 3-1

---

### 3-3. Settings UI RC 토글

**작업**: SettingsWindow에 RC 토글 추가

**파일**: `Editor/ClaudeCodeSettingsWindow.cs` (수정)

```
◆ Remote Control
┌──────────────────────────────┐
│ [✓] 기본 활성화               │
│ 활성화하면 claude.ai/code나   │
│ 모바일 앱에서 세션에 접속     │
│ 가능합니다.                   │
└──────────────────────────────┘
```

**의존**: 3-1

---

### 3-4. Toolbar 상태 인디케이터

**작업**: 툴바 뱃지에 Channel 연결 상태 표시

**파일**: `Editor/ClaudeToolbar.cs` (수정)

**변경:**
- 기존 뱃지: `✦ Claude [N]`
- 변경 뱃지: `✦ Claude [N] ●`
- `●` 색상:
  - 초록: Bridge Connected + Discord Connected (또는 Discord Off)
  - 노랑: Bridge Connected + Discord Disconnected
  - 빨강: Bridge Error
  - 표시 안함: Monitor 비활성

**구현:**
```csharp
static Label _statusDot;

// BuildButton()에 추가
_statusDot = new Label("●") { style = { color = Color.clear, ... } };
btn.Add(_statusDot);

// BadgePoll()에서 상태 업데이트
static void UpdateStatusDot()
{
    if (!ClaudeCodeSettings.MonitorEnabled)
    {
        _statusDot.style.color = Color.clear;
        return;
    }

    _statusDot.style.color = ChannelBridge.CurrentState switch
    {
        ChannelBridge.State.Connected => new Color(0.3f, 0.85f, 0.4f),  // 초록
        ChannelBridge.State.Connecting => new Color(0.9f, 0.8f, 0.2f),  // 노랑
        _ => new Color(0.9f, 0.3f, 0.3f)                                // 빨강
    };
}
```

**의존**: Phase 2 완료 (ChannelBridge.CurrentState 참조), 1-3

---

### 체크포인트 3: 전체 통합

```
검증 시나리오 (전체 기능):
1. Unity Settings에서 모니터 활성, Discord 알림 모드, RC 활성 설정
2. Manager 윈도우에서 "메인 Claude 실행" 클릭
3. Claude Code가 --channels --rc 플래그로 실행됨
4. Bridge 프로세스 spawn → Named Pipe 연결 → Discord 연결
5. 툴바 뱃지: ✦ Claude [0] ● (초록)
6. Unity에서 에러 발생 → Claude에 전달 → Discord에 알림
7. Discord에서 !mode active → 적극적 모드 전환
8. Discord에서 작업 지시 → Claude 수행 → 결과 Discord에 응답
9. claude.ai/code에서 RC로 접속 → 코드 직접 확인 가능
10. Unity Manager 윈도우에서 Channel 상태 정상 표시

성공 기준:
- 전체 파이프라인이 끊김 없이 동작
- 모드 전환이 양쪽에서 실시간 반영
- RC로 코드 열람 가능
- 에디터 성능 체감 저하 없음
```

---

## package.json 버전 업데이트

**Phase 3 완료 후:**
```json
{
  "name": "com.tjdtjq5.claude",
  "version": "1.0.0"
}
```

---

## 작업 요약

| Phase | 태스크 | 신규 파일 | 수정 파일 |
|-------|--------|----------|----------|
| 1 | 7개 | 6개 (Bridge 4 + Unity 2) | 1개 (Launcher) |
| 2 | 7개 | 2개 (Bridge 2) | 3개 (Settings, SettingsUI, ManagerUI) |
| 3 | 4개 | 0개 | 3개 (Settings, SettingsUI, Toolbar) |
| **합계** | **18개 + 3 체크포인트** | **8개** | **5개 (중복 포함)** |

---

## 다음 단계

`/sc:implement`로 Phase 1부터 순서대로 구현 시작.
각 태스크 완료 시 체크포인트에서 통합 검증 수행.
