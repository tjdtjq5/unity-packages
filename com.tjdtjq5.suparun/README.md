# SupaRun

Unity Editor에서 게임 서버 인프라를 관리하는 올인원 패키지.
ASP.NET + Supabase + Cloud Run 자동 배포.

## 설치

manifest.json에 추가:

```json
"com.tjdtjq5.suparun": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.suparun#suparun/v0.5.1"
```

### 의존성

- `com.tjdtjq5.editor-toolkit` >= 1.1.0
- `com.unity.nuget.newtonsoft-json` >= 3.2.1

## 빠른 시작

```csharp
using Tjdtjq5.SupaRun;

// 1. 앱 진입점에서 명시적 로그인 (한 번)
await SupaRun.Login();

// 2. 데이터 조회 (서버 또는 LocalGameDB 자동 분기)
var stats = await SupaRun.GetAll<PlayerStatConfig>();
if (stats.success && stats.data != null)
{
    Debug.Log($"Loaded {stats.data.Count} stats");
}

// 3. 서비스 호출 (Source Generator로 자동 생성된 ServerAPI)
var result = await ServerAPI.CurrencyService.GetBalance(playerId);
```

## 주요 기능

- **명시적 로그인**: `SupaRun.Login()` — 게스트 자동 생성 또는 기존 세션 복원
- **데이터 API**: `SupaRun.Get<T>()`, `SupaRun.GetAll<T>()` — `[Config]` 타입은 PostgREST, `[Table]` 타입은 Cloud Run, 미배포는 LocalGameDB
- **Source Generator**: `[Service]` 클래스 → `ServerAPI.{Service}.{Method}` 정적 프록시 자동 생성
- **Auth**: Google/Apple/GameCenter/GPGS 플랫폼 로그인
- **세션 저장소**: 플랫폼별 보안 저장 (Android KeyStore, iOS Keychain, PC PlayerPrefs). MPPM Virtual Player 자동 분리.
- **실시간 채널**: Phoenix Channel 프로토콜 (Broadcast/Presence/PostgresChanges)
- **Cloud Run 배포**: ASP.NET 서버 자동 빌드 + 배포
- **Editor Window**: 통합 설정 + 배포 관리 UI

## 아키텍처 (v0.4.0+)

- **`SupaRun`** (정적 facade) — 호환성 진입점. 내부적으로 `SupaRunRuntime`에 위임.
- **`SupaRunRuntime`** (인스턴스) — 모든 자원 보유. 단위 테스트/DI에 직접 사용 가능.
- **`HttpExecutor` + Strategy 패턴** — `IAuthStrategy` + `IRetryStrategy` + `IAuthRefresher` 조합. mock transport로 단위 테스트 가능.
- **`ISessionStorage`** — `SecureSessionStorage` (플랫폼) / `MemorySessionStorage` (테스트). MPPM 자동 prefix 분리.
- **`IRealtimeClient`** — Realtime 추상화. `SupabaseRealtime` 또는 mock 주입.
- **`IAuthApi`** — Auth HTTP 추상화. `SupabaseAuthApi` 또는 mock 주입.
- **EditMode 단위 테스트 67개** — 전체 HTTP/Auth/Realtime 계층 mock 검증.

## 설정 파일 (v0.5.1+)

설정은 두 파일로 분리되어 저장된다:

| 파일 | git | 내용 |
|------|-----|------|
| `ProjectSettings/SupaRunProjectSettings.json` | ✅ 커밋 | URL, AnonKey, DB Password, Access Token, GitHub Token, Cron Secret, GCP/Auth 정책 등 (22개 필드) |
| `UserSettings/SupaRunUserSettings.json` | ❌ 미커밋 | `serverLogToConsole`, `setupCompleted` |

> ⚠ **시크릿(DB Password / Access Token / GitHub Token / Cron Secret)이 `ProjectSettings/`에 평문 저장되어 git에 커밋됩니다. private repo 전용 사용을 가정합니다.** 외부 공개 저장소에서는 사용하지 마세요.

기존 v0.4.x 사용자는 첫 실행 시 자동 마이그레이션됩니다 (단일 `UserSettings/SupaRunSettings.json` → 2개 파일 분배 + 원본은 `.bak`으로 백업).

## 디버깅

```csharp
// Verbose 로그 켜기 (HTTP POST 본문, LocalDB 작업 등)
SupaRun.Verbose = true;
```

## 라이선스

MIT
