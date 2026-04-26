# Changelog

## [0.5.2] - 2026-04-26

### Fixed
- **`[Service] + [API]` endpoint 라우팅 불일치** (Critical) — 클라이언트가 호출하는 URL과 서버 Controller의 Route가 일치하지 않아 모든 Service 호출이 404로 실패.
  - 클라 `ServiceGenerator` (SourceGen): `endpoint = $"{svc.Name}/{m.Name}"` → PascalCase 그대로 사용 (`api/ActiveRoomService/Create`)
  - 서버 `ServerCodeGenerator`: `[Route("api/{ToSnakeCase(name)}")]` → snake_case로 변환 (`api/active_room_service/Create`)
  - 두 Generator의 변환 규칙이 어긋나서 서버는 정상 동작 중인데 클라 호출은 미들웨어 파이프라인 끝까지 가서 404. `Cloud Run logs`에 `Request reached the end of the middleware pipeline without being handled by application code` 로그가 반복 출력됨.
  - 수정: `ServiceGenerator`에 `ToSnakeCase` 헬퍼 추가 + endpoint 빌드 시 `svc.Name`을 snake_case로 변환. 서버 Controller가 이미 snake_case로 매핑되어 있으므로 **서버 재배포 없이 클라만 수정**으로 해결.
  - 영향: SupaRun을 사용하는 모든 프로젝트에서 `[Service]` 호출이 단 한 번도 동작하지 않았음. 즉 모든 사용자가 영향 받음.

### Build
- **`SourceGen~/Tjdtjq5.SupaRun.SourceGen.csproj` 추가** — 모노레포 `.gitignore`의 `*.csproj` 패턴으로 누락되어 macOS/Linux 환경에서 SourceGen dll 재빌드가 불가능했음. 새 csproj 추가 (Roslyn `IIncrementalGenerator` 빌드용, netstandard2.0 + `Microsoft.CodeAnalysis.CSharp 4.3.0`).

## [0.5.1] - 2026-04-26

### Changed (Behavior)
- **설정 파일 분리** — `UserSettings/SupaRunSettings.json` 단일 파일에서 2개 파일로 분리
  - `ProjectSettings/SupaRunProjectSettings.json` — 공유 데이터(URL, AnonKey, DB Password, Access Token, GitHub Token, Cron Secret, GCP/Auth 정책 등 22개 필드). git 커밋 대상.
  - `UserSettings/SupaRunUserSettings.json` — 개인 환경(`serverLogToConsole`, `setupCompleted`). git 미커밋.
  - **⚠ 시크릿이 git에 평문 커밋되므로 private repo 전용 사용을 가정합니다.** 외부 공개 저장소에서는 사용 금지.
  - 자동 마이그레이션: 첫 실행 시 기존 `UserSettings/SupaRunSettings.json`을 2개로 분배 + 원본은 `.bak`으로 백업. 멱등 — 분리된 파일이 이미 있으면 스킵.
  - `SupaRunSettings.Instance.X` facade API는 100% 유지 — 호출자 코드 수정 불필요

### Changed
- `SupaRunRuntime.LoadOptionsFromSettings` (Editor 분기) — 새 경로(`ProjectSettings/SupaRunProjectSettings.json`) 우선, 마이그레이션 직전 상태에서는 레거시 `UserSettings/SupaRunSettings.json` fallback
- `SettingsView` — 시크릿 git 커밋 안내 경고 배너 (HelpBox/Warning) 상단에 자동 표시

## [0.4.3] - 2026-04-26

