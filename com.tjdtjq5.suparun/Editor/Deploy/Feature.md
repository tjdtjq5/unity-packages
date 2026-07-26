# Deploy

- **상태**: stable
- **용도**: Unity에서 [UserData]/[SpecData]/[Service] 클래스를 스캔하여 ASP.NET 서버 코드를 자동 생성하고, GitHub에 push 후 Cloud Run에 배포하는 파이프라인

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| SupaRunSettings | `../Settings/SupaRunSettings.cs` | Supabase/GCP/GitHub 설정값 |
| PrerequisiteChecker | `../PrerequisiteChecker.cs` | dotnet/gh/gcloud CLI 상태 확인 |
| SupabaseManagementApi | `../SupabaseManagementApi.cs` | pg_cron SQL 실행, Auth 설정 |
| DeployRegistry | `../DeployRegistry.cs` | 배포된 엔드포인트 기록 |

## 구조

| 파일 | 타입 | 설명 |
|------|------|------|
| `DeployManager.cs` | `static class` | 배포 오케스트레이터 — 코드 생성, 빌드 테스트, 배포, pg_cron 잡 등록 |
| `ServerCodeGenerator.cs` | `static class` | [UserData]/[SpecData]/[Service] 리플렉션 스캔 → ASP.NET Controller/Migration/DTO/Admin 코드 생성 |
| `GitHubPusher.cs` | `static class` | gh CLI로 레포 clone → 파일 쓰기 → commit/push + GitHub Secrets 설정 |
| `ActionsTracker.cs` | `static class` | GitHub Actions 워크플로우 상태 폴링 (5초 간격, head_sha 필터링) + 성공/실패 결과 수집 |
| `ServerCacheHealthChecker.cs` | `static class` | 배포 스냅샷 저장, 코드 변경 감지(SHA256), .NET 버전 변경/캐시 만료 경고 |
| `ServerCacheTypes.cs` | `static class` | 서버 캐시 타입 상수 정의 (NuGet, Docker, Skip) |
| `EnvironmentPromoter.cs` | `static class` | 환경 간 [SpecData] 승격 — 대상 스냅샷 → 원본 추출 → 대상 주입 |

## 환경 (dev / prod …)

설정은 `SupaRunSettings.EnvironmentData` 목록으로 들고, **편집 환경**과 **빌드 환경**을 따로 가리킨다.

| 축 | 무엇이 따라가는가 |
|---|---|
| `editorEnvironment` | 컴파일 시 스키마 자동 반영 · 어드민 · 대시보드 · 에디터 플레이 |
| `buildEnvironment` | 빌드 산출물의 `Resources/SupaRunConfig.json` |

둘을 나눈 이유는 **dev 를 보면서 prod 빌드를 뽑는 것이 정상 상태**이기 때문이다. 하나로 묶으면
빌드마다 편집 환경을 바꿔야 하고, 되돌리기를 잊으면 그 다음 컴파일이 라이브 스키마를 건드린다.

- `settings.supabaseUrl` 등 기존 프로퍼티는 **현재 편집 환경의 값**을 돌려준다.
  덕분에 `SupaRunSettings` 를 참조하는 20여 곳이 수정 없이 환경을 따라간다
- 환경별인 것: Supabase URL/키/DB비번/PAT, Cloud Run 서비스명·URL, cronSecret
  공통인 것: GCP 프로젝트·리전·서비스계정, GitHub 레포·토큰, 스케일링, 캐시, authProviders
- ⚠ **반영 기록(해시)은 환경마다 별도 파일**이다 (`ProjectSettings/SupaRunSchemaHash.<env>.txt`).
  하나로 두면 dev 에 반영한 해시 때문에 prod 반영이 "변경 없음" 으로 **조용히 스킵된다**

### 승격 (dev → prod)

**스키마는 옮기지 않는다.** 마이그레이션이 코드 생성 + 멱등이라 대상에서 실행하면 같은 구조가 나온다.
그래서 순서가 ① 스키마 반영 ② 데이터 승격이다.

- 데이터는 **전체 통째**. 부분 승격을 두지 않는 이유는 두 환경이 서서히 달라지는 것을 막기 위해서다
- 적용 직전 **대상 환경 스냅샷이 자동 저장**되므로 되돌릴 수 있다
- `jsonb_populate_recordset` 으로 넣는다 — 대상 테이블 정의 기준이라 **원본에만 있는 컬럼은 무시되고
  대상에만 있는 컬럼은 기본값으로 남는다**. 컬럼이 어긋나도 승격이 죽지 않는다
- 페이로드는 세션 변수(`suparun.promote_payload`)에 올린다. TEMP TABLE 은 트랜잭션 경계에 따라
  사라질 수 있고, 인라인 반복은 테이블 수만큼 SQL 을 복제한다
- 실행 위치가 에디터인 이유: **두 환경의 PAT 를 동시에 쥔 곳이 여기뿐**이다.
  어드민은 환경 하나만 보므로 이 일을 할 수 없다
- 대상 스냅샷은 어드민 RPC 를 그대로 쓰되, Management API 에는 로그인 사용자가 없으므로
  `set_config` 로 **관리자 신원을 트랜잭션 동안만 빌린다**. 대상에 관리자가 없으면 여기서 막히는데,
  그건 실제로 승격하면 안 되는 상태다

