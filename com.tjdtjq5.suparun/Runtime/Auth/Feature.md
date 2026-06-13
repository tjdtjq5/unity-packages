# Auth Feature

- **상태**: stable
- **용도**: Supabase 인증 시스템. 게스트 자동 로그인, OAuth 소셜 로그인, 플랫폼 네이티브 인증(GPGS/Game Center), 토큰 관리.

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| Models | `../Models/` | AuthSession, AuthTokenResponse, BanStatus |
| Client | `../Client/` | `IServerClient` (late-bind via `AttachServerClient`). 역으로 SupaRunAuth는 `ISessionProvider`로 Client에 토큰을 제공(pull). |

> **P1-3 (2026-04-08)**: `SupaRunAuth → SupaRun.Client` 정적 의존을 `IServerClient` 인터페이스 주입으로 대체. 의존성 사이클 끊김.
>
> **P2-2 (2026-04-09)**: `SecureStorage` 정적 클래스를 `ISessionStorage` 인터페이스 + 인스턴스 구현체로 분리. MPPM Virtual Player 자동 키 prefix 분리 (인스턴스마다 별도 게스트 계정).
>
> **Auth 생명주기 리팩터 (2026-06-14)**: 3건 동시 수정.
> 1. **temporal coupling 버그 제거** — `EnsureLoggedIn`/`TryRefreshToken`의 dedup을 `SingleFlight`로 추출. 이전엔 launch 후 `_loginUcs` *필드*를 다시 읽어, 동기 어댑터(테스트 mock)에서 NRE → EditMode 테스트 5건 실패. 이제 ucs를 로컬에 캡처 → sync/async 모두 안전, 동시 refresh도 dedup.
> 2. **토큰 단일 home (`ISessionProvider`)** — HTTP/REST 클라이언트가 `_client.Session` 미러 대신 `SupaRunAuth.CurrentSession`에서 요청 시 pull. Realtime 소켓만 `OnSessionChanged`로 push(소켓은 pull 불가 — 유일 반응자 = 진짜 seam).
> 3. **`SupaRunAuth : IDisposable`** — `OAuthHandler` deep-link 구독/HttpListener를 `SupaRunRuntime.Dispose → _auth.Dispose()`로 정리(이전엔 재생성마다 누수). 생성 순환은 Auth를 먼저 만들고 `AttachServerClient`로 client를 late-bind해 해소.

## 구조

```
Auth/
├── SupaRunAuth.cs               # 인증 메인 클래스 (게스트/OAuth/플랫폼/토큰 관리). ISessionProvider + IDisposable. 이전 이름: SupabaseAuth (P1-2)
├── ISessionProvider.cs           # 토큰의 단일 home. HTTP 클라이언트가 요청 시 pull (SupaRunAuth가 구현)
├── SingleFlight.cs               # 동시 호출 dedup 프리미티브 (login/refresh 공용). ucs 로컬 캡처로 동기 완료에도 NRE 없음
├── ISessionStorage.cs            # P2-2: 세션 저장소 추상화 (KV API, 6 메서드)
├── SecureSessionStorage.cs       # P2-2: 플랫폼 보안 저장소 구현체 (iOS Keychain / Android KeyStore / PC PlayerPrefs) + key prefix
├── MemorySessionStorage.cs       # P2-2: 메모리 Dictionary 구현체 (테스트/임시용)
├── OAuthHandler.cs               # OAuth 브라우저 플로우 (모바일: 딥링크, PC: localhost HTTP)
├── AuthProvider.cs               # 인증 제공자 enum (Guest, Google, Apple, GPGS 등 13종)
└── Platform/
    ├── IPlatformAuth.cs          # 플랫폼 네이티브 인증 인터페이스
    ├── GPGSAuthHandler.cs        # Google Play Games 인증 (Reflection으로 SDK 감지)
    └── GameCenterAuthHandler.cs  # Apple Game Center 인증
```

> **이전 구조에서 제거됨 (P2-2)**: `SecureStorage.cs` 정적 클래스 — `SecureSessionStorage` 인스턴스로 대체. 플랫폼 분기 로직(iOS Keychain / Android KeyStore / PC PlayerPrefs)은 보존됨.

## API

