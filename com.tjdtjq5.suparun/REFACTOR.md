# SupaRun 리팩터 로드맵

> 시작일: 2026-04-08
> 상태: **P0+P1+P2 ✅ 완료 (2026-04-09). P3는 선택 사항**
> 작성 배경: SurvivorsDuo 멀티플레이 테스트 중 PlayerStatConfig 로드 실패 디버깅 → 구조적 문제 다수 발견 → ultrathink 평가 → 4단계 리팩터 결정

## 다음 작업할 사람을 위한 가이드

**현재 상태**: SupaRun 패키지는 안정 라이브러리. 게임 본 작업으로 돌아가도 무방.

**P3 시작 시점**: 필요해질 때만. "P3 — 장기" 섹션 참조.

**작업 진입 순서**:
1. 이 문서의 "진행 상황" 표 확인
2. 시작할 작업의 **"추천 시점"** 확인 — 지금 진짜 필요한가?
3. 작업의 **"가치"** 와 **"한계"** 읽기
4. **"작업 단계"** 체크박스 따라 진행
5. 작업 후 체크박스 + 진행 상황 표 업데이트

## 배경

SurvivorsDuo 프로젝트에서 Multiplayer Play Mode(MPPM) Virtual Player 로 멀티플레이 테스트 중,
`PlayerStatConfig` 로드가 `success=true, count=0` 으로 실패하는 현상 발견.
원인을 추적하면서 SupaRun 패키지 전반의 구조적 문제가 드러남.

표면 버그(로그인 경합)는 패치했지만, 근본적으로는 다음 문제들이 있음:
- 데드 코드 + 동명 클래스 충돌 가능성
- 책임 분리 위반 (양방향 정적 의존)
- 초기화 진입점 3개 분산
- HTTP 클라이언트 3개 분산 (헤더/리트라이/로깅 정책 제각각)
- 정적 클래스 → 테스트 불가

자세한 평가는 SurvivorsDuo `.claude/sessions/` (해당 세션 로그) 참조.

---

## P0 — 즉시 (안정화)

> 목표: 새로 만든 버그 정리 + 사용자가 겪던 silent failure 차단
> 예상 작업량: 30분

### P0-1. `SupaRun._loginTask` 중복 상태 제거
- **문제**: `Runtime/Client/SupaRun.cs:19` 에 추가한 `_loginTask` 가 `SupabaseAuth._loginTask` 와 이중 상태. SupabaseAuth는 finally에서 null로 리셋하는데 SupaRun은 안 함 → SignOut 후 재로그인 안 됨
- **수정**: `SupaRun._loginTask` 필드 삭제. `SupaRun.Login()` 을 다음으로 교체:
  ```csharp
  public static async Task Login()
  {
      if (!_initialized) AutoInitialize();
      if (_auth == null) { LogError; return; }
      if (_auth.IsLoggedIn) return;        // 이미 됨
      await _auth.EnsureLoggedIn();         // auth 내부 dedup
  }
  ```
- **위치**: `Runtime/Client/SupaRun.cs:19, 113-130`
- [x] 작업 완료 (2026-04-08)
- [ ] 컴파일 확인
- [ ] 동작 테스트 (Main + Virtual Player)

### P0-2. `WaitForAuth()` 의미 재정의 (안전망 강화)
- **문제**: 현재 구현은 Login 미호출 시 에러 로그만 찍고 진행 → silent failure
- **수정**: `IsLoggedIn` 체크 + 미로그인이면 `EnsureLoggedIn()` 자동 호출 (안전망)
  ```csharp
  public static async Task WaitForAuth()
  {
      if (_auth == null) return;
      if (_auth.IsLoggedIn) return;
      Debug.LogError("[SupaRun] Login() 미호출 — generated proxy 호출 전에 SupaRun.Login() 필수");
      await _auth.EnsureLoggedIn();  // 안전망
  }
  ```
- **위치**: `Runtime/Client/SupaRun.cs:131-148`
- [x] 작업 완료 (2026-04-08)

### P0-3. `SupaRun.Initialize(ServerConfig)` Obsolete 표시
- **문제**: public인데 부분 초기화만 함 (Auth/RestClient 안 만듦) — 호출하면 망함
- **수정**: `[Obsolete("Use SupaRun.Login() instead. AutoInitialize handles config loading.")]` 추가
- **위치**: `Runtime/Client/SupaRun.cs:36-42`
- [x] 작업 완료 (2026-04-08)

### P0-4. `SupabaseRestClient` anonymous 호출 경고
- **문제**: 세션 없이 호출되면 RLS에 막혀도 success=true, count=0 로 silent
- **수정**: 세션 없이 Fetch 시 LogWarning 추가
  ```csharp
  if (Session == null || string.IsNullOrEmpty(Session.accessToken))
      Debug.LogWarning($"[SupaRun:REST] anonymous 호출 ({url}) — RLS authenticated 정책에 막힐 수 있음");
  ```
- **위치**: `Runtime/Client/SupabaseRestClient.cs:60-91`
- [x] 작업 완료 (2026-04-08)

