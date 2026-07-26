# Dashboard

- **상태**: stable
- **용도**: SupaRun 통합 관리 EditorWindow — 초기 설정 마법사, 서비스 상태 모니터링, 배포 실행, 서버 로그 조회, 설정 편집을 하나의 윈도우에서 제공

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| SupaRunSettings | `../Settings/SupaRunSettings.cs` | 모든 설정값 읽기/쓰기 |
| Deploy | `../Deploy/` | DeployManager, ActionsTracker, ServerCacheHealthChecker 호출 |
| SupaRunUI | `UI/SupaRunUI.cs` | 공용 IMGUI 헬퍼 (알림 바, NotificationType, 정보 박스) — 바닐라 IMGUI |
| PrerequisiteChecker | `../PrerequisiteChecker.cs` | dotnet/gh/gcloud CLI 상태 확인 |
| SupabaseManagementApi | `../SupabaseManagementApi.cs` | 프로젝트 목록, Anon Key, Auth 설정, DB 쿼리 |
| AuthUrlSyncManager | `../AuthUrlSyncManager.cs` | Auth URL 변경 감지 + 자동 동기화 |
| PostgresConnectionTester | `../PostgresConnectionTester.cs` | DB 연결 테스트 |
| DeployRegistry | `../DeployRegistry.cs` | 엔드포인트 배포 상태 조회 |

## 구조

```
Dashboard/
├── SupaRunDashboard.cs      # 메인 EditorWindow (Setup/Dashboard/Settings 모드 전환)
├── CostMenu.cs              # 메뉴: Tjdtjq/SupaRun/Cost/{Supabase,Google Cloud,GitHub Actions}
├── Setup/
│   ├── SetupWizard.cs       # 5단계 초기 설정 (.NET → Supabase → gh → gcloud → Deploy)
│   ├── SupabaseSetup.cs     # Supabase 연결 설정 (토큰, 프로젝트 선택, Anon Key, DB 비밀번호, 연결 테스트)
│   └── DeploySetup.cs       # 배포 설정 (GitHub + GCP) — Setup 마지막 단계
├── Tabs/
│   ├── StatusTab.cs          # 서버 온라인/응답시간, DB 커넥션 풀, Supabase 프로젝트 정보, 요금 링크
│   ├── DeployTab.cs          # 배포 실행 UI (캐시 관리, 빌드 검증, push, Actions 추적, 결과 표시)
│   │                         #  + 스키마 동기화 / Id 상수 생성
│   ├── MonitorTab.cs         # 서버 로그(server_log) 조회 — 레벨 필터, 페이징, 상세보기
│   ├── ServicesTab.cs        # [Service] 클래스 자동 스캔 + 배포 상태 표시
│   └── SettingsView.cs       # 설정 편집 (환경/프로젝트/Supabase/GitHub/GCP/Auth/Tools/로그)
├── SharedUI/
│   ├── GcpSetupUI.cs         # GCP 설정 공용 UI (CLI → 로그인 → 프로젝트 → API 활성화)
│   ├── GitHubSetupUI.cs      # GitHub 설정 공용 UI (CLI → 로그인 → 레포 생성/선택)
│   ├── ProjectManagerUI.cs   # Supabase 프로젝트 목록·생성·삭제·복구 + 환경 자동 연결
│   └── EditorInputDialog.cs  # 한 줄 입력 모달 (되돌릴 수 없는 동작의 이름 타이핑 확인)
└── UI/
    └── SupaRunUI.cs          # 공용 IMGUI 헬퍼 (NotificationType, 알림 바, 정보 박스)
```

## 메인 툴바 드롭다운 (`SupaRunToolbar.cs`)

Unity 6.3 `MainToolbarElement` 로 툴바에 `SupaRun: <환경> ▾` 을 얹는다.
같은 프로젝트의 Photon Quantum 이 씬 선택 드롭다운에 같은 API 를 쓰고 있어 검증된 경로다.

목적이 둘이고, **첫 번째가 더 중요하다**:

1. **현재 편집 환경을 항상 보이게 한다.** 라벨이 곧 표시다 — "dev 인 줄 알고 prod 를 건드리는"
   사고는 지금 어디인지 모르는 상태에서 나온다. 대시보드를 열어야만 알 수 있으면 늦다
2. 자주 쓰는 동작을 대시보드 없이 실행한다

