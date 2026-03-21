# Phase 2+3 설계: Discord 연동 + RC + UI 확장

> 작성일: 2026-03-21
> 전제: Phase 1 완료 (커밋 2e4f42e)

---

## Phase 1에서 이미 완료된 항목

| 워크플로우 태스크 | 상태 | 비고 |
|-------------------|------|------|
| 2-5. ClaudeCodeSettings 확장 | 완료 | Discord 설정 속성 + DPAPI 암호화 |
| 3-1. RC 설정 추가 | 완료 | RemoteControlEnabled 속성 |
| 3-2. Launcher --rc 플래그 | 완료 | BuildClaudeCommand()에 --rc |

**남은 작업: 8개**

---

## 작업 의존성 (통합)

```
[Phase 1 완료]
     │
     ├──→ A. discord.js 추가 + discord-client.js
     │    │
     │    └──→ B. event-router.js
     │         │
     │         └──→ C. index.js Discord 통합
     │
     └──→ D. SettingsWindow UI 확장 (Monitor + Discord + RC)
     │
     └──→ E. ManagerWindow UI 확장 (Channel 상태 섹션)
     │
     └──→ F. Toolbar 상태 인디케이터
     │
     [체크포인트: 전체 통합]
```

A→B→C는 순차. D, E, F는 독립적으로 병렬 가능.

---

## A. discord-client.js

**파일**: `Bridge~/src/discord-client.js`

```javascript
import { Client, GatewayIntentBits } from 'discord.js';
import { EventEmitter } from 'node:events';

export class DiscordClient extends EventEmitter {
  constructor({ token, channelId, allowedUsers }) { }

  async connect()     // Bot 로그인 + 채널 리스닝
  async disconnect()  // 연결 해제

  // 수신: 허용 사용자 메시지 → emit('message', { user, content })
  // 수신: !명령어 → emit('command', { name, args })
  // 발신
  async sendMessage(text)                    // 일반 메시지 (2000자 분할)
  async sendNotification(text, category)     // 이모지 prefix 포함 알림

  get isConnected
}
```

**알림 포맷팅:**
```javascript
const CATEGORY_PREFIX = {
  error:  '❌',
  build:  '📦',
  commit: '✅',
  info:   'ℹ️',
};

// 코드 블록 자동 감지: ``` 포함 시 그대로, 아니면 텍스트
```

**명령어 파서:**
```javascript
// "!mode active" → { name: 'mode', args: ['active'] }
// "!mute"        → { name: 'mute', args: [] }
// "!status"      → { name: 'status', args: [] }
function parseCommand(content) {
  const parts = content.slice(1).trim().split(/\s+/);
  return { name: parts[0], args: parts.slice(1) };
}
```

---

## B. event-router.js

**파일**: `Bridge~/src/event-router.js`

```javascript
export class EventRouter {
  constructor({ mcpChannel, discordClient, pipeServer }) { }

  // 모드
  get mode()              // 'off' | 'notify' | 'interactive'
  setMode(mode)
  get isMuted()
  setMute(muted)

  // 소스별 핸들러
  handleUnityEvent(event)              // Unity → Claude + Discord(notify+)
  handleDiscordMessage(msg)            // Discord → Claude(interactive만)
  handleDiscordCommand(cmd)            // Discord !명령어 처리
  handleClaudeReply(text)              // Claude reply 도구 → Discord
  handleClaudeNotification(text, cat)  // Claude send_notification → Discord
}
```

**라우팅 매트릭스:**

```
           │ Claude에 전달  │ Discord에 전달
───────────┼────────────────┼─────────────────
Unity 이벤트│ 항상           │ mode≥notify && !muted
Discord MSG │ interactive만  │ ─ (이미 Discord에 있음)
Claude reply│ ─              │ 항상 (mode≥notify)
Claude notif│ ─              │ 항상 (mute 무시)
```

**명령어 처리:**
```javascript
handleDiscordCommand(cmd) {
  switch (cmd.name) {
    case 'mode':
      const newMode = cmd.args[0];  // off, notify, active→interactive
      if (newMode === 'active') this.setMode('interactive');
      else this.setMode(newMode);
      this.discordClient.sendMessage(`모드: ${this.mode}`);
      this.pipeServer.sendToUnity({ type: 'mode_changed', mode: this.mode });
      break;

    case 'mute':   this.setMute(true);  this.discordClient.sendMessage('🔇 음소거'); break;
    case 'unmute': this.setMute(false); this.discordClient.sendMessage('🔊 음소거 해제'); break;

    case 'status':
      this.discordClient.sendMessage(
        `📊 상태\n` +
        `• 모드: ${this.mode}\n` +
        `• 음소거: ${this.isMuted ? 'ON' : 'OFF'}\n` +
        `• Unity: ${this.pipeServer.isConnected ? '연결됨' : '미연결'}`
      );
      break;
  }
}
```

---

## C. index.js Discord 통합

**파일**: `Bridge~/src/index.js` (수정)

Phase 2 스텁을 실제 구현으로 교체:

```javascript
import { DiscordClient } from './discord-client.js';
import { EventRouter } from './event-router.js';