### P0-5. SupaRun settings 절대 경로 사용 (MPPM 호환, **진짜 root cause fix**)
- **문제**: `SupaRun.cs:147` 의 `const string settingsPath = "UserSettings/SupaRunSettings.json";` 가 **상대 경로**. 더 깊게 들어가면 MPPM Virtual Player는 자체 가상 프로젝트 루트를 가짐:
  - Main: `<projectRoot>/Assets`
  - Virtual Player: `<projectRoot>/Library/VP/<vp-id>/Assets`
  - VP에서 `Application.dataPath` + `..` 로 가면 `<projectRoot>/Library/VP/<vp-id>/` 가 나와서 진짜 UserSettings/ 와 다름.
- **수정**: dataPath의 부모에서 `/Library/VP/<id>/` 패턴을 감지하면 거슬러 올라가 진짜 프로젝트 루트 도출
  ```csharp
  var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
  var vpIdx = projectRoot?.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase) ?? -1;
  if (vpIdx > 0) projectRoot = projectRoot.Substring(0, vpIdx);
  ```
- **위치**: `Runtime/Client/SupaRun.cs:182-196`
- [x] 작업 완료 (2026-04-08, 2회 수정)

### P0-6. LocalGameDB fallback 진단 로그 (silent failure 가드 확장)
- **문제**: `SupaRun.GetAll<T>` 에서 `_client == null` 이면 LocalGameDB로 fallback. LocalGameDB가 비어있으면 success=true + count=0 (또 다른 silent failure). P0-4 가드는 RestClient 안에 있어서 이 경로를 못 잡음.
- **수정**: 첫 LocalDB fallback 시 한 번만 LogWarning 출력 (static flag로 dedup). `WarnLocalDbFallbackOnce()` 헬퍼 추가, `Get<T>` / `GetAll<T>` 양쪽에서 호출.
- **위치**: `Runtime/Client/SupaRun.cs:34-46, 60-91`
- [x] 작업 완료 (2026-04-08)

### P0 종료 조건
- [x] 전체 컴파일 통과 (2026-04-08, 에러 0)
- [x] MPPM Main + Virtual Player 둘 다 PlayerStatConfig 25행 정상 로드 (2026-04-08)
- [x] 콘솔에 의도하지 않은 경고/에러 없음 (Server Tick Batching 노이즈는 ECS 네트코드 관련, P0와 무관)

### P0 검증 로그 (2026-04-08)
```
[GameInitializer] 초기화 완료                                              ← Main Editor
[DataInitializer] 데이터 + 비주얼 초기화 완료                                  ← Main Editor
[Player 2] [SupaRun:Auth] 세션 복원: 4ce768d8-dd7b-43a0-aae4-a368af758b40    ← Virtual Player 첫 성공
[Player 2] [DataInitializer] 데이터 + 비주얼 초기화 완료
[Player 2] [GameInitializer] 초기화 완료
```

---

## P1 — 다음 사이클 (정리 + 책임 분리 1단계)

> 목표: 데드 코드 정리, 이름 충돌 해결, 양방향 의존 끊기
> 예상 작업량: 1~2시간

### P1-1. `Runtime/Supabase/` 데드 코드 삭제
- **문제**: `SupabaseClient`, `Supabase/SupabaseAuth(스텁)`, `Supabase/SupabaseStorage(스텁)` 모두 미사용. 스텁은 TODO만 있음.
- **삭제 대상**:
  - `Runtime/Supabase/SupabaseClient.cs` (+ .meta) ✅
  - `Runtime/Supabase/SupabaseAuth.cs` (+ .meta) ✅
  - `Runtime/Supabase/SupabaseStorage.cs` (+ .meta) ✅
- **수정**: `Runtime/Supabase/SupabaseRealtime.cs:40-44` 의 `SupabaseRealtime(SupabaseClient client)` 생성자 삭제 ✅
- **확인**: `Runtime/Supabase/Feature.md` 갱신 ✅
- **컴파일**: 에러 0, 경고 0 ✅
- [x] 작업 완료 (2026-04-08)

### P1-2. `SupabaseAuth` 이름 충돌 해결
- **문제**: `Tjdtjq5.SupaRun.SupabaseAuth` (Auth/) vs `Tjdtjq5.SupaRun.Supabase.SupabaseAuth` (Supabase/, 스텁) 두 개. P1-1 후 스텁은 사라졌으나, 명확성을 위해 실제 클래스도 리네이밍.
- **수정**:
  - `Runtime/Auth/SupabaseAuth.cs` → `SupaRunAuth.cs` 파일 + .meta 리네이밍 ✅
  - 클래스명 `SupabaseAuth` → `SupaRunAuth` ✅
  - 생성자명 `SupabaseAuth(...)` → `SupaRunAuth(...)` ✅
  - `SupaRun.cs:17, 110, 266` 의 타입 참조 교체 ✅
  - `SupaRun.cs:133, 146` 의 주석 안 언급 교체 ✅
  - `Runtime/Auth/Feature.md` 의 모든 `SupabaseAuth` → `SupaRunAuth` 일괄 교체 ✅
- **컴파일**: 에러 0 (경고 7개는 SupaRun 무관) ✅
- [x] 작업 완료 (2026-04-08)

