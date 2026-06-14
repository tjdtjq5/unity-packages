# Changelog

## [0.8.6] - 2026-06-14

### Changed — 속성 스캔을 AttributeRegistry로 통합

`[PrimaryKey]/[NotNull]/[Unique]/[MaxLength]/[Default]/[CreatedAt]/[UpdatedAt]` 분류를 `LocalGameDB`(런타임 검증)와 `ServerCodeGenerator`(마이그레이션 제약)가 각자 필드를 순회하며 `GetCustomAttribute`를 반복하던 것을, 타입당 한 번 스캔해 캐시하는 `AttributeRegistry.Get(type)` 단일 계약으로 통합.

- `AttributeRegistry` + `TypeAttributeInfo` 신규(Runtime/Attributes). `ConcurrentDictionary` 캐시.
- `LocalGameDB`: 7개 스캔 메서드 + `_fieldCache`를 레지스트리로 대체(매 Save 반복 reflection 제거).
- `ServerCodeGenerator`: 마이그레이션 제약/타입/기본값 스캔을 레지스트리 경유로 변경(동작 byte-동일).
- `AttributeRegistryTests`: 레지스트리 분류가 직접 reflection과 동일함을 검증(두 소비자가 의존하는 동등성 보증).
- 소스 제너레이터(DefGenerator)는 별도 컴파일(Roslyn)이라 통합 대상에서 제외.

## [0.8.5] - 2026-06-14

### Fixed — LocalGameDB 직렬화를 시스템 표준 Newtonsoft로 통일

LocalGameDB(개발 모드 in-memory fallback)가 `JsonUtility`를 쓰는 바람에, Realtime/REST/서버(모두 Newtonsoft)와 같은 `[Table]`/`[Config]` 타입의 직렬화 규칙이 달랐다. `JsonUtility`는 property·`Dictionary`·`[JsonProperty]`를 처리하지 못해 같은 타입이 경로에 따라 조용히 손실/불일치될 수 있었다.

- `LocalGameDB`의 7개 `JsonUtility.To/FromJson` 호출을 단일 `Serialize/Deserialize` 헬퍼(Newtonsoft `JsonConvert`)로 통일. in-memory 저장이라 마이그레이션 영향 없음.
- `LocalGameDbTests` 추가 — property/`Dictionary` 보존 라운드트립 검증.

## [0.8.4] - 2026-06-14

### Fixed — Auth 생명주기 리팩터 (temporal coupling / 토큰 단일 home / 자원 누수)

EditMode 테스트 5건 실패의 단일 근본 원인 + 관련 설계 부채 3건을 한 번에 정리.

- **`SingleFlight` dedup 추출**: `EnsureLoggedIn`이 async 작업 launch 후 `_loginUcs` *필드*를 다시 읽어, 동기 `IAuthApi` 어댑터(테스트 mock)에서 `finally`가 필드를 먼저 null로 비워 `NullReferenceException`이 나던 문제 해결. ucs를 로컬에 캡처해 sync/async 모두 안전. 같은 프리미티브를 `TryRefreshToken`에도 적용해 동시 401 시 refresh POST 중복도 제거.
- **토큰 단일 home (`ISessionProvider`)**: `SupaRunClient`/`SupabaseRestClient`가 push로 받던 `Session` 미러 필드를 제거하고, 요청 시 `SupaRunAuth.CurrentSession`에서 pull. Realtime 소켓만 `OnSessionChanged`로 push(소켓은 pull 불가). 토큰 staleness 구조적 제거. 생성 순환은 `AttachServerClient` late-bind로 해소.
- **`SupaRunAuth : IDisposable`**: `OAuthHandler`의 `Application.deepLinkActivated` 구독 + `HttpListener`를 `SupaRunRuntime.Dispose → _auth.Dispose()`로 정리(이전엔 재생성마다 누수).

## [0.8.3] - 2026-05-05

### Fixed — DapperGameDB reflection 캐시 thread-safety + 어드민 인증 fallback

Cloud Run 동시 request 환경에서 `DapperGameDB._fieldCache` (Dictionary)가 race condition으로 corrupted 상태가 되면 `IndexOutOfRangeException`이 영구 발생 → 어드민 페이지 403 (인스턴스 재시작 전까지 복구 불가) 문제 해결.