// handleConfig에서 Discord 초기화
async function handleConfig(config) {
  if (config.discordMode === 'off' || !config.discordBotToken) {
    // Discord 비활성 — 기존 연결 해제
    if (discordClient) {
      await discordClient.disconnect();
      discordClient = null;
      eventRouter = null;
    }
    pipeServer.sendToUnity({ type: 'bridge_status', status: 'connected', message: 'Discord OFF' });
    return;
  }

  // Discord (재)연결
  if (discordClient) await discordClient.disconnect();

  discordClient = new DiscordClient({
    token: config.discordBotToken,
    channelId: config.discordChannelId,
    allowedUsers: (config.discordAllowedUsers || '').split(',').map(s => s.trim()).filter(Boolean),
  });

  eventRouter = new EventRouter({ mcpChannel, discordClient, pipeServer });
  eventRouter.setMode(config.discordMode);

  discordClient.on('message', (msg) => eventRouter.handleDiscordMessage(msg));
  discordClient.on('command', (cmd) => eventRouter.handleDiscordCommand(cmd));
  discordClient.on('connected', () => {
    pipeServer.sendToUnity({ type: 'bridge_status', status: 'discord_connected' });
  });
  discordClient.on('disconnected', () => {
    pipeServer.sendToUnity({ type: 'bridge_status', status: 'discord_disconnected' });
  });

  await discordClient.connect();
}
```

**onToolCall 수정:**
```javascript
// reply, send_notification이 eventRouter를 통해 동작하도록 이미 구현됨
// handleConfig에서 eventRouter 할당 후 자동으로 작동
```

---

## D. SettingsWindow UI 확장

**파일**: `Editor/ClaudeCodeSettingsWindow.cs` (수정)

**OnEnable에 신규 상태 로드:**
```csharp
bool _monitorEnabled;
int _monitorSeverity;
int _cooldownSeconds;
int _discordMode;
string _discordBotToken;
string _discordChannelId;
string _discordAllowedUsers;
bool _remoteControlEnabled;
bool _showToken;  // 토큰 표시 토글

void OnEnable() {
    // 기존...
    _monitorEnabled = ClaudeCodeSettings.MonitorEnabled;
    _monitorSeverity = ClaudeCodeSettings.MonitorSeverity;
    _cooldownSeconds = ClaudeCodeSettings.CooldownSeconds;
    _discordMode = ClaudeCodeSettings.DiscordMode;
    _discordBotToken = ClaudeCodeSettings.DiscordBotToken;
    _discordChannelId = ClaudeCodeSettings.DiscordChannelId;
    _discordAllowedUsers = ClaudeCodeSettings.DiscordAllowedUsers;
    _remoteControlEnabled = ClaudeCodeSettings.RemoteControlEnabled;
}
```

**OnGUI에 신규 섹션 추가 (워크트리 동작 섹션과 사용법 안내 사이에):**

```
기존 섹션:
  1. Claude 명령어
  2. Windows Terminal
  3. 워크트리 동작
  ──── 여기에 추가 ────
  4. 모니터 설정      (신규)
  5. Discord 설정     (신규)
  6. Remote Control   (신규)
  ────────────────────
  7. 사용법 안내