### P1-3. `SupaRunAuth → SupaRun.Client` 역방향 의존 제거
- **문제**: `Runtime/Auth/SupaRunAuth.cs:359, 383, 416` 에서 `SupaRun.Client` 정적 호출 → 순환 의존 (`SupaRun → SupaRunAuth → SupaRun`).
- **선택**: 옵션 B (IServerClient 인터페이스). P2-1의 사전작업으로 자연스럽게 이어짐. ISP 위반 0 (SupaRunAuth 사용 메서드 = SupaRunClient public API와 1:1 매칭).
- **수정**:
  - **`Runtime/Client/IServerClient.cs` 신규 생성** ✅ — 3개 메서드 (`GetAsync<T>`, `PostAsync<T>`, `PostAsync`)
  - **`SupaRunClient : IServerClient`** 추가 ✅ (시그니처 100% 일치, 변경 0)
  - **`SupaRunAuth`**: `_serverClient` 필드 + 생성자 nullable 파라미터 추가 ✅
  - **`DeleteAccount` / `CheckBan` / `SignInWithPlatform`** 의 `SupaRun.Client` → `_serverClient` 교체 ✅. null 가드 명확화 + DeleteAccount는 IServerClient 미주입 시 로컬 세션만 정리하도록 LogWarning 추가.
  - **`SupaRun.cs:266`**: `new SupaRunAuth(supabaseUrl, anonKey, url, _client)` 로 변경 ✅
  - **`Auth/Feature.md`**, **`Client/Feature.md`** 갱신 ✅
- **검증**: `grep "SupaRun\." SupaRunAuth.cs` 결과 주석 1줄만 (실제 정적 호출 0)
- **컴파일**: 에러 0 (경고 7개 모두 SupaRun 무관) ✅
- **양방향 콜백**: `_client.OnTokenRefresh = () => _auth.TryRefreshToken()` 은 `Func` 델리게이트 약한 결합으로 유지. SupaRunClient가 SupaRunAuth의 구체 타입을 모르므로 OK. P2-1에서 다시 검토.
- [x] 작업 완료 (2026-04-08)

### P1-4. 로깅 verbose 게이트
- **문제**: `SupaRunAuth.Post()` 가 매 요청마다 INFO 로그 (URL + Response 200자). `LocalGameDB.Log()` 도 모든 Get/Save/Delete를 출력. 운영 노이즈.
- **수정**:
  - **`SupaRun.Verbose`** 정적 프로퍼티 추가 (기본 `false`) ✅ — `Runtime/Client/SupaRun.cs:34-43`
  - **`SupaRun.LogVerbose(string)`** internal 헬퍼 추가 ✅
  - **`SupaRunAuth.Post()`** 의 `Debug.Log` 2곳 → `SupaRun.LogVerbose` 교체 ✅ (`Runtime/Auth/SupaRunAuth.cs:188, 209`)
  - **`LocalGameDB.Log()`** 헬퍼 1곳 → `SupaRun.LogVerbose` 교체 ✅ (`Runtime/DB/LocalGameDB.cs:453-456`) → 호출하는 모든 GetAll/Save/Delete 등 자동 적용
- **유지** (verbose로 안 가림): `LogWarning`/`LogError`, `[SupaRun:Auth] 게스트 생성/세션 복원` 같은 1회성 핵심 이벤트
- **사용법**:
  ```csharp
  // 디버깅 시 코드에서 한 줄로 토글
  SupaRun.Verbose = true;
  ```
  자연스러운 호출 위치: `GameInitializer.StartAsync()` 진입점 또는 `#if UNITY_EDITOR` 가드
- **컴파일**: 에러 0 ✅
- [x] 작업 완료 (2026-04-08)

### P1 종료 조건
- [x] 전체 컴파일 통과 (2026-04-08, 에러 0)
- [x] 데드 코드 0개 (P1-1)
- [x] 정적 의존 사이클 0개 (P1-3, IServerClient 추상화)
- [x] Editor 콘솔 노이즈 감소 — `Verbose=false` 기본값으로 verbose 로그 숨김 (P1-4)

---

## P2 — 큰 리팩터 (책임 분리 2단계)

> 목표: HTTP 클라이언트 통합, 추상화 도입, 테스트 가능성 확보
> 예상 작업량: 1~2일

### P2-1. HTTP 클라이언트 통합 (Strategy 패턴 / Composition)
- **현재**: 3개 분산 클라이언트
  - `SupaRunClient` (Cloud Run, 토큰 갱신, 재시도)
  - `SupabaseRestClient` (PostgREST [Config])
  - `SupaRunAuth.Post()` (inline HTTP, Auth API용)
- **선택**: **옵션 D — Strategy 패턴 (Composition)**
  - `IHttpTransport` (raw HTTP) + `IAuthStrategy` (헤더 정책) + `IRetryStrategy` (재시도) + `IAuthRefresher` (옵션, 401 갱신) → `HttpExecutor` 가 조합
  - 각 client는 자기 정책의 strategy 조합으로 executor 생성
- **`SupaRunAuth.Post` 는 P2-1 범위 외 (스킵)**:
  - 이유: 의존성 인플레이션 방지 (생성자 인자 5→6 회피), private 메서드라 외부 영향 0, 호출 빈도 매우 낮음, Strategy 이점 적음
  - 미래: P2-3 또는 P3에서 `SupabaseAuthApi` 별도 클래스로 분리 (큰 작업)

#### 7단계 분할