- **`Dictionary` → `ConcurrentDictionary`**: `_fieldCache`를 thread-safe로 교체. `GetOrAdd` 사용. valueFactory가 동시 호출 시 여러 번 실행될 수 있으나 reflection은 idempotent라 결과 안전. Cloud Run 동시 request race 영구 차단.
- **Admin 미들웨어 fallback (Program.cs.template)**: `db.GetAll<AdminUser>()` 또는 `db.Save()`가 throw해도 admin이 막히지 않게 SQL 직접 호출 fallback. catch 블록에서 `admin_user` 테이블 직접 조회로 `role=admin` 확인 후 통과.
- **Stack trace 강화**: catch 메시지를 `Exception.Message` → `Exception.ToString()`로 변경. 발생 라인 추적 가능.

## [0.8.1] - 2026-05-03

### Added — 자동 컬럼 너비 + Wrap 토글 + 우클릭 메뉴

어드민 테이블 컬럼이 데이터 길이/타입에 맞게 자동 조정되고, 긴 텍스트는 자동 wrap. 헤더 우클릭으로 토글/리셋.

- **자동 너비 계산**: 헤더는 canvas measureText로 정확 측정, 데이터는 sample 50행 char count × char width. 타입별 min/max로 clamp (`bool`/`int`/`float`/`string`/`isJson`/`isEnum`/`fk` — 별도 limits 표).
- **Wrap 자동 감지**: 컬럼명에 description/desc/comment/memo/note/message/reason/detail 포함 OR 데이터 평균 60자+ → 자동 multi-line.
- **헤더 우클릭 컨텍스트 메뉴**: `[x] Wrap Text` 토글, `Reset This Width`, `Reset All Cols`.
- **수동 우선 정책**: 사용자 드래그로 조절한 너비는 localStorage에 저장, 자동 계산보다 우선.
- **localStorage 마이그레이션**: 기존 배열 형식 (`[w1, w2, ...]`) → 신 object 형식 (`{ widths: {0: w0}, wraps: {0: true} }`) 자동 변환. 옛 사용자의 저장된 폭 깨짐 없음.
- **JSON 매트릭스 모달도 같은 로직 적용**: nested 모달도 storageKey 분리하여 부모와 폭 독립.
- **신규 헬퍼**: `autoColWidth`, `measureText`, `shouldAutoWrap`, `applyWrapMode`, `loadColPrefs`, `saveColPrefs`, `fieldTypeKey`, `fieldDataKey`, `showColMenu` (어드민 index.html).
- **`enableColResize` 시그니처 강화**: `(container, storageKey, opts={})` — opts 안 주면 기존 동작 호환.
- **호출자 7곳 갱신**: Config 평면 / JSON 모달 / Admins / Audit log / Table view / Cross search / Player cards 모두 fields/data 전달.

### Fixed — ActionsTracker가 이전 commit run을 잘못 잡는 버그

git push 직후 GitHub Actions에 새 run이 등록되기 전(5~15초 지연) ActionsTracker가 폴링하면 **이전 commit의 success run**을 보고 즉시 완료 판정 → 사용자에게 "잠시 추적 중" → 즉시 "성공" 표시되는 문제.

- **head_sha 필터링**: `gh run list --limit 1` (latest only) → `gh api repos/{repo}/actions/runs?head_sha={sha}&per_page=1` (정확한 commit). 이전 commit run 잘못 잡는 버그 해결.
- **새 run 등록 대기**: head_sha에 매칭되는 run이 등록될 때까지 5초 × 12회 = 60초 대기. 등록 실패 시 (Actions 미설정 repo) fallback success.
- **폴링 주기 단축**: 15초 → 5초 (반응성 ↑).
- **Workflow 미설정 fallback**: workflow가 60초 동안 안 잡히면 push 성공으로 간주 + Cloud Run URL 조회 시도.
- **`GitHubPusher.LastPushedSha` 신규 public property**: push 직후 `git rev-parse HEAD` 캡처. 두 push 경로 (일반 + redeploy 빈커밋) 모두 지원.
- **`ActionsTracker.StartTracking(repo, headSha)` 신규 overload**: 기존 `StartTracking(repo)`도 그대로 동작 (시그니처 호환).

## [0.8.0] - 2026-05-03

### Changed (Visual) — Brutalist Terminal redesign