| 메뉴 | 동작 |
|---|---|
| 환경/`<이름>` | 편집 환경 전환. **prod·live·release 로 바꿀 때만** 확인 모달 |
| 어드민 열기 / 대시보드 열기 | 기존 진입점 |
| 스키마 반영 (`<환경>`) | `SchemaAutoSync.SyncNow()` |
| Id 상수 생성 | `IdConstantGenerator.Generate()` — 결과를 콘솔에 남긴다 |
| 서버 배포… | 확인 후 **Deploy 탭으로 이동**. 툴바에서 곧장 쏘지 않는다 |

- 전환 확인을 **매번** 묻지 않는 이유: 확인창이 흔해지면 정작 위험한 전환에서도 습관적으로 넘긴다
- 배포를 툴바에서 실행하지 않는 이유: 몇 분이 걸리고 진행 로그를 봐야 하는데 툴바에는 그 자리가 없다.
  여기서 할 일은 **버튼까지 데려다주는 것**이다
- 아이콘은 `EditorGUIUtility.FindTexture` 로 찾는다. `IconContent` 는 없는 이름에 경고를 뿌리고,
  내장 아이콘 이름은 Unity 버전마다 사라지기도 한다

> ⚠ **툴바 요소는 등록만으로는 보이지 않는다.** 표시 여부가 사용자 설정에 저장되고
> `MainToolbarElementAttribute` 에는 그것을 제어하는 항목이 없다(`path`/`defaultDockPosition`/
> `menuPriority`/`displayName`/`ussName` 뿐). 같은 프로젝트의 Quantum 도 기본 숨김이다.
> 사용자는 **툴바 우클릭 → Tools > SupaRun > Environment Bar** 로 켠다.
>
> 그래서 `ShowOnceOnFirstLoad()` 가 프로젝트마다 **한 번만** 자동으로 켠다.
> 한 번뿐인 이유는 나중에 사용자가 끄면 그 선택을 존중해야 하기 때문이다 — 매번 켜면 훼방이 된다.
> 쓰는 `MainToolbar.ShowAll` 은 **Unity 내부 API(non-public)** 라 리플렉션으로 부르고 실패는 삼킨다.
> 툴바가 안 보이는 것은 불편이지 고장이 아니고, 우클릭 메뉴라는 길이 남아 있다.

## 프로젝트 관리 (Settings > Supabase 프로젝트 관리)

환경 카드 바로 아래에 둔다 — "어느 프로젝트를 쓰는가"(환경)와 "그 프로젝트가 실제로 있는가"(여기)는
같이 봐야 판단이 된다. 목록에는 **그 프로젝트를 쓰는 환경 이름**이 함께 뜬다.

| 동작 | 설명 |
|---|---|
| 목록 | 상태 아이콘(● 정상 / ◐ 준비중 / ○ 정지) · 리전 · 상태 · 연결된 환경 |
| 생성 | 이름 + **리전 드롭다운** + 플랜(free/pro) → 준비 대기 → anon key → 환경 자동 등록 |
| 복구 | `INACTIVE` 인 프로젝트를 되살린다 (무료 플랜은 안 쓰면 자동 정지된다) |
| 환경으로 등록 | 이미 있는 프로젝트를 환경에 연결. 생성 중 취소했을 때 이어받는 통로 |
| 삭제 | 확인 모달 → **프로젝트 이름 타이핑** 요구 |

- **리전은 생성할 때만** 정할 수 있다. `PATCH /v1/projects/{ref}` 가 바꾸는 것은 이름뿐이라
  기존 프로젝트는 읽기 전용으로 표시하고, 옮기려면 새로 만들어 승격하도록 안내한다
- **DB 비밀번호는 자동 생성**해 환경에 저장한다. 사람이 외울 값이 아니다
- 생성 흐름은 취소할 수 있고, **취소해도 프로젝트는 남는다** — 그래서 목록의
  '환경으로 등록' 이 이어받는 길이 된다. 2분짜리 대기가 유일한 연결 통로면 놓쳤을 때 복구가 안 된다
- ⚠ 폴링은 `EditorApplication.update` 로 돈다. `UniTask.Delay` 는 PlayerLoop 에 매여 있어
  **비플레이 모드에서 돌지 않을 수 있다**(ActionsTracker 도 같은 이유로 같은 방식이다)

## API

### SupaRunDashboard (EditorWindow)