##### P2-1a. DTO/인터페이스 신규 (6개 파일)
- `Runtime/Client/IHttpTransport.cs` ✅
- `Runtime/Client/HttpTransportRequest.cs` ✅
- `Runtime/Client/HttpTransportResponse.cs` ✅
- `Runtime/Client/Strategies/IAuthStrategy.cs` ✅
- `Runtime/Client/Strategies/IAuthRefresher.cs` ✅
- `Runtime/Client/Strategies/IRetryStrategy.cs` ✅
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

##### P2-1b. `UnityHttpTransport` 구현
- `Runtime/Client/UnityHttpTransport.cs` ✅
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

##### P2-1c. Strategy 구현체 7개
- Auth: `BearerTokenAuth` ✅, `BearerJwtOrAnonAuth` ✅, `ApiKeyOnlyAuth` ✅, `NoAuth` ✅
- Retry: `NoRetry` ✅, `ExponentialBackoffRetry` ✅
- Refresher: `CallbackAuthRefresher` ✅
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

##### P2-1d. `HttpExecutor` 신규 + 컴파일 검증
- `Runtime/Client/HttpExecutor.cs` ✅ — IHttpTransport + IAuthStrategy + IRetryStrategy + IAuthRefresher 조합. 401 처리 + 재시도 루프.
- 컴파일: 에러 0 ✅ (신규 14개 파일 모두 통과)
- [x] 작업 완료 (2026-04-09)

##### P2-1e. `SupabaseRestClient` 마이그레이션 (RestClient 먼저)
- `_executor` 필드 추가, 생성자에서 strategy 조합 (`BearerJwtOrAnonAuth` + `NoRetry`) ✅
- `Fetch<T>` 가 `HttpExecutor.ExecuteAsync` 사용 ✅
- anonymous 가드 (P0-4), isAuthenticated/hint 메타 (P2-4) 보존 ✅
- JSON 파싱 에러를 명시적 BadRequest로 분류 (이전엔 catch에서 NetworkError로 잘못 분류) ✅
- 컴파일: 에러 0 ✅
- **MPPM 테스트 (2026-04-09)**:
  ```
  [Main]      [SupaRun:Auth] 토큰 갱신: 4ce768d8-...    ← 자동 토큰 갱신까지 정상 동작
  [Main]      [DataInitializer] 데이터 + 비주얼 초기화 완료
  [Player 2]  [SupaRun:Auth] 세션 복원: 9d163032-...   ← P2-2 자체 게스트 정상
  [Player 2]  [DataInitializer] 데이터 + 비주얼 초기화 완료
  [Player 2]  [WeaponTestInitializer] 무기 장착 완료: new
  ```
  Strategy 패턴 정상 동작. PlayerStatConfig 25행 양쪽 모두 정상 로드. anonymous 경고 0건.
- [x] 작업 완료 (2026-04-09)

##### P2-1f. `SupaRunClient` 마이그레이션
- 전체 재작성 (175줄 → 145줄, UnityWebRequest 직접 사용 코드 제거)
- `RequestWithRetry<T>` / `SendRequest<T>` 제거 ✅ — executor가 401 갱신 + 재시도 처리
- Strategy 조합:
  - `BearerTokenAuth(() => Session)` ✅ — Session lazy read
  - `ExponentialBackoffRetry(maxAttempts: 3, baseDelayMs: 1000)` ✅ — 1s/2s/4s 백오프 (기존과 동일)
  - `CallbackAuthRefresher(async () => OnTokenRefresh != null ? await OnTokenRefresh() : null)` ✅ — 콜백 lazy read
- `BuildRequest`/`ParseResponse`/`ClassifyError` 메서드는 보존하되 시그니처를 `HttpTransportResponse` 로 변경 ✅
- public API (`GetAsync`, `PostAsync<T>`, `PostAsync`, `Session`, `OnTokenRefresh`) 그대로 — IServerClient 호환 유지
- `using UnityEngine`, `using UnityEngine.Networking` 제거 ✅
- **검증**: `grep "UnityWebRequest" SupaRunClient.cs` → 주석 1줄만 (실제 코드 0)
- **동작 보존 검증** (25개 이슈 사전 체크):
  - 재시도 횟수 (4번 호출, 1s/2s/4s 대기) ✓
  - 401 갱신 흐름 (1회만, fall-through 시 즉시 종료) ✓
  - ParseResponse/ClassifyError 시그니처 1:1 매핑 ✓
  - JsonException catch (plain text 응답) 그대로 ✓
  - PostAsync<object> 변환 (P2-4 메타데이터 복사 추가) ✓
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

##### P2-1g. `SupaRun.cs:AutoInitialize` 단일 transport 통합
- 단일 `UnityHttpTransport` 인스턴스 생성 ✅ — 패키지 전체 client가 공유
- `SupaRunClient(config, transport)` 주입 ✅
- `SupabaseRestClient(supabaseUrl, anonKey, transport)` 주입 ✅
- 컴파일: 에러 0 ✅
- **MPPM 종합 테스트 (2026-04-09)**:
  ```
  [Main]      [SupaRun:Auth] 세션 복원: 4ce768d8-...
  [Main]      [DataInitializer] 데이터 + 비주얼 초기화 완료
  [Main]      [WeaponTestInitializer] 무기 장착 완료: new
  [Player 2]  [SupaRun:Auth] 세션 복원: 9d163032-...
  [Player 2]  [DataInitializer] 데이터 + 비주얼 초기화 완료
  [Player 2]  [WeaponTestInitializer] 무기 장착 완료: new
  [Player 2]  [GameDataBridge] BlobAsset 등록 완료 — 16개 World, Enemies:2 Proj:1
  ```
  Strategy 패턴 양쪽 클라이언트 정상 동작. 회귀 0. 모든 의존 컴포넌트 정상.
