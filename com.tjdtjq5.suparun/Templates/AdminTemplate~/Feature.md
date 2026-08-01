# AdminTemplate

> **상태**: React 전환 완료 (ADR-0003 — 로그인부터 전 화면까지 React. 실기 확인 대기)
> **용도**: Supabase Auth 기반 로그인과 Config/Table CRUD를 제공하는 어드민 웹 페이지 SPA 템플릿

## 의존성

- **로컬 브리지 전용** — `SupaRunBridge.ServeAdmin` 이 유일한 서빙 경로다. 배포(서버 `/admin`)는
  없다: 공개 URL 에선 로그인이 유일한 문인데 그 로그인을 기계 계정 자동화로 없앴기 때문.
  접근 통제는 Supabase 조직 멤버십(각자 PAT)이 맡는다
- 외부 CDN: Tabler CSS/JS, Bootstrap 5, Supabase JS v2, Chart.js, Sortable
- 빌드: vite 8 + React 19 + TypeScript 7 (`@xyflow/react` 는 노드 그래프용, ADR-0002)

## 구조

```
AdminTemplate~/
├── package.json        vite / react@19 / @xyflow/react@12 / typescript@7
├── vite.config.ts      root=src, 산출물 고정 파일명, /admin/api 프록시
├── tsconfig.json       strict, noEmit (타입검사는 `npm run typecheck`)
├── .env.local          (gitignore) 로컬 dotnet 서버 주소 VITE_SERVER_URL
│
├── src/                ← 소스
│   ├── index.html      1,616줄(최초 3,950). CSS 1,409줄 + 인라인 JS 171줄(플레이스홀더·프리뷰 mock)
│   ├── main.tsx        React 진입점 — #root 에 <App/> 마운트
│   ├── App.tsx         로그인 화면 ↔ 어드민 껍데기 분기
│   ├── shared/         api / supabase / toast / types / Modal / Spinner / snapshot / policy
│   │                   colResize / castValue / env / chart / db / meta
│   └── features/       화면 단위 폴더
│       ├── shell/      껍데기 — 레이아웃·사이드바·툴바·라우팅·키맵·AdminContext
│       │               EnvSwitcher(타이틀바 환경 드롭다운) / TitlebarClock(시계)
│       ├── nodegraph/  [NodeGraph] 컬럼이 여는 노드 캔버스 (ADR-0002)
│       │               NodeGraphModal / GraphNode / graphIO / validate / nodegraph.css
│       ├── snapshot/   [SpecData] 시점 저장·복원 — SnapshotPage / RestoreModal / useSnapshots
│       ├── environment/ 환경 현황 카드 · 이 환경 설정(환경 안)
│       │               EnvironmentPage / EnvSettingsPage / AuthProvidersBlock
│       ├── setup/      첫 셋업(온보딩)과 미연결 프로젝트 셋업
│       │               OnboardingPage / SetupProjectPage
│       ├── deploy/     배포 대상 체크리스트 — DeployBlock
│       ├── ops/        **Unity 를 시키는 화면** — 스키마 반영·Id 상수·배포·승격. OpsPage
│       ├── logs/       서버 로그(`server_log`) — LogsPage
│       ├── secrets/    이 환경의 비밀 — SecretsPage (값은 절대 표시하지 않는다)
│       └── …           audit / table / config  (auth·admins 폴더는 없다 — 아래 인증 참조)
│
├── node_modules/       (gitignore)
└── dist/               ← 빌드 산출물. **커밋한다** (소비 프로젝트는 Node 불필요)
    ├── index.html
    └── assets/index.js, index.css
```

| 명령 | 용도 |
|------|------|
| `npm ci` | 의존성 설치 (최초 1회) |
| `npm run dev` | vite 개발 서버. `/admin/api` 는 `VITE_SERVER_URL` 로 프록시 |
| `npm run build` | `dist/` 생성 — **DeployManager 가 싣는 대상** |
| `npm run typecheck` | `tsc --noEmit`. vite 빌드는 타입 검사를 하지 않으므로 별도로 돈다 |

> ⚠ `dist/` 를 빌드하지 않으면 `DeployManager` 가 경고를 내고 **어드민 페이지 없이 배포**한다.
> 패키지를 로컬 개발 모드(`/ft:pkg-dev`)로 쓰는 중이라면 `npm run build` 를 한 번 돌려둘 것.