## API

### DeployManager

| 메서드 | 설명 |
|--------|------|
| `GenerateFiles(settings, onProgress?)` | [UserData]/[SpecData]/[Service] 스캔 → 서버 코드 + 템플릿 + 공유 파일 생성. `(List<GeneratedFile>, Type[], error)` 반환 |
| `Deploy(settings, onProgress, onSuccess, onFailed, onSkipped?)` | 전체 배포 파이프라인 실행 (코드 생성 → 변경 감지 → GitHub push → 배포 기록) |
| `IsDotnetAvailable()` | dotnet CLI 설치 여부 |
| `PrepareBuildTest(settings)` | 메인 스레드에서 코드 생성 + temp 폴더에 쓰기. `(tempDir, error)` 반환 |
| `RunDotnetBuild(tempDir)` | 백그라운드에서 dotnet build 실행 + temp 폴더 자동 삭제. `(success, output)` 반환 |
| `RegisterCronJobs()` | [Cron] 어트리뷰트가 있는 메서드를 pg_cron 잡으로 등록 (Supabase Management API 경유) |

### ServerCodeGenerator

| 메서드 | 설명 |
|--------|------|
| `Generate(tableTypes, specTypes, logicTypes, settings)` | 리플렉션 기반 ASP.NET 코드 일괄 생성 (Controller, Migration, DTO, Admin, IGameDB, DapperGameDB 등) |
| `GenerateCronExtensionsSql_PgCron()` | pg_cron 확장 활성화 SQL |
| `GenerateCronExtensionsSql_PgNet()` | pg_net 확장 활성화 SQL |
| `GenerateCronCleanupSql()` | 기존 gs_ 접두사 cron 잡 삭제 SQL |
| `GenerateCronScheduleSqls(logicTypes, cloudRunUrl, cronSecret)` | [Cron] 메서드 → pg_cron 스케줄 등록 SQL 목록 |

### GitHubPusher

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `Push(settings, files, onSuccess, onFailed)` | gh CLI로 레포 clone → 파일 교체 → commit/push + GitHub Secrets 자동 설정 |
| `LastPushedSha` | 마지막 push에 사용된 commit SHA (40자 hex). `ActionsTracker.StartTracking`의 head_sha 필터링에 사용 |

### ActionsTracker

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `StartTracking(repo)` | (구버전 호환) head_sha 필터 없이 latest run을 폴링. `StartTracking(repo, null)`과 동일 |
| `StartTracking(repo, headSha)` | GitHub Actions 폴링 시작 (5초 간격, 10분 타임아웃). `headSha`가 있으면 해당 commit의 run만 추적 — push 직후 이전 commit의 success run을 잘못 잡는 버그 방지. 새 run이 60초간 안 잡히면 fallback Success |
| `Stop()` | 폴링 중단 |
| `CurrentStatus` | `Status` enum — Idle, Polling, Success, Failed, Timeout |
| `FailedLog` | 실패 시 마지막 50줄 로그 |
| `CloudRunUrl` | 배포 성공 시 gcloud에서 조회한 서비스 URL |
| `ElapsedSeconds` | 폴링 시작 후 경과 시간 |
| `GetActionsUrl(repo)` | GitHub Actions 페이지 URL |

### ServerCacheHealthChecker

| 메서드 | 설명 |
|--------|------|
| `GetAlerts()` | 캐시 상태 경고 목록 (첫 배포, 이전 실패, .NET 버전 변경, 캐시 만료) |
| `Invalidate()` | 캐시된 경고 무효화 |
| `SaveDeploySnapshot(files)` | 배포 성공 시 코드 해시 + .NET 버전 + 날짜 저장 |
| `MarkDeployFailed()` | 배포 실패 기록 |
| `IsCodeChanged(files)` | 현재 코드 해시와 마지막 배포 해시 비교 |
| `LastDeployDate` | 마지막 배포 시각 (nullable) |

### GeneratedFile

| 필드 | 설명 |
|------|------|
| `Path` | 출력 상대 경로 (예: `Generated/Controllers/PlayerController.cs`) |
| `Content` | 생성된 코드 문자열 |

## 주의사항

- `ServerCodeGenerator.cs`는 약 1900줄의 대규모 코드 생성기. ASP.NET Controller, DapperGameDB, Migration SQL, Admin API, Cron Controller 등 전체 서버 스택을 생성
- `GitHubPusher`는 temp 폴더에 clone → 파일 교체 → push 방식. 기존 `Generated/`, `Shared/`, `admin/` 폴더를 삭제 후 새로 씀
- GitHub Secrets로 `SUPABASE_CONNECTION_STRING`, `SUPABASE_AUTH_URL`, `CLOUD_RUN_*`, `CRON_SECRET`을 자동 설정
- `ActionsTracker`는 `EditorApplication.update`에 폴링을 등록하므로, `Stop()` 호출로 정리 필요
- 변경 감지 (`ServerCacheTypes.Skip`)가 활성화되면 코드 해시가 동일할 때 배포를 스킵
- pg_cron 등록은 Supabase Management API의 `RunQuery`를 사용하므로 Access Token 필요