- [x] 작업 완료 (2026-04-09)

### P2-2. 세션 저장소 추상화
- **현재**: `SecureStorage` 정적 + 플랫폼별 분기 (PlayerPrefs/Keychain/KeyStore)
- **목표**: `ISessionStorage` 인터페이스 + 인스턴스 구현체. 정적 SecureStorage 삭제.
- **수정**:
  - **`Runtime/Auth/ISessionStorage.cs`** 신규 ✅ — KV API 6 메서드
  - **`Runtime/Auth/SecureSessionStorage.cs`** 신규 ✅ — 기존 SecureStorage 로직 인스턴스화 + key prefix 지원
  - **`Runtime/Auth/MemorySessionStorage.cs`** 신규 ✅ — Dictionary 기반 (테스트/임시)
  - **`Runtime/Auth/SecureStorage.cs` 삭제** ✅ (+ .meta)
  - **`SupaRunAuth`** 수정 ✅ — `_storage` 필드 + 생성자 nullable 파라미터, 16개 SecureStorage 호출 → `_storage` 호출 교체
  - **`SupaRun.cs`** 수정 ✅:
    - `GetMppmInstanceId()` / `GetProjectRoot()` 헬퍼 추가 (`SupaRun.cs:34-77`)
    - P0-5 settings 로딩 코드를 `GetProjectRoot()` 호출로 리팩터 (DRY)
    - `AutoInitialize` 에서 `new SecureSessionStorage(GetMppmInstanceId())` 생성 후 SupaRunAuth에 주입
  - **`Auth/Feature.md`** 갱신 ✅
- **MPPM 자동 분리**: Main Editor → prefix 빈 문자열 (기존 호환). VP → 인스턴스 ID prefix → 자체 게스트 계정.
- **컴파일**: 에러 0 ✅
- **검증 (2026-04-09)**:
  ```
  [Main]      [SupaRun:Auth] 세션 복원: 4ce768d8-dd7b-43a0-aae4-a368af758b40   ← 기존 user_id 유지
  [Player 2]  [SupaRun:Auth] 게스트 생성: 9d163032-171c-4823-a070-70b67b683614  ← 자체 prefix → 새 계정 생성
  ```
  Main과 VP가 완전히 다른 게스트 계정으로 동작 → 데이터 격리 성공.
- [x] 작업 완료 (2026-04-09)

### P2-3. `SupaRun` 정적 → instance facade (옵션 A — Singleton wrapper)
- **목표**: 정적 클래스는 얇은 wrapper로 두고 내부 로직은 instance class
- **선택 이유**: 호환성 100% (codegen + 4곳 외부 호출자 변경 0). 옵션 C는 codegen 재작성 + ServerAPI 디자인 변경 부담이 큼. 옵션 A 후 P3에서 점진적으로 옵션 C 진화 가능.
- **신규**:
  - `SupaRunRuntimeOptions` — 옵션 객체 (의존성 주입용)
  - `SupaRunRuntime` (instance) — 모든 자원 보유, Login/Get/GetAll/WaitForAuth 메서드, IDisposable
  - `SupaRun` (static) — `SupaRunRuntime.Instance` lazy singleton facade

#### 5단계 분할

##### P2-3a. `SupaRunRuntimeOptions.cs` 신규
- `Runtime/Client/SupaRunRuntimeOptions.cs` ✅ — SupabaseUrl, AnonKey, CloudRunUrl, Transport, SessionStorage 5개 필드
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

##### P2-3b. `SupaRunRuntime.cs` 신규 (현 SupaRun.cs 로직 옮김)
- `Runtime/Client/SupaRunRuntime.cs` 신규 ✅ (~270줄)
  - 자원 필드 8개 (internal readonly): `_options`, `_transport`, `_client`, `_restClient`, `_auth`, `_realtime`, `_localDB`, `_sessionStorage`
  - public 프로퍼티 7개: `ServerClient`, `Auth`, `Realtime`, `LocalDB`, `SessionStorage`, `IsLoggedIn`, `CurrentSession`, `PlayerId`
  - 생성자 `SupaRunRuntime(SupaRunRuntimeOptions)` ✅ — 옵션 주입, AutoInitialize 로직 옮김
  - `CreateFromSettings()` static factory ✅ — settings JSON 로드 (Editor/Build 분기)
  - `LoadOptionsFromSettings()` private helper ✅
  - `Get<T>(object id)` / `GetAll<T>()` instance 메서드 ✅
  - `Login()` / `WaitForAuth()` instance 메서드 ✅
  - `OnAuthSessionChanged` private 메서드 ✅ (lambda → named method, 테스트 친화)
  - `IDisposable` 구현 ✅ — `_disposed` flag, 이벤트 핸들러 제거, Realtime.Disconnect
