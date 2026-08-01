# Bridge

- **상태**: stable
- **용도**: 어드민 웹을 내보내고, 브라우저가 못 하는 일을 Unity 가 대신 해 준다.

## 왜 있나

어드민은 웹이고 Unity 는 에디터다. 둘 사이에 세 가지 벽이 있다:

1. **CORS** — 브라우저는 `api.supabase.com` 을 직접 못 부른다. 그쪽이 `https://supabase.com`
   오리진만 허용한다
2. **PAT** — Supabase 계정 마스터키다. 브라우저에 내려보낼 수 없고, git 에도 올릴 수 없다
   (EditorPrefs 에만 있다)
3. **로컬** — `gcloud`·`gh`·`dotnet` 은 이 컴퓨터의 명령이고, 생성된 Id 상수는 이 Unity
   프로젝트에 파일로 쓰인다. 웹에서 돌릴 방법이 없다

그래서 PAT 를 쥔 Unity 가 `127.0.0.1` 에 작은 HTTP 서버를 띄우고 대신 부른다.

## 어드민 자체를 여기서 내보낸다

한때 이 자리를 `suparun-admin` Edge Function 이 맡았다. 근거는 "Unity 가 꺼져 있어도 웹만
열어 보는 사람" 이었는데, **어드민을 이 브리지가 서빙하게 되면서 그 전제가 사라졌다.**
Unity 가 없으면 이 페이지도 없다.

> ⚠ **Supabase 에서는 HTML 을 못 내보낸다.** `*.supabase.co` 는 Storage 와 Edge Functions
> 양쪽에서 `text/html` 을 `text/plain` 으로 강제 변환한다(실측 확인). 그래서 어드민을 그쪽에
> 올리는 선택지는 처음부터 없었다.

접속 정보는 페이지를 내보낼 때 `window.__SUPARUN_BRIDGE = {port, token}` 으로 꽂아 준다.
예전처럼 DB(`suparun_meta`)에 실어 보내면 그 표가 `public_read` 라 **anon key 만으로 토큰이 샌다** —
게임 빌드에서 뽑히는 키다. 그 경로(`PublishEndpointAsync`)는 삭제했다.