### Fixed
- **macOS .NET SDK 인식 실패** (Critical, macOS only)
  - 기존: `PrerequisiteChecker.Run()`이 모든 플랫폼에서 `cmd.exe /c`를 호출 → macOS에 `cmd.exe`가 없어 `dotnet --version` 검사가 항상 exit -1로 실패. SDK 설치 후에도 SetupWizard가 미설치로 표시되고 `RunDotnetBuild`가 빌드 불가.
  - 수정: `Run()`에 플랫폼 분기 추가 — Windows는 `cmd.exe /c`, macOS/Linux는 `/bin/sh -lc`로 셸 실행.
  - **`FindDotnet()` 신규** (`PrerequisiteChecker`) — `where`/`command -v`로 PATH 검색 후 알려진 macOS 설치 경로(`/usr/local/share/dotnet/dotnet`, `/usr/local/share/dotnet/x64/dotnet`, `/opt/homebrew/bin/dotnet`, `/usr/local/bin/dotnet`, `~/.dotnet/dotnet`)를 fallback으로 시도. Unity GUI 프로세스가 셸 PATH를 상속하지 못하는 macOS 환경에서도 안정 동작.
  - `FindGcloud`, `FindGh` 동일 패턴으로 cross-platform화 (Homebrew Apple Silicon/Intel 경로 추가).
  - `DeployManager.RunDotnetBuild` — `FileName = "dotnet"` 직접 호출을 `PrerequisiteChecker.FindDotnet()` 절대 경로로 교체.

## [0.4.2] - 2026-04-19

### Fixed
- **`DeployManager.ScanTypes` — asmdef 분리 환경 지원** (Critical)
  - 기존: `Assembly-CSharp` 이름 포함 어셈블리만 스캔 → 별도 asmdef로 분리된 user 코드 (예: `SurvivorsDuo.dll`)는 누락
  - 수정: `UnityEditor.TypeCache.GetTypesWithAttribute<T>()` 사용 → 모든 user 어셈블리 안전 스캔 + 미리 인덱싱 성능 이점
  - 영향: ECS/Burst/유닛 테스트 등으로 asmdef를 분리한 프로젝트의 [Table]/[Config]/[Service] 타입이 silent하게 누락되던 버그 해결

### Added
- **C# field initializer 기반 자동 SQL DEFAULT** (`ServerCodeGenerator.GetDefaultClause/GetSqlDefaultFromInitializer`)
  - 기존: `[Default(value)]` attribute로만 SQL DEFAULT 지정 가능
  - 신규: attribute 없이도 `public float lateral_extend = 1f;` 같은 C# field initializer 값을 reflection(`Activator.CreateInstance` + `GetValue`)으로 추출하여 `DEFAULT 1` 자동 적용
  - 우선순위: `[Default]` attribute → field initializer → 없으면 DEFAULT 절 생략
  - 지원 타입: `int/long/short/float/double/bool/enum`. `string`은 nullable이라 skip.
  - **NULL row 자동 정정**: `UPDATE {table} SET {col} = {default} WHERE {col} IS NULL` SQL 자동 생성 (멱등). 이전에 default 없이 추가된 컬럼이 NULL로 남은 row를 안전하게 채움.
  - 효과: 새 컬럼 추가 시 클라이언트의 `null → non-nullable type` deserialize 실패 차단

## [0.4.1] - 2026-04-19

### Added
- **`IAuthApi.GetAuthenticatedAsync`** — Bearer 토큰으로 Auth 엔드포인트에 GET 요청. 저장된 세션의 서버 측 유효성 검증용 (예: `/auth/v1/user`). 성공(2xx) 시 응답 텍스트, 실패(401/403/5xx) 시 null.
- **`SupabaseAuthApi.GetAuthenticatedAsync`** — 구현 추가
- **`MockAuthApi.GetAuthenticatedAsync`** — 테스트 mock 추가

### Changed
- **`SupaRunAuth`** — 세션 유효성 서버 검증 로직 확장, 관련 연쇄 갱신 (`SupaRunRuntime`, `CallbackAuthRefresher`, `SupabaseRestClient`)

### Removed
- `REFACTOR.md.meta` 잔재 정리 (REFACTOR.md는 v0.4.0에서 삭제됨)

## [0.4.0] - 2026-04-09