### 전환 진행 상황 (ADR-0003)

| 단계 | 화면 | 상태 |
|---|---|---|
| 0 | 빌드 환경 + DeployManager | **완료** |
| 1 | `features/admins/` 관리자 관리 | **완료** (실기 확인 완료) |
| 2 | `features/audit/` 변경 이력 | **완료** (실기 확인 완료) |
| 3 | `features/table/` + `cross/` + `player/` | **완료** (실기 확인 완료) |
| 4 | `features/config/` Config CRUD | **완료** (실기 확인 완료) |
| 5a | 죽은 코드 제거 + 키맵 모달 가드 수정 | **완료** |
| 5b | 컬럼 유틸 → `shared/colResize.ts` | **완료** |
| 5c | `features/shell/` 레이아웃·사이드바·툴바·라우팅 | **완료** (실기 확인 대기) |
| 5d | 로그인 · Supabase 세션 | **완료** (실기 확인 대기) |

**index.html 에 남은 것**: CSS 전량, CDN 스크립트 4종, `window.__SUPARUN_ENV` 노출,
부팅 실패 표시(`#boot-error`), `#toast-container`, `#root`, 프리뷰 mock 블록. **바닐라 로직은 없다.**

> ⚠ **플레이스홀더(`{{...}}`)는 인라인 `<script>` 에만 둘 수 있다.** vite 는 `type="module"` 이 아닌
> 인라인 스크립트를 번들하지 않으므로 그 자리에서만 치환이 통과한다. React 번들 안에 쓰면
> 문자열이 그대로 남아 배포가 조용히 깨진다. 그래서 `window.__SUPARUN_ENV` 로 한 번 내보내고
> `shared/env.ts` 가 읽는다.

**라우팅**: react-router 를 쓰지 않는다. 화면 6개에 해시 하나뿐이라 `features/shell/route.ts`
30줄로 충분하다. 바닐라와 동일하게 `replaceState` — 뒤로가기로 화면을 되짚지 않는다.

**인증 토큰**: 전역 변수로 들고 있지 않고 API 호출마다 `sb.auth.getSession()` 에서 읽는다.
supabase-js 가 갱신을 알아서 하므로 옮겨 적을 필요가 없다(바닐라는 `onAuthStateChange` 에서 수동 갱신했다).
401/403 은 `shared/api.ts` 의 `onUnauthorized` 구독으로 `App` 에 전달되어 로그인 화면으로 돌린다.

> ⚠ **`position: fixed` 인 오버레이는 반드시 `document.body` 로 portal 한다.**
> 바닐라가 `document.body.appendChild()` 로 붙이던 것을 React 이관 때 셀 안에 그대로 렌더하면
> 표 레이아웃에 갇혀 엉뚱한 곳에 뜬다. 해당 요소는 `.icon-grid-overlay`(모달)·`.ss-pop`(검색 드롭다운)·
> `.tb-env-menu`(타이틀바 환경 드롭다운 — 타이틀바 z 1000 안에 그리면 사이드바 navbar z 1030 이 덮는다).
> 모달은 `shared/Modal.tsx`, 드롭다운은 `SearchSelect`/`EnvSwitcher` 가 각각 portal + 좌표 계산을 담당한다.
> z-index 순서: `.tb-env-menu`(1200) > `.ss-pop`(1100) > `.icon-grid-overlay`(1090) — 모달 안에서도 드롭다운이 보여야 한다.

## 플레이스홀더 변수

브리지가 서빙할 때 실제 값으로 치환합니다(`SupaRunBridge.InjectEnvAsync` — 런타임 주입이라
환경을 바꿔도 재빌드가 없다). 같은 자리에서 `__SUPARUN_BRIDGE`(포트·토큰)와
`__SUPARUN_SESSION`(기계 계정 세션)도 함께 꽂힌다.

| 플레이스홀더 | 타입 | 설명 |
|-------------|------|------|
| `{{SUPABASE_URL}}` | string | Supabase 프로젝트 URL (`https://xxx.supabase.co`) |
| `{{SUPABASE_ANON_KEY}}` | string | Supabase Anonymous Key |

## 주요 기능

### 인증 — **사람 로그인이 없다** (기계 계정 자동 로그인)

- 이 페이지는 로컬 브리지 전용이고, 연 사람은 이미 브리지 토큰(=PAT 대행 전권)을 쥐고 있다.
  로그인 화면은 전권자에게 또 세운 문이었다 — **접근 통제는 Supabase 조직 멤버십(각자 PAT)**