**세션은 꽂지 않는다** — 사람 로그인(이메일+비밀번호)이 신원이다 (ADR-0009, #23).
한때 기계 계정 세션(`__SUPARUN_SESSION`)을 여기서 만들어 줬는데, 그 논거("로컬 전용이라
로그인이 보안을 더하지 않는다")는 원격 접근자가 생기면 무너지고, 감사의 "누가"도 행위자
식별 없이는 무의미하다. 첫 관리자 등록만 `/auth/claim-admin` 으로 브리지가 돕는다(아래).

## 구조

```
Bridge/
├── SupaRunBridge.cs        # HttpListener + 어드민 정적 서빙 + PAT 대행
├── BridgeDeployRoutes.cs   # /setup/* /deploy/*  — **준비**: 셋업, 로그인, 대상 값, 자동 설정
├── BridgeOpsRoutes.cs      # /ops/*             — **실행**: 스키마 반영, 배포, 승격, 환경
├── BridgeIo.cs             # Err / Write / Fail / ReadBody
└── SupaRunAdmin.cs         # 메뉴 진입점 (Tjdtjq/SupaRun/Admin, Ctrl+Shift+D)
```

준비와 실행을 나눈 기준은 파일 크기가 아니라 **성격**이다. `/ops/*` 는 되돌리기 어려운 일을 한다.

## 보안 경계

| 장치 | 막는 것 |
|---|---|
| `127.0.0.1` 바인딩 | 같은 네트워크의 다른 기기 |
| `x-bridge-token` 헤더 | 이 머신의 다른 페이지가 우연히 부르는 것 |
| `Origin` 검사 (`http://127.0.0.1:{Port}` 만) | 다른 사이트가 브라우저를 시켜 부르는 것 |
| 토큰을 `window` 로만 전달 | DB 를 경유하며 anon key 로 새는 것 |

**토큰 검사보다 앞에 있는 라우트는 `/admin/*` 와 `/setup/pat` 뿐이다.** 앞엣것은 페이지를
받아야 토큰을 알 수 있어서고, 뒤엣것은 아직 PAT 가 없는 사람이 부르는 곳이다.

여기까지 통과했다는 것은 "이 컴퓨터에서 우리 어드민을 열었다" 는 뜻이고, 그 사람은 이미
PAT 대행으로 무엇이든 할 수 있다. 그래서 개별 라우트에서 신원을 다시 묻지 않는다 —
늘어나는 안전이 없는 확인은 두지 않는다.

## 라우트

### `/setup/*` — 첫 셋업 (BridgeDeployRoutes)

**플래그가 아니라 사실로 판정한다.** "첫 실행" 표시를 두지 않는 이유: PAT 는 만료되고,
새 환경은 스키마가 없고, 팀원이 클론하면 토큰이 없다. 플래그를 두면 사실과 어긋나는 순간이 온다.

| 라우트 | 설명 |
|---|---|
| `GET /setup/state` | hasPat·hasProject·schemaReady·initRunning |
| `POST /setup/pat` | 저장 **전에** `ListProjects` 로 검증. 틀리면 401 |
| `POST /setup/project` | `{ref, env?}` — anon key 를 PAT 로 받아 채운다. `env` 를 주면 그 슬롯에 |
| `POST /setup/init` | 스키마 반영. 물어보지 않고 부른다 — 안 하면 아무것도 안 되므로 |

> **`POST /auth/claim-admin`** — 로그인 직후 어드민이 부른다. access token 을 GoTrue 에
> 되물어 신원을 확정하고(`SupaRunAdminClaim`), 그 사람을 `admin_user` 에 등록한 뒤
> **game-admin 롤을 부여한다**(#24 — `admin_user_role` 매핑).
> 표가 비어 있으면 아무도 자기를 등록할 수 없는 RLS 매듭을 PAT 가 끊는 자리 — 여기까지 온
> 사람은 이미 PAT 전권이라 승인을 따로 묻지 않는다(원격 접근자는 이 경로 자체가 없다).
> reset-password·auth-config 라우트는 없다 — 어드민 로그인은 이메일 전용이고, 비밀번호를
> 잊으면 [REGISTER] 로 새 계정을 만들거나 PAT 로 직접 복구한다.

### `/deploy/*` — 배포 준비 (BridgeDeployRoutes)

**어드민은 대상을 정하는 곳.** 상태를 알려주고, 값을 정하게 하고, 자동화를 대신 돌려준다.

| 라우트 | 설명 |
|---|---|
| `GET /deploy/status` | 체크리스트가 필요한 전부 — tools·billing·permission·target·autoSetup·ready |
| `POST /deploy/gcloud-login`·`gh-login` | fire-and-forget. **완료는 폴링이 잡는다** |
| `POST /deploy/refresh` | 어드민이 `suparun_env` 에 직접 쓰므로 Unity 도 다시 읽어야 한다 |
| `GET·POST /deploy/gcp-projects`·`gh-repos` | 목록 / 새로 만들기 |
| `GET /deploy/billing-accounts`, `POST /deploy/billing-link` | 자동 설정 실패의 최다 원인 |
| `POST /deploy/auto-setup` | API 활성화 + 서비스계정 + Secret 을 한 번에 |

`BlockedReason(...)` 은 **순수 함수**다(단위 테스트 대상 — `Tests/EditMode/DeployBlockedReasonTests.cs`).
위에서부터 막힌 **첫 이유 하나만** 돌려주므로 화면이 한 번에 하나만 말하게 된다.

### `/ops/*` — 실행 (BridgeOpsRoutes)

| 라우트 | 설명 |
|---|---|
| `GET /ops/state` | editorEnv·environments(+autoSchemaSync·autoIdConstants)·dotnet·schema·deploy |
| `POST /ops/env-auto-schema`·`env-auto-ids` | 편집 환경의 자동화 토글 둘(팀 공유 — 설정 파일, git) |
| `POST /ops/id-constants` | Id 상수 생성. **동기.** 어드민 자동 트리거 전용 — 토글 꺼져 있으면 skipped |
| `POST /ops/deploy`, `POST /ops/deploy-reset` | 배포 시작(**스키마 선반영 포함** — 실패 시 중단) / 결과 닫기 |
| `POST /ops/env-select`·`env-add`·`env-remove` | 환경 슬롯 (빌드 환경 라우트는 없다 — 빌드 = 편집 환경) |
| `POST /ops/env-rename` | **편집 환경의** 이름 변경 — 슬롯·해시파일·DB(`suparun_env.name`) 동시 갱신 |
| `POST /ops/promote-schema` | 대상에 스키마 반영 |
| `POST /ops/upload-version` | dev 데이터를 대상의 **미게시 버전**으로 (ADR-0010, #30 — 라이브 무영향. 게시는 대상 어드민의 Game Configs) |

> 수동 스키마 반영·요약 라우트는 없다 — 반영 경로는 둘뿐이다: 자동을 켠 환경은 컴파일이,
> 끈 환경은 배포가(선반영) 민다. prod 는 끄는 것이 규약 — 확인창 대신 구조가 막는다.

> ⚠ **여기서 UI 를 부르지 않는다.** `EditorUtility.DisplayDialog` 를 띄우면 브라우저는 눌린 줄
> 알고 기다리는데 모달은 Unity 창 뒤에 숨는다. 확인은 어드민이 받고 여기는 받은 대로 실행한다.

배포 상태(`_deployPhase`)는 여기 정적 필드가 유일한 주인이다. 옛 `DeployTab` 은 탭이 상태를
들고 OnGUI 가 매 프레임 그렸는데, 그 화면이 없어졌다. `ActionsTracker` 는 스스로
`EditorApplication.update` 로 폴링하므로 **`/ops/state` 를 읽을 때 그 결과를 옮겨 온다.**

## 주의사항

- **블로킹 CLI 는 `OffThread` 로 민다.** 요청 처리가 메인 스레드의 `Pump` 에서 돌기 때문에,
  거기서 프로세스를 기다리면 에디터가 얼고 **브리지 전체가 멈춘다**
- ⚠ 그 안에서 **Unity API 를 부르면 예외**다. `PrerequisiteChecker` 의 캐시 시계가
  `EditorApplication.timeSinceStartup` 이던 시절 `/deploy/gcp-projects` 가 500 을 뱉었다 —
  지금은 `DateTime.UtcNow` 다. `ActionsTracker.ElapsedSeconds` 는 메인 스레드에서만 읽는다
- `[InitializeOnLoad]` 의 `delayCall` 이 안 뜨는 경우가 있다(강제 재컴파일 후 실측 2회).
  그래서 `SupaRunAdmin` 이 열기 직전 `EnsureRunning()` 을 부른다