### SupaRunAuth : ISessionProvider, IDisposable

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `EnsureLoggedIn()` | 자동 로그인. 저장된 세션 복원 -> 만료 시 갱신 -> 실패 시 익명 로그인. 동시 호출 dedup (`SingleFlight`). |
| `SignIn(AuthProvider)` | 소셜/플랫폼 로그인. Guest/GPGS/GameCenter/OAuth 분기. |
| `LinkProvider(AuthProvider)` | 게스트 계정에 소셜 계정 연결 (Supabase Identity Link API). |
| `SignOut()` | 로그아웃 후 게스트로 자동 재생성. |
| `DeleteAccount()` | 계정 삭제 (서버 service_role로 Supabase 유저 삭제). |
| `CheckBan()` | 밴 체크 (서버에서 확인). |
| `TryRefreshToken()` | 토큰 갱신. 동시 호출(여러 401 동시 발생) dedup → refresh POST 1회. |
| `ClearSession()` | 세션 클리어 (로컬 저장소 포함). |
| `AttachServerClient(IServerClient?)` | 서버 의존 client late-bind (생성 순환 회피, internal). |
| `Dispose()` | OAuthHandler deep-link 구독/HttpListener 정리. `SupaRunRuntime.Dispose`가 호출. |
| `Session` | 현재 AuthSession (읽기 전용). |
| `CurrentSession` | `ISessionProvider` 구현 — 토큰의 단일 home. `Session`과 동일 값. 클라이언트가 요청 시 pull. |
| `IsLoggedIn` | 로그인 여부. |
| `UserId` | 현재 유저 ID. |
| `IsGuest` | 게스트 여부. |
| `OnSessionChanged` | 세션 변경 이벤트 (Action\<AuthSession\>). |
| `OnSessionExpired` | 세션 만료 이벤트. |
| `OnKicked` | 다른 기기 로그인 시 이벤트 (미구현). |
| `OnBanned` | 밴 감지 이벤트 (Action\<string\> reason). |

### OAuthHandler

| 메서드 | 설명 |
|--------|------|
| `Authenticate(AuthProvider)` | OAuth 로그인. 브라우저 열고 토큰 대기 (120초 타임아웃). |
| `AuthenticateForLink(AuthProvider, string accessToken)` | 게스트->소셜 연결용 Identity Link 플로우. |
| `ParseTokensFromUrl(string url)` | URL에서 access_token/refresh_token 추출 (static). |
| `GetRedirectUrl(string cloudRunUrl)` | 현재 플랫폼에 맞는 Redirect URL (static). |
| `GetRequiredRedirectUrls(...)` | Supabase에 등록해야 할 URL 목록 (static). |
| `Dispose()` | 딥링크 구독 해제 + HTTP 서버 정리. |

### ISessionStorage (interface) — P2-2

| 메서드 | 설명 |
|--------|------|
| `Set(key, value)` | 문자열 저장. value가 빈 문자열이면 Delete와 동일. |
| `Get(key, defaultValue)` | 문자열 읽기. 없으면 defaultValue. |
| `SetInt(key, value)` | 정수 저장. |
| `GetInt(key, defaultValue)` | 정수 읽기. |
| `Delete(key)` | 키 삭제. |
| `Save()` | 동기 플러시 (PlayerPrefs.Save). 메모리 구현체는 no-op. |

#### 구현체

| 구현체 | 용도 |
|--------|------|
| `SecureSessionStorage(string keyPrefix = "")` | 플랫폼 보안 저장소. iOS Keychain / Android KeyStore / PC PlayerPrefs. **prefix 옵션**: MPPM Virtual Player가 자체 키 사용 시. |
| `MemorySessionStorage()` | Dictionary 기반. 테스트/임시 세션. 프로세스 종료 시 사라짐. |

#### MPPM 자동 분리

`SupaRun.AutoInitialize()` 가 `GetMppmInstanceId()` 를 호출하여 현재 Editor가 MPPM Virtual Player인지 감지. VP면 인스턴스 ID(예: `mppm40870be5`)를 prefix로 사용 → Main과 별도 게스트 계정. 빌드/Main Editor는 prefix 없음(기존 호환).

### AuthProvider (enum)

Guest, Google, Apple, Facebook, Discord, Twitter, Kakao, Twitch, Spotify, Slack, GitHub, GPGS, GameCenter

### IPlatformAuth (interface)

| 멤버 | 설명 |
|------|------|
| `Provider` | AuthProvider enum 값. |
| `IsAvailable` | 현재 플랫폼에서 사용 가능 여부. |
| `GetToken()` | 플랫폼 SDK로 인증 후 토큰 반환. |

## 주의사항

- `EnsureLoggedIn()`/`TryRefreshToken()`은 `SingleFlight`로 동시 호출 dedup. 여러 곳에서 호출해도 안전하며, 동기 어댑터(테스트 mock)에서도 NRE 없이 동작한다.
- 토큰은 `SupaRunAuth.CurrentSession`이 단일 home. HTTP/REST 클라이언트는 `ISessionProvider`로 요청 시 pull하고, Realtime 소켓만 `OnSessionChanged`로 push받는다(소켓은 pull 불가).
- `SupaRunAuth`는 `IDisposable`. `OAuthHandler`(deep-link 구독 + HttpListener)를 정리하므로 `SupaRunRuntime.Dispose`에서 `_auth.Dispose()`가 호출되어야 한다.
- PC OAuth는 localhost에 임시 HTTP 서버를 열어 fragment 토큰을 수신한다 (2단계: HTML->JS->fetch).
- 모바일 OAuth는 딥링크(`{bundleId}://auth`)로 토큰을 수신한다. Supabase > Auth > Settings에서 Site URL에 딥링크 스킴을 등록해야 한다.
- `GPGSAuthHandler`는 Reflection으로 GPGS SDK를 감지하므로 SDK 미설치 시에도 컴파일 가능하다.
- Android `SecureStorage`는 KeyStore 초기화 실패 시 PlayerPrefs로 fallback한다.
- iOS Keychain은 네이티브 플러그인(`Plugins/` 폴더)이 필요하다.