- RLS 가 요구하는 세션은 브리지가 **기계 계정**(`{OS계정}.{머신명}@suparun.local`)으로 만들어
  `window.__SUPARUN_SESSION` 으로 꽂아 준다 — Unity 쪽 `SupaRunMachineAccount` 참조.
  App 이 `setSession` 으로 싣고, 이후 갱신은 supabase-js 가 refresh_token 으로 알아서 한다
- 첫 관리자 매듭(`admin_user` 가 비면 아무도 못 씀)도 브리지가 PAT 로 스스로 푼다 —
  버튼도 가입도 없다. 감사로그(updated_by)에는 기계 계정 이메일이 남아 사람이 읽힌다
- 401/403 이나 세션 주입 실패는 안내 화면 하나로 수렴한다(원인은 Unity Console)

> 로그인 방식은 네 번 바뀌었다. OAuth 만(프로바이더 앱 등록 ~10분) → 매직링크(기본 메일
> **시간당 2통**) → 이메일+비밀번호(메일 미사용) → **기계 계정 자동 로그인**(사람 로그인 소멸,
> 2026-08-01). jwt_secret 직접 서명안은 기각 — Supabase 신규 프로젝트가 비대칭 서명키로
> 넘어가면 서명 비밀 자체를 못 얻는다. 정식 발급 창구(Auth)에 줄서는 쪽이 체계와 무관하다.

### 서버 로그 (`[SYSTEM] > server_log`)

레벨 필터(전체/error/warn) · 50개 페이징 · 행을 눌러 스택트레이스와 request body 를 편다.

> ⚠ 이 화면은 옛 Unity 대시보드의 Monitor 탭이었고, 그쪽은 **anon key** 로 읽었다.
> 그래서 `server_log` 는 RLS 를 켠 적이 없었다 — anon key 는 게임 빌드에서 뽑히므로
> request_body·player_id·스택트레이스가 사실상 공개였다. 어드민은 관리자 세션으로 읽으므로
> 그 표를 잠글 수 있고, `admin_read` 정책이 함께 들어갔다. 서버는 직접 Postgres 연결(표 소유자)로
> 쓰므로 RLS 를 타지 않는다.

### 운영 (`[SYSTEM] > ops`)

**Unity 를 시키는 화면.** 옛 대시보드 Deploy 탭이 통째로 여기로 왔다.

| 블록 | 내용 |
|---|---|
| 배포 | **스키마 선반영** → idle → verifying → deploying → tracking → success/failed/skipped |
| 승격 | 대상 환경 선택 → ① 스키마 반영 ② 데이터 승격 |

스키마·Id 상수 블록은 없다 — 스키마는 자동을 켠 환경(설정 토글)은 컴파일이, 끈 환경은
배포가(선반영, 실패 시 중단) 밀고, **Id 상수는 행 추가/삭제/복사·스냅샷 복원 때 어드민이
자동 트리거한다**(`shared/idsync.ts` — 디바운스 2.5초, 정책 판정은 브리지, 생성기에 내용 비교
가드가 있어 PK 집합이 안 바뀌면 재컴파일 없음).

- **확인은 여기서 받는다.** 브리지는 받은 대로 실행한다 — Unity 쪽에서 모달을 띄우면
  브라우저는 눌린 줄 알고 기다리는데 그 모달은 Unity 창 뒤에 숨는다
- **되돌리기 비싼 것만** 확인을 받는다(승격). 스키마 반영·Id 생성은 그냥 실행한다
- 진행 중일 때만 3초 폴링한다. 가만히 있는 화면을 두드리지 않는다

### Config CRUD
- **사이드바**: Config 타입별 그룹/비그룹 네비게이션, `/_types` API로 자동 생성
- **인라인 편집**: 셀 클릭 → input 변환 → blur 시 debounce 500ms 자동 저장
- **행 추가/복사/삭제**: PK 입력 프롬프트, 삭제 확인 모달
- **bool 토글**: 체크박스 스위치로 즉시 저장
- **FK 드롭다운**: `foreignKey` 필드는 참조 대상 Config에서 옵션 자동 로드
- **Ctrl+Z 되돌리기**: undoStack으로 필드 단위 되돌리기