- `SupaRun.IsConfig<T>` 와 `WarnLocalDbFallbackOnce` 가시성 `private → internal` ✅ — SupaRunRuntime이 호출 가능
- 컴파일: 에러 0 ✅ (경고 7 모두 SupaRun 무관)
- **사용처 0** — SupaRun 정적 클래스는 아직 사용하지 않음 (P2-3c에서 facade로 변경)
- [x] 작업 완료 (2026-04-09)

##### P2-3c. `SupaRun.cs` 정적 facade로 재작성
- 기존 342줄 → 약 175줄 (-49%) ✅
- **삭제된 것**:
  - 정적 필드 6개: `_client`, `_restClient`, `_localDB`, `_auth`, `_realtime`, `_initialized`
  - `AutoInitialize()` 메서드 80줄 (SupaRunRuntime 생성자가 같은 일을 함)
  - `Get<T>` / `GetAll<T>` / `Login` / `WaitForAuth` 의 본문 로직
- **추가된 것**:
  - `static SupaRunRuntime _instance` + `_initLock`
  - `Instance` lazy 프로퍼티 (double-check locking, thread-safe)
- **유지된 것 (정적 헬퍼)**:
  - `Verbose`, `LogVerbose`, `GetMppmInstanceId`, `GetProjectRoot`
  - `IsConfig<T>`, `WarnLocalDbFallbackOnce` (P2-3b에서 internal)
  - `_configCache`, `_localDbFallbackWarned` private static
- **facade 위임 메서드/프로퍼티**:
  - `Get<T>`, `GetAll<T>`, `Login`, `WaitForAuth` → `Instance.X()`
  - `Auth`, `Realtime`, `LocalDB`, `CurrentSession`, `PlayerId`, `IsLoggedIn` → `Instance.X`
  - `Client` → `Instance._client` (구체 SupaRunClient 타입, codegen 호환)
  - `IsInitialized` → `_instance != null` (의미 약간 변경, 사용처 0)
- **deprecated**:
  - `Initialize(ServerConfig)` → noop + LogWarning (사용처 0, 시그니처만 호환)
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

##### P2-3d. 컴파일 검증 + MPPM 종합 테스트
- [x] 컴파일 0 ✅
- [x] MPPM Main + Virtual Player 정상 동작 (2026-04-09)
- **검증 로그**:
  ```
  [Main]      [SupaRun:Auth] 세션 복원: 4ce768d8-...
  [Main]      [DataInitializer] 데이터 + 비주얼 초기화 완료
  [Player 2]  [SupaRun:Auth] 토큰 갱신: 9d163032-...    ← 토큰 갱신 chain 정상
  [Player 2]  [DataInitializer] 데이터 + 비주얼 초기화 완료
  ```
- **검증 포인트**:
  - Lazy Singleton facade 정상 (SupaRun.Login() → Instance.Login())
  - codegen 호환 (SupaRun.WaitForAuth(), SupaRun.Client.PostAsync<T>())
  - 토큰 갱신 chain (401 → CallbackAuthRefresher → SupaRunAuth.TryRefreshToken → OnAuthSessionChanged → 모든 client에 전파)
  - PlayerStatConfig 25행 양쪽 모두 정상 로드
  - 모든 의존 컴포넌트 정상 (WeaponTest, GameDataBridge, InGameCore 등)

##### P2-3e. 도메인 리로드 cleanup + SetInstance internal API
- **`RegisterDomainReloadCleanup()`** (`UNITY_EDITOR` 가드) ✅
  - `[InitializeOnLoadMethod]` 로 자동 등록
  - `AssemblyReloadEvents.beforeAssemblyReload` 콜백에서 `_instance.Dispose() + null 설정`
  - Realtime WebSocket 연결 같은 자원 누수 방지
- **`internal static SetInstance(SupaRunRuntime)`** (`UNITY_EDITOR` 가드) ✅
  - 단위 테스트에서 mock SupaRunRuntime 주입
  - 기존 인스턴스 있으면 Dispose 후 교체
  - instance=null 이면 정리만 (다음 Instance 호출 시 새로 생성)
- 컴파일: 에러 0 ✅
- [x] 작업 완료 (2026-04-09)

### P2-4. 에러 메타데이터 강화
- **현재**: `ServerResponse<T>` 가 success/error/errorType/statusCode
- **추가**:
  - `isAuthenticated` (bool) — 호출 시점에 인증된 세션이 있었는지 ✅
  - `hint` (string) — 진단 힌트 (예: "anonymous 호출 — RLS 막힘 가능", "LocalDB fallback — 서버 미연결") ✅
- **활용**:
  - `SupabaseRestClient.Fetch<T>` — anonymous 호출 시 hint 채움 ✅ (`SupabaseRestClient.cs:67-118`)
  - `SupaRun.Get<T>` / `GetAll<T>` LocalDB fallback 시 hint 채움 ✅ (`SupaRun.cs:60-90`)
- **호출자가 분기 처리 가능**: 기존엔 `Debug.LogWarning` 만 발사 (사람이 보는 용도). 이제 코드에서 `if (response.hint != null) { ... }` 같이 프로그램 분기 가능.
- **범위 외**: `SupaRunClient` (Cloud Run 경로) 는 P2-1 HTTP 통합 시 같이 처리.
- **컴파일**: 에러 0 ✅
- [x] 작업 완료 (2026-04-08)

