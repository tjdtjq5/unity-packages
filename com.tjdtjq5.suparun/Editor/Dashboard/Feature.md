# Dashboard

- **상태**: stable
- **용도**: SupaRun 통합 관리 EditorWindow — 초기 설정 마법사, 서비스 상태 모니터링, 배포 실행, 서버 로그 조회, 설정 편집을 하나의 윈도우에서 제공

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| SupaRunSettings | `../Settings/SupaRunSettings.cs` | 모든 설정값 읽기/쓰기 |
| Deploy | `../Deploy/` | DeployManager, ActionsTracker, ServerCacheHealthChecker 호출 |
| EditorToolkit | 패키지 `com.tjdtjq5.editor-toolkit` | EditorUI 드로잉 유틸 (EditorTabBase, 색상 상수, 컴포넌트) |
| PrerequisiteChecker | `../PrerequisiteChecker.cs` | dotnet/gh/gcloud CLI 상태 확인 |
| SupabaseManagementApi | `../SupabaseManagementApi.cs` | 프로젝트 목록, Anon Key, Auth 설정, DB 쿼리 |
| AuthUrlSyncManager | `../AuthUrlSyncManager.cs` | Auth URL 변경 감지 + 자동 동기화 |
| PostgresConnectionTester | `../PostgresConnectionTester.cs` | DB 연결 테스트 |
| DeployRegistry | `../DeployRegistry.cs` | 엔드포인트 배포 상태 조회 |

## 구조

```
Dashboard/
├── SupaRunDashboard.cs      # 메인 EditorWindow (Setup/Dashboard/Settings 모드 전환)
├── CostMenu.cs              # 메뉴: Tools/SupaRun/Cost/{Supabase,Google Cloud,GitHub Actions}
├── Setup/
│   ├── SetupWizard.cs       # 5단계 초기 설정 (.NET → Supabase → gh → gcloud → Deploy)
│   ├── SupabaseSetup.cs     # Supabase 연결 설정 (토큰, 프로젝트 선택, Anon Key, DB 비밀번호, 연결 테스트)
│   └── DeploySetup.cs       # 배포 설정 (GitHub + GCP) — Setup 마지막 단계
├── Tabs/
│   ├── StatusTab.cs          # 서버 온라인/응답시간, DB 커넥션 풀, Supabase 프로젝트 정보, 요금 링크
│   ├── DeployTab.cs          # 배포 실행 UI (캐시 관리, 빌드 검증, push, Actions 추적, 결과 표시)
│   ├── MonitorTab.cs         # 서버 로그(server_log) 조회 — 레벨 필터, 페이징, 상세보기
│   ├── ServicesTab.cs        # [Service] 클래스 자동 스캔 + 배포 상태 표시
│   └── SettingsView.cs       # 설정 편집 (Supabase/GitHub/GCP/Auth/Tools/로그)
├── SharedUI/
│   ├── GcpSetupUI.cs         # GCP 설정 공용 UI (CLI → 로그인 → 프로젝트 → API 활성화)
│   └── GitHubSetupUI.cs      # GitHub 설정 공용 UI (CLI → 로그인 → 레포 생성/선택)
└── UI/                       # (빈 폴더 — 예약)
```

## API

### SupaRunDashboard (EditorWindow)

| 메서드 | 설명 |
|--------|------|
| `Open()` | Dashboard 열기 (메뉴: `Tools/SupaRun/Dashboard`, 단축키 `Ctrl+Shift+Q`) |
| `OpenAdmin()` | Admin 웹 페이지 열기 (메뉴: `Tools/SupaRun/Admin`, 단축키 `Ctrl+Shift+D`) |
| `ShowNotification(message, type)` | 상단 알림 바에 메시지 표시 |
| `OnSetupCompleted()` | Setup 완료 처리 → Dashboard 모드 전환 |
| `OpenSettings()` | Settings 모드로 전환 |
| `BackToDashboard()` | Dashboard 모드로 복귀 |
| `OpenSetup()` | Setup 마법사 다시 시작 |

### CostMenu

| 메뉴 항목 | 설명 |
|-----------|------|
| `Tools/SupaRun/Cost/Supabase` | Supabase 요금 페이지 열기 |
| `Tools/SupaRun/Cost/Google Cloud` | GCP Billing 페이지 열기 |
| `Tools/SupaRun/Cost/GitHub Actions` | GitHub Actions Billing 페이지 열기 |

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