어드민 web app 전체를 90s computer terminal 톤으로 통째 재디자인. CRT scanlines + green phosphor (#00ff66) 액센트 + monospace 타이포 + ASCII tree 사이드바.

- **폰트**: JetBrains Mono + Pretendard fallback (영문 mono / 한글 자동 sans)
- **컬러 시스템**: 절대 검정 베이스 + green phosphor primary + amber/red 보조. 기존 Bootstrap/Tabler 변수 통째 override.
- **CRT atmosphere**: 5% scanline (`repeating-linear-gradient`) + green ambient glow (radial) + corner vignette (body::after).
- **상단 titlebar 신규**: `● SUPARUN.ADMIN :: PERK_CONFIG.SH ... v0.x.x / user@host`. 1.6s blink dot.
- **사이드바 ASCII tree**: 기존 Bootstrap dropdown navigation을 `├─`/`└─` 트리 구조로 전면 재작성 (`renderSidebar()` 수정). 마지막 항목만 `└─`. section header `[CONFIGS]`/`[SYSTEM]`/`[TABLES]`. active 시 `▶ ` prefix + green tint.
- **사이드바 status footer**: `conn ● live / user / env / ver` 신규.
- **페이지 prompt 신규**: `admin@suparun:~/configs/perk_config$ inspect --list-all_` (cursor blink). `selectType()` 호출 시 동적 갱신 (`setTerminalContext()` 신규 함수).
- **로그인 페이지 풀 변환**: `terminal-window` 박스 형태. `> email:` `> password:` prefix + `[ENTER]` `[REGISTER]` 버튼. 기존 ID(`#login-email`, `#login-password`, `#login-error`, `#oauth-section`, `#oauth-buttons`) 모두 보존.
- **모든 컴포넌트 변환**: `border-radius: 0` 강제. button (1px green outline + hover inverse), form-control (1px line + green focus), table (amber uppercase header, green PK), modal (green border + ASCII style), badge (mono + green-soft), toast (좌측 3px line + bg-2), pagination, dropdown, alert 모두 brutalist 톤.
- **JSON 매트릭스 모달 디테일**: `#json-editor-back` 버튼이 `[< BACK]` 스타일 (`::before/::after` 브래킷). breadcrumb green mono. nested 모달도 동일.
- **Chart.js global defaults**: 색상 indigo → green, font-family mono. `renderDistChart` 색상 변경.

### Notes

- **JS 동작 보존**: `jsonEditorStack`, `openJsonEditor`, `openNestedJsonEditor`, `jsonEditorBack`, `renderJsonEditor`, `renderJsonEditorRows`, FK dropdown 분기, PREVIEW mode IIFE (`__SUPARUN_PREVIEW__`/`__previewApi`/`__previewTableApi`/`__previewAdminApi`), `showAdmin`/`renderTable`/`api`/`tableApi`/`adminApi`/`showAdmins`/`showAuditLog`/`showCrossSearch`/`showPlayerSearch` 모두 시그니처/동작 그대로.
- **Bootstrap 5.3 + Tabler 1.2 클래스 보존** — 모든 셀렉터는 CSS override로 변환.
- **PREVIEW mode IIFE 본체 무수정** — 사이드바 ASCII tree에 mock 데이터 정상 표시.

## [0.7.0] - 2026-05-03

### Added — Admin nested JSON 모달

JSON 모달 안에서 또 다른 JSON 컬럼을 매트릭스로 편집 가능하도록 하위 모달 지원 추가. PerkConfig.tiers 같은 다단계 JSON 구조 어드민에서 직관적 편집.

- `jsonEditorStack` — 기존 4개 전역 변수(`jsonEditorRowId`/`jsonEditorFieldName`/`jsonEditorSchema`/`jsonEditorOrigItems`) 통합. 모달 element 1개 재사용 + 빵부스러기(breadcrumb)로 layer 위치 표시.
- `openNestedJsonEditor` / `jsonEditorBack` / `renderJsonEditor` / `jsonEditorCancel` 신설.
- `mapJsonSchema` / `countJsonItems` 헬퍼 신설.
- `renderJsonEditorRows`에 `isJson` 분기 — cyan 배지(`N개 항목 ✏️`)로 카운트 표시. 클릭 시 자식 모달 진입.
- 모달 헤더에 `< 뒤로` 버튼 + `#json-editor-breadcrumb`.
- `BuildJsonSchemaJson`은 무수정 — nested isJson + jsonSchema 메타 자동 재귀 생성이 이미 OK.

### Added — JSON 모달 안 ForeignKey dropdown

JSON 모달 안 컬럼이 `[ForeignKey]` 가지면 자동 dropdown. 평면 셀(`renderCell`)과 동일한 UX.

- `loadRewardSources`에 `collectFk(fields)` 재귀 함수 — 평면 + nested jsonSchema 모두 순회해 fkTargets 수집. 미순회 시 nested FK dropdown 옵션 비어있던 문제 해결.
- `mapJsonSchema`에 `foreignKey` 필드 보존 추가.
- `renderJsonEditorRows`에 `s.foreignKey && fkSources[s.foreignKey]` 분기 추가 — 평면 셀의 dropdown 패턴 복제 + 미지원 값 보존 옵션.

### Changed

- `AspNetTemplate~/SupaRun.csproj.template`에 `Newtonsoft.Json` v13.* PackageReference 추가. server build에서 `[JsonObject(MemberSerialization.Fields)]` 같은 Newtonsoft attribute 사용 가능 — `[Json]` attribute가 field-only인 제약과 Newtonsoft 기본 ContractResolver의 properties-only 처리 사이의 충돌 해결.
- `#json-editor-rows th` CSS에서 `text-transform:uppercase` 제거. 컬럼명이 schema.key 그대로(예: `field_orb_id`) 보여 평면 테이블과 시각 일관성 ↑.

### Notes

- `BuildMemberJson` 무수정 — nested 멤버의 `[ForeignKey]` attribute가 메타에 자동 포함되는 동작 그대로.
- 사용 예: 프로젝트의 PerkConfig.tiers JSON에 nested PerkStatBonusDef 매트릭스 + `field_orb_id`/`skill_id` FK dropdown 동작 확인.

## [0.6.0] - 2026-05-02

### Changed (Breaking) — UniTask 마이그레이션

Runtime + Editor 전반을 `Task` → `UniTask`로 전환. 외부 시그니처 변경이 다수 발생하므로 minor 범프(0.x).

- **인터페이스 6개 시그니처 변경** — `IAuthApi`, `IPlatformAuth`, `IAuthRefresher`, `IHttpTransport`, `IServerClient`, `IGameDB`. 모두 `Task` → `UniTask` 반환 + `CancellationToken ct = default` 인자 추가. 구현체(`SupabaseAuthApi`, `GPGSAuthHandler`, `GameCenterAuthHandler`, `UnityHttpTransport`, `SupaRunClient`, `LocalGameDB`)도 동시 변경.
- **callback delegate** — `CallbackAuthRefresher`의 `Func<Task<AuthSession?>>` → `Func<UniTask<AuthSession?>>`. `SupaRunClient.OnTokenRefresh` 프로퍼티 시그니처도 동일.
- **Auth/Client/Realtime 핵심** — `SupaRunAuth`(13 메서드), `OAuthHandler`, `SupaRun` 정적 facade(4), `SupaRunRuntime`(4), `SupabaseRestClient`, `RealtimeChannel`(7), `SupabaseRealtime`(6) 시그니처 일괄 UniTask로 전환.
- **`async void` 17건 → 0건** — Runtime `RealtimeChannel.PushAccessToken`(앱 크래시 위험 1건) + Editor 16건 모두 `async UniTaskVoid` + try/catch로 안전화.
- **`UniTaskCompletionSource` 캐싱** — `SupaRunAuth.EnsureLoggedIn`을 `Task? _loginTask` → `UniTaskCompletionSource? _loginUcs`로 전환 (다중 호출자 dedup).
- **Realtime 안정화** — `ReceiveLoop`/`HeartbeatLoop`/`TryReconnect`/`Rejoin` fire-and-forget 4건이 `_ = Method()` → `Method().Forget(예외 핸들러)`로 변경. silent 죽음 방지.
- **`Task.Run` → `UniTask.RunOnThreadPool`** — `OAuthHandler` HttpListener 백그라운드 호출 2곳.

### `[Service]`/`[API]` 정책 — 서버 호환 유지

`[Service]` 클래스의 `[API]` 메서드는 **`Task`로 유지**한다. 이유: `ServerCodeGenerator`가 `[Service]` 타입을 reflection으로 읽어 ASP.NET 컨트롤러를 생성할 때, 서버 .NET 환경에는 UniTask가 없으므로 클라 [Service] 인스턴스가 서버에서도 컴파일 가능해야 한다.

대신 **Source Generator 출력**(`ServerAPI.{Service}.{Method}` 프록시)은 항상 `UniTask` 반환:
- 입력: `[API]` 메서드의 `Task` 또는 `UniTask` 모두 인식 (`ServiceGenerator`/`TableQueryGenerator` 패치)
- 출력: `public static async UniTask<ServerResponse<T>>` 시그니처
- → 클라 호출자는 `await ServerAPI.X.Y()` 패턴 그대로, UniTask로 일관 사용

### Build
- SourceGen `Tjdtjq5.SupaRun.SourceGen.dll` 재빌드 + `Runtime/` 복사. UniTask 인식 로직 추가.

### Migration Guide
호출 측 코드는 대부분 `await`만 사용하므로 변경 불요 (Task ↔ UniTask awaiter 호환). 단:
- 외부에서 `Task` 타입을 명시적으로 변수 선언/필드로 보유한 코드는 `UniTask`로 변경 또는 `.AsTask()` 사용
- `IGameDB`/`IDataProvider`를 직접 구현하는 외부 코드는 `UniTask` 시그니처로 갱신
- Tests asmdef에 `"UniTask"` reference 추가 필요 (`Tjdtjq5.SupaRun.Tests.EditMode.asmdef`에 이미 추가됨)

## [0.5.4] - 2026-04-26

### Changed (Behavior)
- **`DeployRegistry` 저장소를 `PlayerPrefs` → `ProjectSettings/SupaRunDeployedEndpoints.json`으로 이동** (멀티 개발 환경 대응)
  - 기존 `PlayerPrefs`는 PC별 저장소라 한 PC에서 Deploy 실행해도 다른 PC는 인식하지 못해 LocalDB로 폴백되는 문제. 결과: 두 클라가 서로 다른 메모리 인스턴스의 데이터를 보고 매칭/JoinByCode 실패.
  - v0.5.4부터: `ProjectSettings/SupaRunDeployedEndpoints.json` 파일(JSON 배열)에 저장. git commit으로 공유되어 모든 PC가 자동 동기화.
  - **자동 마이그레이션** — 첫 로드 시 `PlayerPrefs.SupaRun_DeployedEndpoints`에서 데이터를 읽어 새 파일에 저장하고 PlayerPrefs 키를 삭제. 사용자 액션 불필요.
  - `Save()`는 endpoint 배열을 ordinal 정렬한 prettyPrint JSON으로 기록 — git diff 안정성.
  - 빌드 환경에서는 ServiceGenerator의 `#if UNITY_EDITOR` 분기로 `IsDeployed` 호출 자체가 제거되므로 영향 없음 (빌드는 항상 서버 호출).

### Migration Note
- 새 PC 합류 시 별도 작업 불필요 — `git pull` 후 Unity 실행하면 `ProjectSettings/SupaRunDeployedEndpoints.json`을 그대로 인식.
- 처음 v0.5.4 적용한 PC가 main PC에서 Deploy 실행 후, 생성된 `ProjectSettings/SupaRunDeployedEndpoints.json`을 commit/push해야 다른 PC에 전파.

## [0.5.3] - 2026-04-26

### Fixed
- **`DeployRegistry` PascalCase ↔ snake_case 키 불일치** (Critical, v0.5.2 hotfix) — v0.5.2에서 `ServiceGenerator`만 snake_case로 변환하고 `DeployManager.MarkDeployed`는 여전히 PascalCase로 등록하여, Editor에서 `IsDeployed`가 false로 평가됨. 결과: Editor가 LocalDB로 폴백되어 두 클라가 서로 다른 메모리 인스턴스의 데이터를 보고 매칭/조회 실패.
  - `DeployManager.cs:77`: `endpoints.Add($"{type.Name}/...")` → `endpoints.Add($"{ToSnakeCase(type.Name)}/...")` 로 수정.
  - `DeployRegistry.EnsureLoaded`에 **자동 마이그레이션** 추가 — `ServiceName/Method` 형태로 저장된 기존 PlayerPrefs 항목을 첫 로드 시 `service_name/Method`로 변환하고 PlayerPrefs 재저장. 이미 snake_case거나 슬래시 없는 키는 그대로 유지. 사용자 액션 불필요 — v0.5.3을 받자마자 자동 동작.

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
