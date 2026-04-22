# EOS Auth — DeviceId Connect Login

## 상태
wip

## 용도
EOS Connect 로그인. OpenID token(Supabase JWT) 우선, DeviceId fallback. Plain class + VContainer DI.

## 의존성
- com.playeveryware.eos — EOSManager, ConnectInterface, CreateDeviceId, ProductUserId
- _Core/Bootstrap/GameInitializer — IAsyncStartable 초기화 체인에 삽입

## 포함 기능
- **EOSConnectLogin** (plain class) — DeviceId 생성 + Connect 로그인 비동기 처리
  - `LoginAsync()` — CreateDeviceId → StartConnectLoginWithDeviceToken → ProductUserId 획득
  - `IsLoggedIn` — 로그인 완료 여부
  - `LocalUserId` — 획득된 ProductUserId
  - `DuplicateNotAllowed` 처리 — DeviceId 이미 존재하면 성공으로 간주
- **GameInitializer 연동** — 생성자 주입 + `await _eosLogin.LoginAsync()` 삽입
- **GameLifetimeScope 등록** — `builder.Register<EOSConnectLogin>(Lifetime.Singleton)`

## 구조

| 파일 | 설명 |
|------|------|
| `EOSConnectLogin.cs` | DeviceId 생성 + Connect 로그인 비동기 처리 |

## API (외부 피처가 참조 가능)

- `EOSConnectLogin.LoginAsync(string openIdToken = null) -> Awaitable<bool>` — `EOSConnectLogin.cs`
- `EOSConnectLogin.IsLoggedIn -> bool` — `EOSConnectLogin.cs`
- `EOSConnectLogin.LocalUserId -> ProductUserId` — `EOSConnectLogin.cs`

## 주의사항
- EOSManager는 MonoBehaviour (DontDestroyOnLoad), EOSConnectLogin은 plain class — EOSManager.Instance를 직접 참조
- EOSManager 초기화보다 GameInitializer.StartAsync가 늦게 실행되므로 타이밍 안전
- DeviceId는 기기당 1회 생성, 이후 재사용 (DuplicateNotAllowed = 정상)
- LoginAsync 실패 시 로그 경고 + 계속 진행 (기존 SupaRun.Login 실패 패턴과 동일)
