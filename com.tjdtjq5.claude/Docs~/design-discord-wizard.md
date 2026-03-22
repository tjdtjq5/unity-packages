# Discord 설정 위자드 설계

> 작성일: 2026-03-22
> 대상: ClaudeCodeSettingsWindow.cs의 Discord 설정 섹션 리팩토링

## 현재 문제

```
Bot Token:   [                    ] [표시]   ← 어디서 가져오지?
Channel ID:  [                    ]          ← 이게 뭔데?
허용 사용자: [                    ]          ← 내 ID가 뭐야?
[설정 적용]
```

- 모든 값을 사용자가 직접 찾아서 복사-붙여넣기 해야 함
- Discord 개발자 포털 URL도 없음
- 설정 순서가 불명확

## 설계: 3단계 위자드

기존 단일 섹션을 **단계별 위자드**로 교체.
`_wizardStep` 상태로 현재 단계를 추적.

---

### Step 1: 봇 생성 + 토큰 입력

```
┌─────────────────────────────────────────┐
│ ◆ Discord 설정                   [1/3] │
│ ┌─────────────────────────────────────┐ │
│ │ Step 1: Discord 봇 생성            │ │
│ │                                     │ │
│ │ [🔗 개발자 포털 열기]               │ │
│ │                                     │ │
│ │ 1. "New Application" → 이름 입력    │ │
│ │ 2. 좌측 "Bot" 탭 클릭              │ │
│ │ 3. "Reset Token" → 토큰 복사       │ │
│ │                                     │ │
│ │ Bot Token:                          │ │
│ │ [••••••••••••••••••••] [표시]       │ │
│ │                                     │ │
│ │              [다음 →]               │ │
│ └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

**"개발자 포털 열기" 버튼:**
```csharp
Application.OpenURL("https://discord.com/developers/applications");
```

**"다음" 활성 조건:** `_discordBotToken`이 비어있지 않음

---

### Step 2: 봇 초대 + 채널 선택

```
┌─────────────────────────────────────────┐
│ ◆ Discord 설정                   [2/3] │
│ ┌─────────────────────────────────────┐ │
│ │ Step 2: 봇 초대 + 채널 선택        │ │
│ │                                     │ │
│ │ [🔗 서버에 봇 초대하기]             │ │
│ │                                     │ │
│ │ 봇을 서버에 추가한 후              │ │
│ │ 채널 목록을 불러오세요:             │ │
│ │                                     │ │
│ │ [채널 목록 불러오기]                │ │
│ │                                     │ │
│ │ ┌─ 채널 선택 ───────────────────┐  │ │
│ │ │ ○ #general                    │  │ │
│ │ │ ● #claude-bot                 │  │ │
│ │ │ ○ #dev-log                    │  │ │
│ │ └───────────────────────────────┘  │ │
│ │                                     │ │
│ │ 또는 직접 입력:                     │ │
│ │ Channel ID: [___________________]   │ │
│ │                                     │ │
│ │       [← 이전]    [다음 →]          │ │
│ └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

**"서버에 봇 초대하기" 버튼:**
```csharp
// Bot Token에서 Client ID 자동 추출
// 토큰 형식: {base64(clientId)}.{timestamp}.{hmac}
static string ExtractClientId(string token)
{
    var firstDot = token.IndexOf('.');
    if (firstDot < 0) return null;
    var base64 = token.Substring(0, firstDot);
    // Base64 패딩 보정
    base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
    var bytes = Convert.FromBase64String(base64);
    return Encoding.UTF8.GetString(bytes);
}

// 초대 URL 생성 (필요 권한 자동 포함)
// 2048 = Send Messages
// 1024 = Read Message History
// 65536 = Read Messages/View Channels
// 32768 = Manage Messages (edit_message용)
var permissions = 2048 | 1024 | 65536 | 32768;
var url = $"https://discord.com/oauth2/authorize?client_id={clientId}&permissions={permissions}&scope=bot";
Application.OpenURL(url);
```

**"채널 목록 불러오기" 버튼:**

Bridge 프로세스를 임시로 실행하여 채널 목록을 가져옴:
```csharp
// Node.js 스크립트로 채널 목록 조회
// Bridge~/src/index.js와 별도로, 간단한 1회성 스크립트 실행
var script = $"node -e \"" +
    "const { Client, GatewayIntentBits } = require('discord.js'); " +
    "const c = new Client({ intents: [GatewayIntentBits.Guilds] }); " +
    $"c.on('ready', () => {{ " +
    "  const channels = c.channels.cache" +
    "    .filter(ch => ch.type === 0)" +  // TextChannel만
    "    .map(ch => ch.id + '|' + ch.name + '|' + ch.guild.name)" +
    "    .join('\\n'); " +
    "  console.log(channels); " +
    "  c.destroy(); " +
    "}}); " +
    $"c.login('{token}');\"";
```

결과를 파싱하여 드롭다운 또는 라디오 버튼 목록으로 표시.

**"다음" 활성 조건:** `_discordChannelId`가 비어있지 않음

---

### Step 3: 확인 + 테스트

