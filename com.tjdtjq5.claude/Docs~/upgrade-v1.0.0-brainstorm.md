# com.tjdtjq5.claude v1.0.0 업그레이드 브레인스토밍

> 작성일: 2026-03-21
> 현재 버전: v0.3.0 (런처 + 워크트리 관리)
> 목표 버전: v1.0.0 (런처 + Channel 통합 + Remote Control)

---

## 배경

Claude Code에서 새로 출시된 기능들을 활용하여 `com.tjdtjq5.claude` 패키지를 업그레이드한다.

### 관련 Claude Code 신규 기능

| 기능 | 설명 | 출시 시기 |
|------|------|----------|
| **Cron/Scheduling** | 세션 내 반복 프롬프트 예약. 5-field cron, 최대 50개, 3일 만료 | v2.1.71 (2026.03) |
| **Channels** | 외부 메시징 플랫폼이 Claude Code 세션에 메시지 push. MCP 서버 기반 양방향 통신 | v2.1.80 (2026.03.20) |
| **Remote Control** | 로컬 터미널 세션을 claude.ai/code, 모바일 앱에서 제어. `--rc` 플래그 | 2026.02.25 |

### 업그레이드 대상 기능 선정

- **Cron** → 보류. 활용도가 아직 불명확
- **Channels** → 채택. Unity ↔ Claude 양방향 통신, Discord 연동
- **Remote Control** → 채택. 코드를 직접 볼 수 있는 강점

---

## 우선순위

| 순위 | 기능 | 핵심 가치 |
|------|------|----------|
| **P1** | Unity 콘솔 → Claude 자동 전달 | 복사-붙여넣기 사이클 제거, 에러 대응 속도 혁신 |
| **P2** | Discord 연동 및 알림 | 어디서든 작업 지시 + 결과 알림 수신 |
| **P3** | Remote Control 지원 | 코드를 직접 보면서 깊은 디버깅/리뷰 |

---

## P1. Unity 콘솔 → Claude 자동 전달

### 사용자 스토리

> Unity에서 플레이/편집 중 에러가 발생하면, 수동 복사 없이 Claude가 자동으로 인지하고 분석/수정할 수 있어야 한다.

### 현재 vs 개선

**현재 워크플로우 (7단계):**
> 플레이 → 에러 발생 → 콘솔 클릭 → 스택트레이스 복사 → Claude 터미널로 전환 → 붙여넣기 → "고쳐줘"

**Channel 워크플로우 (2단계):**
> 플레이 → 에러 발생 → (자동) Claude가 분석 + 수정

### 기능 요구사항

| ID | 요구사항 | 수용 기준 |
|----|---------|----------|
| P1-1 | 콘솔 에러/예외 자동 캡처 | `LogError`, `LogException` 발생 시 메시지 + 스택트레이스 수집 |
| P1-2 | 컴파일 에러 캡처 | `CompilationPipeline` 이벤트에서 컴파일 실패 정보 수집 |
| P1-3 | 심각도 필터링 | Error / Exception / Warning 중 전달 대상 선택 가능 |
| P1-4 | 중복 제거 | 같은 에러 반복 시 첫 건만 전달 + 반복 횟수 태그 |
| P1-5 | 쿨다운 | 에러 전달 후 N초간 같은 소스 파일 에러 무시 (수정 대기) |
| P1-6 | Channel 브릿지 전송 | 수집된 이벤트를 MCP Channel 서버로 전달 |
| P1-7 | on/off 토글 | 에디터 UI에서 자동 전달 활성/비활성 전환 |

### 전달 데이터 종류

| Unity 이벤트 | Claude가 할 수 있는 것 |
|---|---|
| `LogError` + 스택트레이스 | 해당 라인 찾아서 null 체크/예외처리 추가 |
| `LogWarning` | 패턴 분석 — 같은 경고 반복되면 근본 원인 추적 |
| `LogException` | 전체 콜스택 분석 → 어디서 잘못된 호출인지 추적 |
| 컴파일 에러 | CompilationPipeline 이벤트 → 즉시 수정 시도 |

### 데이터 흐름

```
Unity Editor
  ├─ Application.logMessageReceived  ─┐
  ├─ CompilationPipeline.assemblyCompilationFinished ─┤
  └─ (향후) ProfilerRecorder ─────────┘
           │
     [필터 / 중복제거 / 쿨다운]
           │
     [IPC: Named Pipe]
           │
     [Channel Bridge - Node.js MCP Server]
           │
     [Claude Code 세션]
       → 분석 → 수정 → 커밋
```