### 조건부 필드 표시
- **`[VisibleIf]`**: 조건 필드 값 일치 시에만 셀 활성화 (enum 단일/복수, bool 지원)
- **`[HiddenIf]`**: 조건 필드 값 일치 시 셀 비활성화 (VisibleIf 역조건)
- **비활성화 UI**: 회색 배경 + "—" 텍스트 + 편집 불가 (`.cell-na` 클래스)
- **실시간 갱신**: enum/FK/bool 변경 시 해당 row의 조건부 셀 즉시 재평가 (`refreshRowConditions`)

### 노드 그래프 편집 (ADR-0002)
- **진입**: `[NodeGraph(typeof(TCtx))]` 컬럼 셀의 배지 → 전체화면 캔버스(`createPortal` → body)
- **팔레트**: `suparun_meta.node_catalog` 에서 읽는다. 컨텍스트가 그래프 종류를 갈라서 섞이지 않는다
- **노드 입력칸**: 카탈로그의 `fields` 가 표 컬럼과 **같은 메타**라 `[EnumType]` 드롭다운 등이 그대로 동작한다
- **두 종류의 연결**: 실행선(파랑 실선, `[NodeOut]` 포트) / 값선(청록 점선, `NodeValue` 칸에 Pure 노드)
- **연결 제약**: 값선은 타입이 맞는 칸에만, 실행선은 Pure 로 못 간다. 포트당 연결 1개(새로 꽂으면 기존이 빠짐)
- **저장 전 검증**: 진입점 1개 / 포트 중복 / 실행·값 순환 / 도달 불가. 오류가 있으면 저장 버튼이 잠긴다
- **인덱스 재매김**: 노드를 지워 생긴 구멍은 **저장 시점에** 0부터 다시 매긴다. 연결도 함께 옮겨진다
- **좌표**: `layout` 키로 따로 실린다 — 노드를 옮긴 것만으로 게임이 달라지면 안 되기 때문

### JSON 편집
- **Rewards 모달**: `rewards` / `*_rewards` 필드 전용 — 재화/아이템 타입 + ID 드롭다운 + 수량
- **범용 JSON 배열 에디터**: 기타 JSON 필드 — 첫 항목 기반 스키마 자동 감지, 행 추가/삭제
- **JSON 필드 메타데이터**: `[Json(typeof(T))]`의 T 클래스에서 enum/VisibleIf 메타데이터를 서버가 제공
- **JSON 모달 enum 드롭다운**: 메타데이터가 있는 필드는 텍스트 input 대신 enum 드롭다운 렌더링
- **JSON 모달 조건부 표시**: JSON 행 내부에서도 VisibleIf/HiddenIf 동작
- **Nested JSON 편집**: `[Json(typeof(T))]` DTO 안에 또 `[Json(...)]` 필드가 있을 때 단일 모달 + Stack + Breadcrumb로 자식 layer 진입. 자식 자식(3단+) 무제한 자연 지원. `jsonEditorStack` 전역 + `openNestedJsonEditor`/`jsonEditorBack`/`jsonEditorCancel`

### 관리자 관리
- **관리자 목록**: 이메일, 상태(admin/pending), 등록일 표시
- **승인/해제**: role 변경 API 호출
- **삭제**: 관리자 제거

### 변경 이력 (Audit Log)
- 최근 100건 조회, 작업별 뱃지 (create/update/delete/batch/import 등)
- before/after JSON 상세 보기 (새 창)

### Table (읽기 전용 + 분석)
- **테이블 조회**: 필터(=, >, >=, <, <=, like) + 페이지네이션 (50건 단위)
- **통계**: 숫자 필드의 합계/평균/최대/최소/건수
- **분포 차트**: Chart.js 바 차트 (10버킷)
- **크로스 테이블 검색**: 여러 테이블에 조건 걸어 교집합 user_id 검색
- **플레이어 관리**: user_id로 해당 유저의 전 테이블 데이터 조회 + 인라인 편집

### 환경 현황 (`[SYSTEM] > environments`)

dev / prod 각각을 카드 하나로. 왼쪽은 신원(무엇인가), 오른쪽은 지표(지금 어떤가).