```

### 모니터 설정 섹션

```
◆ 모니터 설정        [초록 헤더]
┌───────────────────────────────┐
│ [✓] 모니터 활성화              │
│ 전달 심각도: [Error만 ▼]      │
│             Error만 / Warning+ / All
│ 쿨다운:     [30] 초           │
│ (수정 후 에러 재전달 방지 시간) │
└───────────────────────────────┘
```

### Discord 설정 섹션

```
◆ Discord 설정       [파란 헤더]
┌───────────────────────────────┐
│ 모드: [없음 ▼]                │
│       없음 / 알림 / 적극적 사용
│                               │
│ ── 모드가 "없음"이 아닐 때 ── │
│ Bot Token:   [••••••••] [표시] │
│ Channel ID:  [___________]    │
│ 허용 사용자: [___________]    │
│ (쉼표로 구분된 Discord ID)     │
│                               │
│ [설정 적용]                    │
└───────────────────────────────┘
```

- Discord 모드가 "없음"이면 하위 필드 비활성(DisabledGroup)
- Bot Token은 `_showToken` 토글로 평문/마스크 전환
- "설정 적용" 버튼 → `ChannelBridge.SendConfig()` 호출

### Remote Control 섹션

```
◆ Remote Control     [보라 헤더]
┌───────────────────────────────┐
│ [✓] 기본 활성화               │
│ claude.ai/code나 모바일 앱에서 │
│ 세션에 접속할 수 있습니다.     │
└───────────────────────────────┘
```

---

## E. ManagerWindow UI 확장

**파일**: `Editor/ClaudeCodeManagerWindow.cs` (수정)

**OnGUI에 Channel 상태 섹션 추가 (메인 실행 버튼 아래, 워크트리 위):**

```
기존:
  1. 배너
  2. 메인 Claude 실행
  ──── 여기에 추가 ────
  3. Channel 상태      (신규)
  ────────────────────
  4. 워크트리
```

### Channel 상태 섹션 UI

```
◆ Channel             [초록 헤더]
┌───────────────────────────────┐
│ Bridge:  ● 연결됨   [ON/OFF]  │
│ Discord: ● 알림     [모드 ▼]  │
│ 모니터:  ● Error만            │
└───────────────────────────────┘
```

**상태 인디케이터 (●) 색상:**

| 컴포넌트 | 초록 | 노랑 | 빨강 | 회색 |
|----------|------|------|------|------|
| Bridge | Connected | Connecting | Error | Stopped |
| Discord | Connected | - | Disconnected | Off |
| 모니터 | Enabled | - | - | Disabled |

**구현:**
```csharp
// 상태 추적 — Bridge 메시지 수신 시 업데이트
static string _discordStatus = "off";   // "off", "connected", "disconnected"

// ChannelBridge.OnMessageReceived에서 bridge_status 메시지 파싱
// → _discordStatus 업데이트 → Repaint()
```

**컨트롤:**
- Bridge ON/OFF 토글 → `ChannelBridge.Connect()` / `Disconnect()`
- Discord 모드 드롭다운 → `ClaudeCodeSettings.DiscordMode` + `ChannelBridge.SendConfig()`

---

## F. Toolbar 상태 인디케이터

**파일**: `Editor/ClaudeToolbar.cs` (수정)

**변경:**
```
현재: ✦ Claude [N]
변경: ✦ Claude [N] ●
```

**● 표시 로직:**
```csharp
static Label _statusDot;

static Color GetStatusColor()
{
    if (!ClaudeCodeSettings.MonitorEnabled)
        return Color.clear;  // 숨김

    return ChannelBridge.CurrentState switch
    {
        ChannelBridge.State.Connected => new Color(0.3f, 0.85f, 0.4f),  // 초록
        ChannelBridge.State.Connecting => new Color(0.9f, 0.8f, 0.2f),  // 노랑
        ChannelBridge.State.Error => new Color(0.9f, 0.3f, 0.3f),       // 빨강
        _ => new Color(0.5f, 0.5f, 0.5f),                                // 회색
    };
}
```

**BadgePoll에서 업데이트:**
```csharp
static void BadgePoll()
{
    // 기존 워크트리 카운트 업데이트...

    // 상태 인디케이터 업데이트
    if (_statusDot != null)
        _statusDot.style.color = GetStatusColor();
}
```

---

## 구현 순서

```
Step 1: Bridge JS 3파일 (A→B→C)         — Node.js 쪽 완성
Step 2: SettingsWindow UI (D)            — 설정을 UI에서 조작 가능하게
Step 3: ManagerWindow UI (E)             — 상태 모니터링 가능하게
Step 4: Toolbar 인디케이터 (F)           — 한눈에 상태 확인
Step 5: 통합 검증                        — E2E 테스트
```

Step 1은 JS만, Step 2-4는 C# UI만이라 별개 영역.
Step 1과 Step 2-4를 병렬로 진행하면 가장 빠름.

---

## npm 의존성 추가

```json
// Bridge~/package.json에 추가
"dependencies": {
  "@modelcontextprotocol/sdk": "^1.27.0",
  "discord.js": "^14.0.0"    // 추가
}
```