### 비기능 요구사항

- 에디터 성능에 영향 최소화 (이벤트 수집은 비동기)
- Claude Code 세션이 없을 때는 이벤트 버림 (큐잉 X)
- 브릿지 프로세스 크래시 시 Unity 에디터에 영향 없음

---

## P2. Discord 연동 및 알림

### 사용자 스토리

> Discord에서 Claude Code 세션에 작업 지시를 보내고, Claude의 작업 결과를 알림으로 받을 수 있어야 한다. 알림을 on/off 전환할 수 있어야 한다.

### 기능 요구사항

| ID | 요구사항 | 수용 기준 |
|----|---------|----------|
| P2-1 | Discord → Claude 메시지 전달 | Discord 특정 채널에 메시지 → Claude Code 세션이 수신 |
| P2-2 | Claude → Discord 응답 | Claude 작업 완료 시 Discord 채널에 결과 메시지 |
| P2-3 | 알림 on/off 토글 (Unity) | Unity 에디터 UI에서 Discord 알림 활성/비활성 전환 |
| P2-4 | 알림 on/off 토글 (Discord) | Discord에서 명령어로도 알림 토글 가능 |
| P2-5 | sender 허용목록 | 특정 Discord 사용자만 Claude에 지시 가능 |
| P2-6 | P1 이벤트 Discord 전달 | Unity 콘솔 에러 등 P1 이벤트를 Discord에도 알림 가능 |
| P2-7 | 알림 카테고리 | 에러/빌드/커밋 등 카테고리별 알림 on/off |

### Discord 모드 (3단계)

```
[없음 OFF]  ──→  [알림 Notification]  ──→  [적극적 사용 Interactive]
```

#### 없음 (OFF)

- Discord 연동 완전 비활성
- 브릿지가 Discord에 연결하지 않음
- **기본값**

#### 알림 모드 (Notification)

- **방향**: Claude → Discord (단방향)
- **Discord → Claude 메시지 무시됨**
- 작업 완료/에러 등 핵심 이벤트만 알림

전달하는 것:
```
✅ 작업 완료       "wt-1: PlayerController.cs 수정 완료, 커밋 abc1234"
❌ 컴파일 에러     "컴파일 에러 3건 발생"
🔧 자동 수정 완료  "컴파일 에러 3건 자동 수정됨"
📦 빌드 결과      "Android 빌드 성공 (52MB, 8분 32초)"
⚠️ 충돌 경고      "wt-2: main과 머지 충돌 예상"
```

전달하지 않는 것:
```
✗ Claude의 중간 과정 ("파일 읽는 중...", "분석 중...")
✗ 사소한 경고
✗ 디버그 로그
```

#### 적극적 사용 모드 (Interactive)

- **방향**: Discord ↔ Claude (양방향)
- 컴퓨터 앞에 없을 때 Discord가 메인 인터페이스

**Discord에서 할 수 있는 것:**

일반 작업 지시:
```
유저: PlayerController에 대시 기능 추가해줘
Claude: 작업 시작합니다.

        구현 계획:
        1. DashState enum 추가
        2. Dash() 메서드 구현
        3. InputSystem 바인딩

        진행할까요?
유저: ㅇㅇ
Claude: ✅ 완료
        • PlayerController.cs - Dash() 추가 (+42줄)
        • PlayerInputActions - Dash 액션 추가
        • 커밋: feat: add dash to PlayerController (abc1234)
```

코드 확인 요청:
```
유저: PlayerController.cs의 Move 메서드 보여줘
Claude: ```csharp
        // PlayerController.cs:28-45
        public void Move(Vector2 input) {
            var direction = new Vector3(input.x, 0, input.y);
            direction = camera.TransformDirection(direction);
            direction.y = 0;
            controller.Move(direction * speed * Time.deltaTime);
        }
        ```

유저: speed를 SerializeField로 바꿔줘
Claude: ✅ 수정 완료
        - public float speed = 5f;
        + [SerializeField] private float speed = 5f;
        커밋: abc1235
```

상태 조회:
```
유저: 상태
Claude: 📊 프로젝트 상태
        • 브랜치: wt-1/feature-dash
        • 컴파일: ✅ 정상
        • 변경 파일: 3개 (uncommitted)
        • 최근 커밋: feat: add dash (2분 전)
```

