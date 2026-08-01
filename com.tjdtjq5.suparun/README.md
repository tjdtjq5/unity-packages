# SupaRun

Unity Editor에서 게임 서버 인프라를 관리하는 올인원 패키지.
ASP.NET + Supabase + Cloud Run 자동 배포.

## 설치

manifest.json에 추가:

```json
"com.tjdtjq5.suparun": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.suparun#suparun/v1.0.0"
```

### 의존성

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
- **데이터 API**: `SupaRun.Get<T>()`, `SupaRun.GetAll<T>()` — `[SpecData]` 타입은 PostgREST, `[UserData]` 타입은 Cloud Run, 미배포는 LocalGameDB
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

설정은 **값의 성격에 따라** 네 곳으로 나뉜다. 기준은 "누가 이 값의 유일한 주인인가" 다 —
같은 값을 두 곳이 다른 근거로 쓰면 반드시 어긋나기 때문이다.

| 저장소 | git | 내용 |
|------|-----|------|
| `ProjectSettings/SupaRunProjectSettings.json` | ✅ 커밋 | **부트스트랩뿐** — 환경 이름 · Supabase URL · anon key. 셋 다 공개값이고, 이것이 있어야 팀원이 클론만으로 붙는다 |
| EditorPrefs | ❌ 로컬 | 비밀(Access Token · DB 비밀번호 · GitHub Token · Cron Secret), 편집/빌드 환경 선택, 캐시 |
| `suparun_env` (Supabase) | — | 어드민이 정하는 값(GCP 프로젝트 · 리전 · 서비스명 · 레포 · 로그인 방식)과 Unity 가 굽는 사실(Cloud Run URL · 서비스계정) |
| `suparun_secret` (Supabase) | — | 팀이 공유해야 하는 비밀. **INSERT/UPDATE 정책만 있고 SELECT 는 없다** — 넣을 수는 있어도 읽어 갈 수는 없다 |

> **비밀은 git 에 남지 않는다.** 예전에는 `ProjectSettings/` 에 평문으로 들어가 private repo
> 전용을 가정했는데, 지금은 EditorPrefs 와 `suparun_secret` 으로 나갔다.
> Supabase Access Token(PAT)은 계정 마스터키라 **로컬에만** 둔다.

`UserSettings/SupaRunUserSettings.json` 은 더 이상 쓰지 않는다 — 마지막까지 남아 있던 두 값의
사용처(대시보드)가 없어지면서 파일 자체가 비었다. 남아 있다면 지워도 된다.

## 디버깅

```csharp
// Verbose 로그 켜기 (HTTP POST 본문, LocalDB 작업 등)
SupaRun.Verbose = true;
```

## 라이선스

MIT