### P2 종료 조건
- [x] HTTP 클라이언트 통합 (P2-1, Strategy 패턴 + 단일 transport)
- [x] `ISessionStorage` 추상화 완료 (P2-2, MPPM 자동 prefix 분리)
- [x] `SupaRunRuntime` instance 도입 (P2-3, Singleton facade)
- [x] 단위 테스트 작성 가능 (mock 지원) — 인프라는 준비됨, 테스트 자체는 P3-2

---

## P3 — 장기 (선택)

> 목표: 코드젠 통합, 테스트 추가, 운영 안정성 강화
> P3는 모두 "있으면 좋음" 영역. 게임 본 작업 우선이면 미뤄도 OK.

### 다음 작업 시작 가이드

**현재 상태**: P0+P1+P2 완료. SupaRun 패키지는 안정 라이브러리. 게임 본 작업으로 돌아가도 무방.

**P3 시작 시점**:
- 패키지를 다른 사람과 협업 시작할 때 → 단위 테스트 + 코드젠 통합 우선
- 큰 리팩터를 또 할 때 → 단위 테스트 먼저 (안전망)
- 게임이 일정 안정화된 후 시간 여유 있을 때 → 점진적으로

**시나리오별 추천**:
| 시나리오 | 작업 | 시간 |
|---------|------|------|
| **A. 본 작업 우선** (현실적 추천) | 모두 미루기 | 0 |
| **B. DI만 빠르게** | DI 통합만 | 20분 |
| **C. DI + 핵심 단위 테스트** | DI + 단위 테스트 5~6개 | 1일 |
| **D. 모두** | DI + 단위 테스트 + 코드젠 통합 | 1.5~2일 |

---

### P3-1. SourceGen + BuildProcessor 통합

- **현재**: `ServiceGenerator.cs` (Roslyn IIncrementalGenerator) + `SupaRunBuildProcessor.cs` (reflection 기반 빌드 시점) 가 같은 `[Service]` contract를 두 번 처리. SG 출력 모양이 바뀌면 BuildProcessor 의 reflection 코드가 깨짐.
- **목표 (옵션 A — 추천)**: SG가 같은 메서드의 `#if UNITY_EDITOR` / `#else` 분기를 모두 emit. BuildProcessor 폐기.
- **목표 (옵션 B)**: 단일 IR(JSON 메타데이터) 도입. SG가 IR emit, BuildProcessor가 IR read.

**가치**:
- 유지보수 부담 절반 (한 곳만 신경)
- SG vs BuildProcessor 동기화 깨짐 위험 0
- 신규 개발자 onboarding 쉬움
- 빌드 안정성 ↑ (reflection 의존 제거)

**한계**: 현재 codegen이 검증된 코드 — "안 깨진 거 안 고치는 게 안전". Roslyn SG 디버깅 사이클이 길고 까다로움.

| 항목 | 평가 |
|------|------|
| 작업 양 | 1~2시간 (옵션 A) / 4~6시간 (옵션 B) |
| 난이도 | 높음 (Roslyn SG 디버깅) |
| 리스크 | 중간 (잘못 emit 시 모든 [Service] 호출 깨짐) |
| 단기 가치 | 낮음 |
| 장기 가치 | 높음 (협업/publish 시) |
| **추천 시점** | **새 [Service] 추가가 빈번해질 때 / 다른 사람과 협업 시작 시** |

**작업 단계**:
- [ ] 설계 — 옵션 A vs B 결정
- [ ] 구현
- [ ] 빌드 검증 (Editor + Android)

---

### P3-2. 단위 테스트 추가 ⭐ **가장 가치 큰 작업**

- **전제**: P2-3 instance 분리 ✅ 완료. P2-1 Strategy 패턴 ✅ 완료. → mock 주입 가능.
- **대상**:
  - `SupaRunAuth` 세션 복원/갱신/익명 로그인 (with `MemorySessionStorage`)
  - `BearerJwtOrAnonAuth` / `BearerTokenAuth` strategy 동작
  - `HttpExecutor` 의 401 갱신 + 5xx 백오프 (with `MockHttpTransport`)
  - `SecureSessionStorage` MPPM prefix 분리
  - `SupaRunRuntime` 통합 시나리오 (Get, GetAll)

**가치 (5가지)**:
1. **회귀 방지** — P0~P2에서 잡은 버그가 미래에 다시 발생하지 않도록 가드. 예: P0-1의 "SignOut → 재로그인" 버그
2. **리팩터 안전망** — P3-1 같은 큰 작업 시 안전망. "잘 동작하던 게 깨지는 줄 모르고 commit" 위험 0
3. **문서로서의 가치** — 단위 테스트 = 살아있는 사용 예시
4. **CI/CD 통합** — GitHub Actions 자동 검증, 패키지 publish 전 안정성 보장
5. **디자인 검증** — P0~P2 리팩터의 진짜 ROI 회수. 단위 테스트가 잘 써지면 디자인이 좋다는 증거.

**한계**:
- 작성 시간 4~6시간
- mock infrastructure (MockHttpTransport 등) 필요
- 코드 변경 시 테스트도 같이 유지

| 항목 | 평가 |
|------|------|
| 작업 양 | 4~6시간 (mock + 핵심 8~12개 테스트) |
| 난이도 | 보통 |
| 리스크 | 낮음 |
| 단기 가치 | 낮음 |
| 장기 가치 | **매우 높음** |
| **추천 시점** | **큰 리팩터 직전 / 패키지 stable 직전** |

