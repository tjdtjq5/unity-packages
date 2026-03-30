# AspNetTemplate

> **상태**: stable
> **용도**: SupaRun 서버 배포 시 생성되는 ASP.NET Core 웹 서버 프로젝트 템플릿

## 의존성

- `../AdminTemplate~/` — `/admin` 경로에 정적 파일로 서빙하는 어드민 페이지
- 생성된 코드: `Generated/` 폴더의 Controller, IGameDB, DapperGameDB, AdminUser 등
- NuGet: Npgsql, Dapper, Microsoft.AspNetCore.Authentication.JwtBearer

## 구조

| 파일 | 설명 |
|------|------|
| `Program.cs.template` | ASP.NET Core 서버 진입점 (~189줄). DB, Auth, Rate Limiting, Migration, Admin 미들웨어 |
| `appsettings.json.template` | 앱 설정. Supabase DB 연결 문자열 + Auth URL |
| `Dockerfile.template` | 멀티 스테이지 Docker 빌드. SDK로 빌드 → aspnet 런타임으로 실행 |
| `SupaRun.csproj.template` | .NET 프로젝트 파일. Npgsql + Dapper + JWT Bearer 참조 |

## 플레이스홀더 변수

| 플레이스홀더 | 사용 파일 | 설명 |
|-------------|----------|------|
| `{{DOTNET_MAJOR}}` | csproj, Dockerfile | .NET 메이저 버전 (예: `9` → `net9.0`) |
| `{{SUPABASE_PROJECT_ID}}` | appsettings.json | Supabase 프로젝트 ID (Auth URL 구성용) |

## 주요 기능

### DB 연결
- **Dapper + Npgsql**: `IGameDB` 인터페이스를 `DapperGameDB`로 구현
- **ConnectionString**: `appsettings.json`의 `ConnectionStrings:Supabase`에서 읽음
- **Scoped 등록**: 요청당 DB 인스턴스 생성

### JWT 인증
- **Supabase Auth 연동**: `Authority`를 Supabase Auth URL로 설정
- **토큰 검증**: Audience 검증 비활성, `sub` 클레임을 NameClaim으로 사용
- **디버그 로깅**: `OnAuthenticationFailed`, `OnTokenValidated` 이벤트 콘솔 출력

### Rate Limiting
- **유저별 제한**: `sub` 클레임 또는 IP 기준, 분당 100건 고정 윈도우
- **어드민 면제**: `/admin/api` 경로는 Rate Limit 적용 안 함
- **429 응답**: 제한 초과 시 `429 Too Many Requests`

### Admin 미들웨어
- `/admin/api` 경로에 대해 `admin_users` 테이블 기반 인증
- **자동 등록**: 미등록 유저는 자동으로 `pending` 상태로 등록
- **첫 유저 자동 admin**: `admin_users` 테이블이 비어있으면 첫 가입자에게 `admin` 부여
- **UUID 자동 채움**: 이메일로만 사전 등록된 관리자의 `user_id` 자동 갱신

### Auto Migration
- 서버 시작 시 `Migrations/` 폴더의 `.sql` 파일을 이름순으로 실행
- 이미 적용된 마이그레이션은 예외를 `SKIP`으로 처리 (멱등)

### 정적 파일 서빙
- `admin/` 폴더가 존재하면 `/admin` 경로로 정적 파일 서빙
- `/admin` → `/admin/index.html` 리다이렉트

### 기타
- **Response Compression**: 응답 압축 활성화
- **Health Check**: `GET /health` → `"OK"`
- **Controller 직렬화**: public 필드 직렬화 활성화 (`IncludeFields = true`)

### Docker
- 멀티 스테이지 빌드: SDK 이미지로 빌드 → aspnet 런타임 이미지로 실행
- `Generated/Migrations` → `/app/Migrations`로 복사
- `admin` → `/app/admin`으로 복사
- 포트 8080 노출

## 주의사항

- `.template` 확장자 파일들은 SupaRun 배포 도구가 플레이스홀더를 치환 후 실제 프로젝트 파일로 생성
- `IGameDB`, `DapperGameDB`, `AdminUser` 등의 타입은 SourceGen 또는 배포 도구가 `Generated/` 폴더에 생성
- Controller 코드도 `Generated/` 폴더에 자동 생성됨 — 이 템플릿에는 포함되지 않음
- Migration SQL 파일은 `[Config]`, `[Table]` 어트리뷰트 기반으로 자동 생성됨
- `ConnectionStrings:Supabase`는 배포 환경에서 환경 변수 또는 시크릿으로 주입해야 함