파일 검색:
```
유저: Health 관련 파일 뭐 있어?
Claude: 🔍 검색 결과:
        • Scripts/Player/HealthSystem.cs
        • Scripts/UI/HealthBarUI.cs
        • ScriptableObjects/HealthConfig.asset
        • Tests/HealthSystemTests.cs
```

git 작업:
```
유저: 지금까지 한거 PR 만들어줘
Claude: 📋 PR #43 생성
        제목: feat: add player dash ability
        브랜치: wt-1 → main
        변경: +87줄, -3줄, 4파일
        URL: github.com/...
```

#### 모드별 기능 비교

| 기능 | 없음 | 알림 | 적극적 사용 |
|------|------|------|------------|
| 완료/에러 알림 | X | O | O |
| 작업 지시 | X | X | O |
| 코드 열람 | X | X | O |
| 대화형 응답 | X | X | O (계획 확인, 질문 등) |
| 상태 조회 | X | X | O |
| git 작업 | X | X | O |
| 중간 진행 보고 | X | X | O (작업 규모 클 때) |

#### Discord 명령어

```
!mode notify    → 알림 모드로 전환
!mode active    → 적극적 사용 모드로 전환
!mode off       → 연동 해제
!mute           → 일시 음소거 (모드 유지, 알림만 중단)
!unmute         → 음소거 해제
!status         → 현재 프로젝트 상태
```

### Discord 메시지 예시

```
[수신] 유저 → Discord:
  "PlayerController에 대시 기능 추가해줘"

[발신] Claude → Discord:
  ✅ 완료
  • PlayerController.cs에 Dash() 메서드 추가
  • InputSystem에 Dash 액션 바인딩
  • 커밋: abc1234 (wt-1)

[자동 알림] Unity → Discord:
  ⚠️ 컴파일 에러 2건
  • PlayerController.cs:42 - CS0103: 'dashSpeed' does not exist
  • PlayerMovement.cs:15 - CS0246: type 'DashConfig' not found

[자동 알림] Claude → Discord:
  🔧 컴파일 에러 2건 자동 수정 완료
  • dashSpeed 필드 선언 추가
  • DashConfig 클래스 생성
```

---

## P3. Remote Control 지원

### 사용자 스토리

> Unity 에디터에서 Claude Code 세션의 Remote Control을 활성화하고, 세션 URL을 확인할 수 있어야 한다. 코드를 직접 보면서 지시할 수 있는 것이 핵심 가치.

### 기능 요구사항

| ID | 요구사항 | 수용 기준 |
|----|---------|----------|
| P3-1 | `--rc` 플래그 지원 | 런처 실행 시 Remote Control 활성화 옵션 |
| P3-2 | 설정 UI | Settings 윈도우에서 RC 기본 on/off 설정 |
| P3-3 | Manager 윈도우 표시 | 워크트리 목록에서 RC 활성 세션 표시 |

### Discord와의 역할 분담

```
Discord (P2)              Remote Control (P3)
─────────────             ──────────────────
텍스트 지시               코드 직접 열람 + 지시
푸시 알림 수신            파일 탐색/diff 확인
간단한 작업 지시          복잡한 디버깅/리뷰
"대시 기능 추가해줘"       "이 함수 로직 이상한데 같이 보자"
항상 접근 가능            세션 URL 필요
```

→ **Discord = 일상적 소통**, **RC = 깊은 작업**으로 공존

---

## 기술 결정 사항

### IPC 방식: Named Pipe

| 비교 | Named Pipe | TCP |
|------|-----------|-----|
| 보안 | 로컬 전용, 외부 접근 불가 | localhost 바인딩해도 방화벽 이슈 가능 |
| 설정 | 이름만 정하면 됨 | 포트 충돌 관리 필요 |
| 성능 | 약간 더 빠름 (커널 직접) | 약간의 네트워크 스택 오버헤드 |
| 크로스플랫폼 | Windows와 Unix 방식이 다름 | 동일 |

**결정: Named Pipe**
- Unity ↔ 브릿지는 항상 같은 PC → 네트워크 불필요
- 포트 충돌 없음 (워크트리 여러 개 동시 실행해도)
- 보안상 외부 접근 원천 차단
- Windows 주 타겟이므로 크로스플랫폼 문제 없음

### Discord 봇 호스팅: 로컬

| 비교 | 로컬 | 클라우드 서버 |
|------|------|-------------|
| 구조 | Channel MCP 서버가 Discord 봇 역할 겸임 | 별도 서버에서 봇 상시 운영 |
| 장점 | 추가 인프라 불필요, 설치 간단 | PC 꺼져도 동작 |
| 단점 | PC 꺼지면 Discord도 끊김 | 서버 비용, 관리 부담 |