### Added
- **`IRealtimeClient`** — Realtime 추상화 인터페이스 (P3-3). Mock 주입으로 단위 테스트 가능.
- **`IAuthApi` + `SupabaseAuthApi`** — Auth HTTP 계층 분리 (P2-1d 완료). 패키지 전체 UnityWebRequest 직접 사용 0건.
- **`ApiKeyAuth`** — apikey 헤더 전용 IAuthStrategy (Supabase Auth 엔드포인트용)
- **EditMode 단위 테스트 67개** — Strategy, HttpExecutor, SessionStorage, RestClient, SupaRunClient, Auth, TokenPropagation (P3-2, P3-3)
- **`SupaRunRuntimeOptions.AuthApi` / `.Realtime`** — mock 주입 지원
- **`AssemblyInfo.cs`** — `[InternalsVisibleTo]` 테스트 어셈블리 접근

### Changed
- **`SupabaseRealtime`** — `IRealtimeClient` 구현 (인터페이스 추출)
- **`SupaRunAuth.Post()`** — inline UnityWebRequest → `IAuthApi` 위임
- **`SupaRunRuntime._realtime`** — 구체 타입 → `IRealtimeClient` 인터페이스
- **`SupaRun.Realtime`** — `SupabaseRealtime?` → `IRealtimeClient?`
- **`SupaRunRuntime.OnAuthSessionChanged`** — `void` → `internal void` (테스트 접근)
- nullable annotation 전체 적용 (`#nullable enable` 17개 파일) (P3-4)
- SourceGen BuildProcessor 통합 (BuildProxy 제거, ~130줄 삭제) (P3-1)

### Removed
- **`REFACTOR.md`** — P0~P3 전체 완료, 이력은 git log 참조

## [0.3.11] - 2026-04-09

### Major Refactor (P0+P1+P2 통합)

대규모 안정화/책임 분리/구조 리팩터. 자세한 내역은 `REFACTOR.md` 참조.
**호환성 유지** — 외부 API (`SupaRun.Login()`, `SupaRun.GetAll<T>()` 등) 모두 그대로.

### Added
- **`SupaRunRuntime`** — 인스턴스 클래스 (P2-3). 모든 자원 보유, 단위 테스트 + DI 가능.
- **`SupaRunRuntimeOptions`** — 옵션 객체 (의존성 주입용)
- **`SupaRun.Login()` / `IsLoggedIn`** — 명시적 로그인 진입점 (P0-1)
- **`SupaRun.Verbose` / `LogVerbose`** — 디버그 로그 게이트 (P1-4)
- **`IServerClient`** — 서버 클라이언트 추상화 (P1-3)
- **`IHttpTransport` + `UnityHttpTransport`** — 저수준 HTTP 추상화 (P2-1)
- **`HttpExecutor`** — Strategy 패턴 송신 오케스트레이터 (P2-1)
- **Strategy 7개**: `BearerTokenAuth`, `BearerJwtOrAnonAuth`, `ApiKeyOnlyAuth`, `NoAuth`, `NoRetry`, `ExponentialBackoffRetry`, `CallbackAuthRefresher`
- **`ISessionStorage` + `SecureSessionStorage` + `MemorySessionStorage`** — 세션 저장소 추상화 (P2-2)
- **MPPM Virtual Player 자동 분리** — 인스턴스마다 별도 게스트 계정 (P2-2)
- **`ServerResponse.isAuthenticated` / `hint`** — 진단 메타데이터 (P2-4)
- 도메인 리로드 cleanup 자동 등록 (P2-3e)
- 단위 테스트용 `SupaRun.SetInstance(...)` internal API (P2-3e)

### Changed
- **`SupaRun` 정적 클래스** — `SupaRunRuntime`의 lazy singleton facade로 재작성 (342→175줄, -49%) (P2-3c)
- **`SupabaseAuth` → `SupaRunAuth`** 리네이밍 (P1-2)
- **`SupaRunClient`** — Strategy 패턴 마이그레이션. UnityWebRequest 직접 사용 0 (P2-1f)
- **`SupabaseRestClient`** — Strategy 패턴 마이그레이션. JWT or anon Bearer 자동 (P2-1e)
- **`SupabaseRestClient`** — JWT 사용 (이전엔 항상 anon key) → RLS authenticated 정책 통과
- **`SecureStorage` (정적) → `SecureSessionStorage` (인스턴스)** + key prefix 옵션
- **`SupaRunSettings.json` 로드 경로** — MPPM Virtual Player 가상 루트 자동 감지 (P0-5)
- **`SupaRunAuth`** — 정적 `SupaRun.Client` 의존 끊고 `IServerClient` 생성자 주입 (P1-3)