> **읽기 전용이다.** 값은 전부 Management API + **PAT** 로만 얻을 수 있는데 PAT 는 로컬 에디터에만
> 두기로 했다(어드민 계정이 털려도 Supabase 계정 전체는 안 넘어가게). 그래서 **아이콘 맵과 같은 방식**이다 —
> Unity 가 어드민을 열 때 `EnvironmentSnapshot.CollectAndPublishAsync()` 로 구워 `suparun_meta.environments`
> 에 넣고, 어드민은 읽기만 한다.
>
> 따라서 이 화면은 **마지막으로 Unity 가 본 상태**다. 카드마다 `Unity 가 수집: N분 전` 을 띄우는 이유가
> 그것이다 — 실시간이 아닌 화면에서 그 사실을 숨기면 낡은 숫자를 믿게 된다.

| 지표 | 출처 | 비고 |
|---|---|---|
| CPU | `analytics/endpoints/metrics` 의 `node_load1 / node_cpu_online` | **근사값**. 스냅샷 하나로는 정확한 CPU% 를 못 구한다(`cpu_seconds_total` 은 delta 필요) |
| 메모리 | `node_memory_MemTotal/MemAvailable_bytes` | |
| 스토리지 | `config/disk/util` | JSON 이라 파싱이 단순 |
| 커넥션 | `connection_stats_connection_count` + `SHOW max_connections` | **라벨별로 여러 줄이라 합산**해야 실제 접속 수가 된다 |
| 서비스 헬스 | `health?services=db,rest,auth` | |

- 메트릭은 **Prometheus 텍스트**다(JSON 아님). 정규식 한 줄짜리 파서로 이름이 같은 줄의 값만 뽑는다 —
  전용 라이브러리를 끌어올 만한 일이 아니다
- `node_cpu_online` / `connection_stats_connection_count` 는 **`Sum`**, 나머지는 `First`
- 레플리카는 뺐다 — Supabase read replica 는 **유료 애드온**이라 무료 플랜엔 없다.
  그 자리에 커넥션 사용량을 넣었고, 이쪽이 실제로 겪을 수 있는 문제다
- 추이 그래프도 뺐다. 메트릭 API 가 주는 것은 **현재값 스냅샷** 하나뿐이라 시계열을 그리려면
  누군가 주기적으로 쌓아야 하는데, 브라우저는 탭이 닫히면 멈춘다
- 카드에 `에디터` 배지를 띄운다 — 어느 환경이 컴파일·빌드 대상인지가 사고를 막는 정보다
  (`빌드` 배지는 없다 — 빌드 = 편집 환경)
- **카드 클릭 = 입장.** 로컬(브리지)에서는 연결된 환경이면 **전환 입장**(편집 환경 전환 + 리로드 —
  컴파일 대상도 같이 바뀐다. prod 여도 묻지 않는다: 카드 클릭이 곧 의도), 미연결 프로젝트면
  **셋업 화면**(`setup/<ref>`)으로 들어간다. 배포 어드민(브리지 없음)은 예전대로 —
  배포된 환경만 새 탭, 미연결은 입장 불가

### 설정 (`[SYSTEM] > settings`) — 환경 안

한때 앱 레벨(환경을 고르기 전)에 있었으나 내용물이 전부 특정 프로젝트의 값이라 환경 안으로 옮겼다.
"환경 슬롯" 리스트는 **해체됐다** — ①연결·이름 짓기는 미연결 카드 입장 후 `SetupProjectPage` 에서,
②편집 환경 전환은 카드 클릭(전환 입장)으로, ③빌드 지정·연결 해제는 이 화면에서 한다.

| 블록 | 내용 |
|---|---|
| 이 환경 | 이름(브리지 op `/ops/env-rename` — 슬롯·해시파일·DB 동시 갱신) · **컴파일 후 자동 스키마 반영 토글** · **행 편집 시 Id 상수 자동 생성 토글**(둘 다 환경별 팀 공유값 — Unity 설정 파일, git diff 발생. dev만 켜는 것이 의도) |
| 배포 | DeployBlock 체크리스트 (변화 없음) |
| 게임 로그인 | 플레이어 로그인 수단만 (어드민 웹 프로바이더 블록은 인증 개편으로 소멸) |
| 위험 영역 | **이 프로젝트만** — 연결 해제(슬롯만 삭제) · 프로젝트 삭제. 다른 프로젝트는 거기 들어가서 지운다 |

> **이름의 진실은 슬롯(Unity 설정)이다.** DB(`suparun_env.name`)는 사본 — 셋업/이름 변경 때
> 브리지가 박아 준다. DB 만 고치면 카드(DB 이름)와 슬롯(Unity 이름)이 서로 다른 이름을 말한다.