**작업 단계**:
- [ ] `Tests/EditMode/` 폴더 + asmdef 신규
- [ ] `MockHttpTransport.cs` 헬퍼 작성
- [ ] 핵심 테스트 8~12개 작성
- [ ] (옵션) CI 통합 (GitHub Actions Unity Test Runner)

---

### P3-3. Realtime + Auth 토큰 동기화 통합 테스트

- **목표**: 토큰 갱신 시 Realtime 세션도 갱신되는지 검증
- **현재**: P2-3의 `OnAuthSessionChanged` 가 `_realtime.SetAccessToken(session.accessToken)` 호출 → 동작하지만 자동 검증 없음
- **가치**: Realtime + Auth 흐름 검증, 회귀 방지

| 항목 | 평가 |
|------|------|
| 작업 양 | 2~3시간 |
| 난이도 | 보통 |
| 리스크 | 낮음 |
| **추천 시점** | Realtime 채널 사용량이 늘어날 때 |

**작업 단계**:
- [ ] 테스트 시나리오 작성
- [ ] PlayMode 통합 테스트 자동화

---

### P3-4. 모든 public API에 nullable annotation

- **목표**: `string?`, `AuthSession?` 같은 nullable reference type 명시
- **가치**: IDE/컴파일러가 null 안전성 검증, 호출자 실수 방지

| 항목 | 평가 |
|------|------|
| 작업 양 | 1~2시간 |
| 난이도 | 매우 쉬움 |
| 리스크 | 매우 낮음 |
| **추천 시점** | 패키지 stable v1.0 publish 직전 |

**작업 단계**:
- [ ] `<Nullable>enable</Nullable>` 또는 `#nullable enable` 적용
- [ ] 모든 public API 검토 + ?/! 표시

---

### (P3 후보) VContainer DI 통합

> 정식 P3 항목은 아니지만 SurvivorsDuo 사용 시점에서 가치 큼.

- **현재**: SurvivorsDuo가 VContainer 사용 중인데 SupaRun만 정적 호출 (`SupaRun.Login()`)
- **목표**: `SupaRunRuntime`을 GameLifetimeScope에 등록 → 호출자가 생성자 주입으로 받음

**가치**:
1. **명시적 의존성** — 클래스 시그니처에 SupaRun 의존성이 보임 (코드 리뷰 ↑)
2. **테스트 시 mock 주입** — 정적 SetInstance 보다 깔끔
3. **라이프사이클 자동 관리** — Scope 종료 시 SupaRunRuntime.Dispose() 자동
4. **다중 인스턴스** (드물지만 가능) — dev/staging 동시

**한계**: codegen은 여전히 정적 호출 (`SupaRun.Client.PostAsync<T>`) — 100% DI 전환 불가능. caller 측만 깔끔.

| 항목 | 평가 |
|------|------|
| 작업 양 | 20분 (1줄 등록 + 4곳 호출 변경) |
| 난이도 | 매우 쉬움 |
| 리스크 | 매우 낮음 |
| 단기 가치 | 중간 (코드 리뷰 ↑) |
| 장기 가치 | 높음 (단위 테스트와 결합 시) |
| **추천 시점** | **단위 테스트(P3-2) 시작 전 또는 함께** |

**작업 단계** (SurvivorsDuo 측):
- [ ] `GameLifetimeScope.Configure()` 에 `builder.RegisterInstance(SupaRunRuntime.CreateFromSettings()).AsSelf()`
- [ ] `GameInitializer`, `DataInitializer`, `SupaRunDataProvider`, `CurrencyRealtime` 마이그레이션 (4곳)
- [ ] 컴파일 + MPPM 테스트

---

### (P3 후보) `SupabaseAuthApi` 분리 (P2-1d 스킵분)

- **현재**: `SupaRunAuth.Post()` private 메서드가 inline UnityWebRequest 사용 (P2-1에서 마이그레이션 스킵)
- **목표**: `SupabaseAuthApi` 별도 클래스 분리 → SupaRunAuth가 위임. SupaRunAuth 책임이 줄어듦.
- **가치**: HTTP 코드 중복 100% 제거, SupaRunAuth 책임 분리

| 항목 | 평가 |
|------|------|
| 작업 양 | 1시간 |
| 난이도 | 보통 |
| **추천 시점** | SupaRunAuth 큰 리팩터 시 / Auth API 추가 기능 도입 시 |

---

## 진행 상황

| Phase | 상태 | 시작일 | 완료일 |
|-------|------|--------|--------|
| P0 — 즉시 안정화 | ✅ 완료 | 2026-04-08 | 2026-04-08 |
| P1 — 정리 + 1차 분리 | ✅ 완료 | 2026-04-08 | 2026-04-08 |
| P2 — 큰 리팩터 | ✅ 완료 | 2026-04-08 | 2026-04-09 |
| P3 — 장기 | ⚪ 대기 | - | - |

---

## 참고 자료

- 분석 세션 로그: SurvivorsDuo `.claude/sessions/` (날짜별)
- 사용 프로젝트: `C:/workspace/unity/SurvivorsDuo`
- 패키지 로컬 경로: `C:/workspace/unity/unity-packages/com.tjdtjq5.suparun`
- 패키지 원격: `https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.suparun#suparun/v0.3.3`