### Fixed
- **silent failure 차단** — `SupaRun.GetAll<T>()` 가 anonymous 호출 시 콘솔 경고 + ServerResponse.hint (P0-4, P2-4)
- **LocalGameDB fallback 진단** — `_client` null 시 1회 명확한 경고 (P0-6)
- **MPPM Virtual Player에서 PlayerStatConfig 로드 실패** — settings 절대 경로 + VP 가상 루트 감지로 해결 (P0-5)
- **`Login()` → `SignOut()` → `Login()` 재로그인 깨짐** — `_loginTask` stale 캐시 제거 (P0-1)
- **JSON 파싱 실패 분류** — 이전 NetworkError → BadRequest로 정확화 (P2-1e)

### Removed
- **`SupaRun.cs`의 `_client/_restClient/_localDB/_auth/_realtime/_initialized` 정적 필드** — `SupaRunRuntime`로 이동
- **`SupaRun.AutoInitialize()` 메서드 80줄** — `SupaRunRuntime` 생성자로 이동
- **데드 코드 4개 클래스** — `Runtime/Supabase/SupabaseClient.cs`, `SupabaseAuth.cs(stub)`, `SupabaseStorage.cs(stub)`, `SecureStorage.cs(static)` (P1-1, P2-2)
- **`SupabaseRealtime(SupabaseClient)` 사용 안 되는 생성자** (P1-1)

### Deprecated
- **`SupaRun.Initialize(ServerConfig)`** — `[Obsolete]` 표시 + noop. `SupaRun.Login()` 사용 권장.

## [0.3.10] - 2026-04-04

### Fixed
- 어드민 페이지: Bootstrap JS 이중 로딩 제거 (Tabler JS 번들이 Bootstrap 포함 → 충돌로 dropdown 열자마자 닫힘). Tabler JS 제거, Bootstrap JS만 유지
- 어드민 페이지: 그룹 드롭다운을 Tabler dropdown 패턴으로 복원 + 동적 생성 후 bootstrap.Dropdown 초기화

## [0.3.9] - 2026-04-04

### Fixed
- 어드민 페이지: 그룹 펼치기를 Bootstrap collapse 패턴으로 변경 (dropdown → collapse). Tabler 수직 사이드바에서 정상 동작

## [0.3.8] - 2026-04-04

### Fixed
- 어드민 페이지: 그룹 드롭다운 펼치기 안 되는 버그 수정 (동적 생성 요소에 Bootstrap Dropdown 초기화 추가)

## [0.3.7] - 2026-04-04

### Changed
- FeatureInstaller: Service.cs에 `#if UNITY_EDITOR` 자동 래핑 제거. BuildProcessor가 앱 빌드 시 자동 처리하므로 불필요

## [0.3.6] - 2026-04-04

### Fixed
- StripForServer: `#if UNITY_EDITOR` 블록 내용 보존 (래핑만 제거). Service 코드가 서버 빌드에서 사라지는 문제 수정
- SupaRunBuildProcessor: `GenerateBuildProxy()` 호출 추가 — 앱 빌드 시 Service HTTP 프록시 자동 생성

## [0.3.5] - 2026-04-04

### Fixed
- ServerCodeGenerator: Config/Table/Json Attribute stub에 `(string group)`, `(Type targetType)` 생성자 추가 — `[Config("InGame")]` 서버 빌드 실패 수정

## [0.3.4] - 2026-04-04

### Fixed
- DeployManager: 서버 빌드 시 Unity 전처리기 블록(`#if UNITY`) 제거 로직 추가
- DeployManager: using 필터링을 화이트리스트 방식으로 개선 (System, Tjdtjq5, Newtonsoft, Microsoft만 허용)
- DeployManager: `[Json(typeof(...))]` → `[Json]` 서버 빌드 시 자동 변환
- DeployManager: `[EnumType(typeof(...))]` 서버 빌드 시 자동 제거