### 미연결 프로젝트 셋업 (`setup/<ref>`) — 로컬 전용

미연결 카드로 들어와 이름을 정하면 멈추지 않고 끝까지 간다:
슬롯 생성 → 연결(키 수신) → **편집 환경 전환(자동 — prod 이름이어도 묻지 않는다)** → 스키마 반영
시작 → 리로드. 리로드 뒤는 온보딩이 이어받아 반영 진행과 첫 관리자 등록을 처리한다.
빈 슬롯(이름만 미리 만들기) 흐름은 사라졌다 — 이름 짓기가 여기로 왔기 때문이다.

### 스냅샷 / 복원 (`[SYSTEM] > snapshots`)

`[SpecData]` 전 테이블을 한 시점으로 찍고 되돌린다. 데이터는 **Postgres 스키마 안에서만** 움직인다
(`CREATE TABLE snap_x.t AS SELECT * FROM public.t`) — 크기와 무관하게 빠르고 트랜잭션으로 원자적이다.

- **범위는 `[SpecData]` 뿐** — 서버 `suparun_snapshot_tables()` 가 `config_types` 만 보므로
  이 화면에서 무엇을 눌러도 `[UserData]` 에 닿지 못한다. 플레이어 데이터 롤백은 성격이 다른 일이라
  PITR·감사로그·지급 도구가 맡는다
- **복원 직전 자동 저장** — `suparun_snapshot_restore` 가 먼저 현재 상태를 `auto` 로 한 장 찍고 되돌린다.
  반환값이 그 이름이라 화면이 "돌아올 자리"를 바로 알려 준다
- **공통 컬럼만 복원** — `SELECT *` 로 하면 컬럼이 하나만 늘어도 복원이 실패한다. 스키마 생성기가
  `ADD COLUMN` 만 하고 `DROP` 을 안 하므로 시간이 갈수록 반드시 어긋난다. 새 컬럼은 기본값으로 남고,
  확인 모달이 `+2 기본값` / `-1 버려짐` 배지로 미리 알린다
- **핀 = 보관 여부, `[auto]` 배지 = 출처.** 둘을 나눈 이유는 '자동으로 찍혔지만 남겨둘 것' 이
  표현돼야 하기 때문이다. 핀 없는 자동본은 최근 5개만 남는다(`suparun_snapshot_keep_count()`)
- **복원 가드**: 라벨을 손으로 쳐야 버튼이 열린다. 복원 후에는 보고 있던 표가 낡으므로 페이지를 새로고침한다
- 진입점 둘: 사이드바 `[SYSTEM] > snapshots`, 그리고 **타이틀바 `SNAP`** — 위험한 편집은 Config 표
  위에서 벌어지는데 찍으려고 화면을 옮겨야 하면 결국 안 찍게 된다

> **RPC 를 쓰는 이유와 범위**: 브라우저는 DDL 을 실행할 수 없다. `suparun_set_policy` 와 같은 사정이고
> 같은 방어 구조를 쓴다 — SECURITY DEFINER + `search_path` 고정 + `is_admin()` + 화이트리스트 +
> 식별자는 `quote_ident`/`format('%I')` 로만 조립. **찍기·복원·삭제·차이 4개만 RPC**이고,
> 목록 조회·코멘트 수정·핀 토글은 `suparun_snapshot` 표를 PostgREST 로 그냥 다룬다.
>
> ⚠ 반환 컬럼 이름을 `table_name` 으로 지으면 함수 본문의 `information_schema.columns` 조회와 충돌해
> `column reference is ambiguous` 로 죽는다. 그래서 `tbl_name` / `cur_rows` / `added_cols` 꼴이다.

### 내보내기/가져오기
- **내보내기**: Config 데이터를 JSON 파일로 다운로드 (`/_export`)
- **가져오기**: JSON 파일 업로드로 기존 데이터 교체 (`/_import`)

### UI/UX
- **타이틀바 = "지금 어디에 누구로, 언제인가"** — `SUPARUN.ADMIN :: [환경 ▾] › 화면` 경로 +
  시계(`YY.MM.DD HH:mm:ss`, 배지 클릭으로 로컬↔UTC 토글·localStorage 기억).
  환경 드롭다운은 로컬(브리지)에서 **확인창 없는 즉시 전환 입장**(카드 클릭과 같은 결정),
  배포 어드민에선 표시 전용 칩(클릭=환경 화면). prod 강조는 일부러 없다(사용자 결정).
  사이드바 상태줄의 `env` 행은 "dev" 하드코딩 거짓 표시라 제거했다