| 메서드 | 설명 |
|--------|------|
| `Open()` | Dashboard 열기 (메뉴: `Tjdtjq/SupaRun/Dashboard`, 단축키 `Ctrl+Shift+Q`) |
| `OpenAdmin()` | Admin 웹 페이지 열기 (메뉴: `Tjdtjq/SupaRun/Admin`, 단축키 `Ctrl+Shift+D`) |
| `ShowNotification(message, type)` | 상단 알림 바에 메시지 표시 |
| `OnSetupCompleted()` | Setup 완료 처리 → Dashboard 모드 전환 |
| `OpenSettings()` | Settings 모드로 전환 |
| `BackToDashboard()` | Dashboard 모드로 복귀 |
| `OpenSetup()` | Setup 마법사 다시 시작 |

### CostMenu

| 메뉴 항목 | 설명 |
|-----------|------|
| `Tjdtjq/SupaRun/Cost/Supabase` | Supabase 요금 페이지 열기 |
| `Tjdtjq/SupaRun/Cost/Google Cloud` | GCP Billing 페이지 열기 |
| `Tjdtjq/SupaRun/Cost/GitHub Actions` | GitHub Actions Billing 페이지 열기 |

### SetupWizard

5단계 초기 설정 마법사. 각 단계별 건너뛰기/완료 지원.

| 단계 | 내용 | 필수 여부 |
|------|------|----------|
| 1. .NET SDK | dotnet CLI 설치 확인 | 선택 |
| 2. Supabase | 프로젝트 연결 + Anon Key + 연결 테스트 | 필수 |
| 3. gh CLI | GitHub CLI 설치 + 로그인 | 선택 |
| 4. gcloud CLI | Google Cloud CLI 설치 + 로그인 | 선택 |
| 5. Deploy | GitHub 레포 + GCP 설정 | 선택 |

### SupabaseSetup

| 주요 기능 | 설명 |
|-----------|------|
| Access Token 입력 | 토큰 입력 → 프로젝트 목록 자동 조회 |
| 프로젝트 선택 | 드롭다운에서 선택 → URL 자동 설정 + Anon Key 자동 조회 |
| Auth 자동 설정 | 익명 로그인 활성화 + Auth URL 자동 구성 |
| 연결 테스트 | 2단계 — REST API 확인 → DB 비밀번호 검증 |

### GcpSetupUI / GitHubSetupUI (static class)

Setup과 Settings에서 공용으로 사용하는 UI 컴포넌트.

| 클래스 | 메서드 | 설명 |
|--------|--------|------|
| `GcpSetupUI` | `Draw(dashboard, settings)` | GCP 설정 단계별 UI (CLI → 로그인 → 프로젝트 → API → SA) |
| `GitHubSetupUI` | `Draw(dashboard, settings)` | GitHub 설정 단계별 UI (CLI → 로그인 → 레포 생성) |
| `GitHubSetupUI` | `IsRepoReady` | 레포 생성 완료 여부 (GcpSetupUI에서 참조) |

## 주의사항

- 설정 파일 분리 (v0.4.0~) — 공유 데이터는 `ProjectSettings/SupaRunProjectSettings.json` (git 커밋), 개인 환경은 `UserSettings/SupaRunUserSettings.json` (git 미커밋). 레거시 `UserSettings/SupaRunSettings.json`은 첫 실행 시 자동 마이그레이션 + `.bak` 백업. **시크릿(DB Password / Access Token / GitHub Token / Cron Secret)은 ProjectSettings/에 평문 저장되어 git 커밋되므로 private repo 전용 사용을 가정**한다. 외부 공개 저장소에서는 사용 금지. SettingsView 상단에 경고 배너 자동 표시.
- Dashboard는 3개 모드(Setup / Dashboard / Settings)를 하나의 EditorWindow에서 전환. `setupCompleted` 플래그로 최초 진입 시 Setup 모드 자동 표시
- `SettingsView.cs`는 약 1100줄 — Supabase/GitHub/GCP/Auth OAuth 프로바이더 설정을 모두 포함하는 대형 뷰
- `MonitorTab`은 Supabase REST API로 `server_log` 테이블을 직접 조회. Newtonsoft 의존 없이 간이 JSON 파서 사용
- `StatusTab`의 DB 연결 섹션은 `max_connections` 조회 후 `poolSize * maxInstances <= safeMax(80%)` 자동 계산 + 설정 저장
- `ServicesTab`은 10초마다 Assembly-CSharp의 [Service] 클래스를 리플렉션 스캔
- `PrerequisiteChecker.WarmCacheAsync()`를 OnEnable에서 호출하여 CLI 상태를 백그라운드 캐싱
- Access Token 만료 시 상단에 빨간 경고 바 표시