```
┌─────────────────────────────────────────┐
│ ◆ Discord 설정                   [3/3] │
│ ┌─────────────────────────────────────┐ │
│ │ Step 3: 확인 + 테스트              │ │
│ │                                     │ │
│ │ ✅ Bot Token: 설정됨               │ │
│ │ ✅ Channel: #claude-bot            │ │
│ │    (서버: My Dev Server)            │ │
│ │                                     │ │
│ │ 허용 사용자 (선택):                 │ │
│ │ [채널 멤버 불러오기]                │ │
│ │ ┌───────────────────────────────┐  │ │
│ │ │ [✓] tjdtjq5                   │  │ │
│ │ │ [ ] other-user                │  │ │
│ │ └───────────────────────────────┘  │ │
│ │ (비워두면 모든 사용자 허용)          │ │
│ │                                     │ │
│ │ [🔔 테스트 메시지 보내기]           │ │
│ │  → ✅ "Hello from Unity!" 전송 성공 │ │
│ │                                     │ │
│ │       [← 이전]    [완료 ✓]          │ │
│ └─────────────────────────────────────┘ │
└─────────────────────────────────────────┘
```

**"채널 멤버 불러오기":**
Step 2와 유사하게 Node.js로 멤버 목록 조회.

**"테스트 메시지 보내기":**
```csharp
// 1회성 Node.js 스크립트로 메시지 전송
var script = $"node -e \"" +
    "const { Client, GatewayIntentBits } = require('discord.js'); " +
    "const c = new Client({ intents: [GatewayIntentBits.Guilds] }); " +
    $"c.on('ready', async () => {{ " +
    $"  const ch = await c.channels.fetch('{channelId}'); " +
    "  await ch.send('🔗 Unity 연결 테스트 — Claude Code 패키지에서 전송됨'); " +
    "  console.log('OK'); " +
    "  c.destroy(); " +
    "}}); " +
    $"c.login('{token}');\"";
```

성공하면 ✅ 표시, 실패하면 에러 메시지 표시.

**"완료" 버튼:**
- 설정 저장 (ClaudeCodeSettings)
- ChannelBridge.SendConfig() 호출
- 위자드 닫고 일반 Discord 상태 표시로 전환

---

### 위자드 완료 후 표시 (일반 모드)

위자드 완료 후 Settings에는 간략한 상태만 표시:

```
◆ Discord 설정
┌─────────────────────────────────────┐
│ 모드: [알림 ▼]                      │
│                                     │
│ ✅ Bot: Claude Unity                │
│ ✅ Channel: #claude-bot             │
│ ✅ 허용: tjdtjq5                    │
│                                     │
│ [설정 변경]  [테스트]  [연결 해제]   │
└─────────────────────────────────────┘
```

- [설정 변경] → 위자드 Step 1로 돌아감
- [테스트] → 테스트 메시지 전송
- [연결 해제] → 설정 초기화 + Discord 모드 OFF

---

## 구현 파일

### 수정 파일

| 파일 | 변경 |
|------|------|
| `ClaudeCodeSettingsWindow.cs` | Discord 섹션 → 위자드 UI로 교체 |
| `ClaudeCodeSettings.cs` | `DiscordBotName`, `DiscordChannelName`, `DiscordServerName` 속성 추가 (표시용) |

### 신규 파일

| 파일 | 역할 |
|------|------|
| `DiscordSetupHelper.cs` | Client ID 추출, 초대 URL 생성, 채널/멤버 조회 스크립트 실행 |

### Bridge 수정

| 파일 | 변경 |
|------|------|
| `Bridge~/src/discord-helper.js` (신규) | 채널 목록, 멤버 목록, 테스트 메시지 전송용 1회성 스크립트 |

---

## 상태 관리

```csharp
// SettingsWindow 내부 상태
int _wizardStep;          // 0=미시작(일반모드), 1, 2, 3
bool _isLoadingChannels;  // 채널 목록 로딩 중
bool _isLoadingMembers;   // 멤버 목록 로딩 중
bool _isTesting;          // 테스트 메시지 전송 중
string _testResult;       // "OK" 또는 에러 메시지

// 채널/멤버 목록 (조회 결과 캐시)
struct ChannelInfo { string id; string name; string serverName; }
struct MemberInfo { string id; string username; }
List<ChannelInfo> _channels;
List<MemberInfo> _members;
int _selectedChannelIdx;
HashSet<int> _selectedMemberIdxs;
```

## 자동화 요약

| 항목 | 수동 → 자동 |
|------|------------|
| 개발자 포털 URL | 직접 검색 → **버튼 클릭** |
| Client ID | 수동 입력 → **토큰에서 자동 추출** |
| 초대 링크 | 직접 URL 구성 → **권한 포함 자동 생성** |
| Channel ID | 개발자 모드로 복사 → **채널 목록에서 클릭 선택** |
| 허용 사용자 | Discord ID 찾기 → **멤버 목록에서 체크** |
| 연결 테스트 | 없음 → **테스트 메시지 버튼** |

**사용자가 할 일: 토큰 복사 1번 + 클릭 6번**