- Tabler CSS 프레임워크 (다크 사이드바 + 라이트 콘텐츠)
- Toast 알림 (success/error/info, 3초 자동 소멸)
- **로딩 표시** — `shared/Spinner.tsx` 3종. 모양은 CSS `.sr-spinner` 하나가 갖는다
  (conic-gradient 를 `mask` 로 뚫어 만든 원호. border 로는 그라데이션이 안 된다)
  - `FullScreenLoader` — 첫 세션 확인. 예전엔 이 구간이 통째로 빈 검은 화면이었다
  - `LoadingBlock` — Config/Table/Admin/Audit 목록과 홈 화면이 오기 전 그 자리
  - `Spinner` — 사이드바 트리 로딩, 로그인/회원가입/OAuth 버튼 (누르는 동안 전 버튼 잠금)
- 페이지 전환 fade 애니메이션
- 행 진입/삭제/하이라이트 애니메이션

### 컬럼 자동 너비 / Wrap 토글 / 컨텍스트 메뉴
- **자동 너비**: 헤더 텍스트(canvas measureText) + 데이터 sample(50행 char count) → 타입별 min/max 적용
- **Wrap 자동 감지**: 컬럼명에 description/desc/comment/memo/note/message/reason/detail 포함 OR 데이터 평균 60자+ → 자동 wrap
- **사용자 토글**: 헤더 우클릭 → "Wrap Text" / "Reset This Width" / "Reset All Cols"
- **수동 width 영구 우선**: 사용자 드래그 폭은 localStorage 저장값이 자동 계산보다 우선
- **저장 형식 (`col_w_<key>`)**: `{ widths: {0:120,2:80}, wraps: {0:true} }` object. 레거시 배열(`[w1,w2,...]`)은 자동 마이그레이션
- **적용 범위**: Config 평면 테이블 / JSON 모달 (nested 포함) / Admin / Audit log / Table view / Cross search / Player tables 7곳

## API 엔드포인트 (서버 측에서 제공해야 함)

| 메서드 | 경로 | 설명 |
|--------|------|------|
| GET | `/admin/api/config/_types` | Config 타입 목록 |
| GET | `/admin/api/config/{table}` | Config 전체 조회 |
| POST | `/admin/api/config/{table}` | Config 행 추가 |
| PUT | `/admin/api/config/{table}/{id}` | Config 행 수정 |
| DELETE | `/admin/api/config/{table}/{id}` | Config 행 삭제 |
| GET | `/admin/api/config/_export/{table}` | Config JSON 내보내기 |
| POST | `/admin/api/config/_import/{table}` | Config JSON 가져오기 |
| GET | `/admin/api/config/_audit` | 변경 이력 조회 |
| GET | `/admin/api/table/_types` | Table 타입 목록 |
| GET | `/admin/api/table/{table}` | Table 데이터 조회 (필터/페이징) |
| GET | `/admin/api/table/{table}/_stats` | Table 필드 통계 |
| GET | `/admin/api/table/{table}/_distribution` | Table 분포 데이터 |
| POST | `/admin/api/table/_cross` | 크로스 테이블 검색 |
| GET | `/admin/api/player/{userId}` | 플레이어 전체 데이터 조회 |
| PUT | `/admin/api/table/{table}/{id}` | Table 행 수정 |
| GET | `/admin/api/admins` | 관리자 목록 |
| PUT | `/admin/api/admins/{id}/role` | 관리자 역할 변경 |
| DELETE | `/admin/api/admins/{id}` | 관리자 삭제 |

## 주의사항

- 단일 HTML 파일 SPA로 구성 — 별도 빌드 과정 없이 정적 파일로 서빙
- 플레이스홀더(`{{...}}`)가 치환되지 않으면 Supabase 연결 실패 에러를 사용자에게 표시
- `window.onerror` / `unhandledrejection` 핸들러로 빈 화면 방지
- Admin API 인증은 서버 측 미들웨어에서 `admin_users` 테이블 기반으로 처리
- Rate Limiting은 admin API 경로 면제 (서버 측 설정)