### Added
- `[EnumType]` Attribute: enum 타입 힌트 (Source Generator용)
- `[Config("group")]` 그룹 파라미터 지원 (ConfigAttribute 생성자 이미 존재, 배포 반영)

## [0.3.3] - 2026-03-30

### Fixed
- AutoInitialize: SO 검색 → UserSettings/SupaRunSettings.json 직접 읽기로 변경 (에디터 Play 모드에서 _restClient null 이슈)
- 어드민 페이지: bootstrap.Modal 미정의 에러 수정 (Bootstrap 5 JS 별도 로드)
- 어드민 페이지: 필터링 시 삭제/복사 인덱스 불일치 수정 (row.id 기반으로 전환)
- 어드민 페이지: confirmDelete async 미대기 수정
- 어드민 페이지: 빈 ID 행 삭제 시 405 에러 방어
- 어드민 페이지: addRow 취소 시 빈 ID 행 생성 방지
- 서버: PUT으로 ID 변경 시 기존 행 삭제 + 새 행 생성 (rename 지원)
- 서버: [Json] attribute stub 누락으로 빌드 실패 수정

### Added
- [Json] Attribute: string 필드가 JSON 데이터임을 표시, 어드민 페이지 JSON 에디터 자동 연동
- 어드민 페이지: 범용 JSON 배열 에디터 (테이블 레이아웃, 타입별 컬럼 폭, 가로 스크롤)
- CDN 버전 고정 (Tabler 1.2.0, Icons 3.30.0, Bootstrap 5.3.3)
- FK 소스 병렬 로드 (Promise.all)
- Feature.md 11개 + FEATURE_INDEX.md (피처 구조 도입)

## [0.3.2] - 2026-03-30

### Added
- PostgresConnectionTester: DB 비밀번호 검증 (Management API + SCRAM-SHA-256 해시 비교)
- Setup ⑤ 연결 테스트에 DB Password 검증 추가 (REST API + DB 2단계)
- Settings 연결 테스트에 DB Password 검증 추가

### Changed
- SupabaseSetup: 연결 테스트를 async 방식으로 전환, 2단계 검증
- SettingsView: RunConnectionTest에 DB 비밀번호 Phase 2 추가

## [0.3.1] - 2026-03-29

### Changed
- SupaRunSettings 대규모 리팩토링 (+420줄)
- GcpSetupUI / GitHubSetupUI 개선
- SetupWizard / SupabaseSetup 업데이트
- DeployManager / GitHubPusher / ServerCacheHealthChecker 안정화
- PrerequisiteChecker 강화
- AuthUrlSyncManager 업데이트
- GameServer → SupaRun 네이밍 정리 (레거시 .meta 삭제)
- Editor Utils 유틸리티 추가

### Removed
- GameServerBuildProcessor.cs.meta (레거시)
- GameServerDashboard.cs.meta (레거시)
- GameServerSettings.cs.meta (레거시)
- Tjdtjq5.GameServer.Editor.asmdef.meta (레거시)
- Runtime 레거시 .meta 파일 4개 삭제

## [0.3.0] - 2026-03-29

### Changed
- EditorPrefs 키를 프로젝트별 고유 접두사로 변경 (Application.dataPath 해시 기반)
- 여러 프로젝트에서 SupaRun 사용 시 설정 충돌 방지
- 레거시 접두사(`GameServer_`, `SupaRun_`) → 프로젝트별 접두사 자동 마이그레이션
- Runtime에서 EditorPrefs 직접 접근 대신 리플렉션으로 SupaRunSettings.SupabaseAnonKey 사용

## [0.2.1] - 2026-03-25

### Fixed
- 초기 배포 안정화

## [0.2.0] - 2026-03-24

### Added
- 초기 릴리스: ASP.NET + Supabase + Cloud Run 자동 배포
