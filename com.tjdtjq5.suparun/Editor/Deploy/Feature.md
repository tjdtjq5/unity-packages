# Deploy

- **상태**: stable
- **용도**: Unity에서 [Table]/[Config]/[Service] 클래스를 스캔하여 ASP.NET 서버 코드를 자동 생성하고, GitHub에 push 후 Cloud Run에 배포하는 파이프라인

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
| `ServerCodeGenerator.cs` | `static class` | [Table]/[Config]/[Service] 리플렉션 스캔 → ASP.NET Controller/Migration/DTO/Admin 코드 생성 |
| `GitHubPusher.cs` | `static class` | gh CLI로 레포 clone → 파일 쓰기 → commit/push + GitHub Secrets 설정 |
| `ActionsTracker.cs` | `static class` | GitHub Actions 워크플로우 상태 폴링 (15초 간격) + 성공/실패 결과 수집 |
| `ServerCacheHealthChecker.cs` | `static class` | 배포 스냅샷 저장, 코드 변경 감지(SHA256), .NET 버전 변경/캐시 만료 경고 |
| `ServerCacheTypes.cs` | `static class` | 서버 캐시 타입 상수 정의 (NuGet, Docker, Skip) |

## API

### DeployManager

| 메서드 | 설명 |
|--------|------|
| `GenerateFiles(settings, onProgress?)` | [Table]/[Config]/[Service] 스캔 → 서버 코드 + 템플릿 + 공유 파일 생성. `(List<GeneratedFile>, Type[], error)` 반환 |
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

| 메서드 | 설명 |
|--------|------|
| `Push(settings, files, onSuccess, onFailed)` | gh CLI로 레포 clone → 파일 교체 → commit/push + GitHub Secrets 자동 설정 |

### ActionsTracker

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `StartTracking(repo)` | GitHub Actions 폴링 시작 (15초 간격, 10분 타임아웃) |
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