**결정: 로컬**
- Channel MCP 서버는 Claude Code의 서브프로세스 → 어차피 로컬 필수
- PC 꺼지면 Claude Code도 꺼지니까 봇만 살아있어도 무의미
- 나중에 필요하면 분리 가능

### 브릿지 위치: 패키지 내 Bridge~/

| 비교 | 패키지 내 (Bridge~/) | 별도 리포 |
|------|---------------------|----------|
| 설치 | 패키지 설치하면 끝 | 패키지 + 브릿지 따로 설치 |
| 업데이트 | 패키지 버전과 항상 동기화 | 버전 불일치 가능 |
| 패키지 크기 | node_modules 포함 시 커짐 | Unity 패키지는 가벼움 |

**결정: 패키지 내 Bridge~/**
- 설치 편의성이 압도적 — 하나 깔면 끝
- `Bridge~/` 폴더는 Unity가 무시 (`~` 접미사)
- `node_modules`는 `.gitignore` 처리, 설치 시 `npm install` 한 번만 실행

---

## 프로젝트 구조 (목표)

```
com.tjdtjq5.claude v1.0.0
├── Editor/
│   ├── (기존) ClaudeCodeLauncher.cs       ← --rc 플래그 추가
│   ├── (기존) ClaudeCodeManagerWindow.cs  ← 모니터 상태, RC 표시 추가
│   ├── (기존) ClaudeCodeSettings.cs       ← 새 설정 항목들
│   ├── (기존) ClaudeCodeSettingsWindow.cs ← 새 설정 UI
│   ├── (기존) ClaudeToolbar.cs            ← 뱃지에 Channel 상태 추가
│   ├── (신규) UnityMonitor.cs             ← 콘솔/컴파일/성능 이벤트 수집
│   ├── (신규) ChannelBridge.cs            ← 브릿지 프로세스 관리 (시작/중지/상태)
│   └── (신규) NotificationSettings.cs     ← 알림 카테고리별 on/off
│
├── Bridge~/
│   ├── package.json                       ← Node.js MCP Channel 서버
│   ├── src/
│   │   ├── unity-channel.js               ← Unity ↔ Claude 브릿지
│   │   └── discord-channel.js             ← Discord ↔ Claude 브릿지
│   └── ...
│
├── Docs~/
│   └── upgrade-v1.0.0-brainstorm.md       ← 이 문서
│
└── package.json                           ← 버전 1.0.0
```

---

## 성능 모니터링 확장 (향후)

Channel은 로그뿐 아니라 Unity에서 측정 가능한 모든 것을 보낼 수 있다.

### 데이터 레이어

```
Layer 1: 콘솔 로그        → Application.logMessageReceived (P1에서 구현)
Layer 2: 프레임 성능       → Time.deltaTime, fps 모니터링 (커스텀)
Layer 3: 메모리           → Profiler.GetTotalAllocatedMemoryLong() (커스텀)
Layer 4: GC              → ProfilerRecorder (커스텀)
Layer 5: 물리/렌더링      → Physics.simulationCount, drawcall 등 (커스텀)
Layer 6: 게임플레이 이벤트  → 몬스터 스폰, 플레이어 사망 등 (게임 로직)
```

> **핵심 인사이트**: Channel 자체는 파이프일 뿐이고, Unity 쪽 모니터 컴포넌트를 얼마나 풍부하게 만드느냐가 분석 범위를 결정한다. 로그에 없는 프레임 스파이크도 Unity에서 `Time.deltaTime`으로 감지해서 보내면 Claude가 분석 가능.

### 예시

```csharp
// 프레임 스파이크 감지 (로그 없어도 감지 가능)
void Update() {
    if (Time.deltaTime > 0.033f) { // 30fps 이하
        channel.Send($"[PERF] Frame spike: {Time.deltaTime*1000:F1}ms");
    }
}

// GC Alloc 모니터링
var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
if (gcRecorder.LastValue > threshold) {
    channel.Send($"[PERF] GC Alloc spike: {gcRecorder.LastValue} bytes");
}
```

---

## 다음 단계

1. `/sc:design` → 아키텍처 설계 (Channel 브릿지 프로토콜, Named Pipe 구현 등)
2. `/sc:workflow` → 구현 계획 수립 (P1 → P2 → P3 순서)
