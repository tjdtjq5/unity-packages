# AdminTemplate

> **상태**: React 전환 완료 (ADR-0003 — 로그인부터 전 화면까지 React. 실기 확인 대기)
> **용도**: Supabase Auth 기반 로그인과 Config/Table CRUD를 제공하는 어드민 웹 페이지 SPA 템플릿

## 의존성

- `../AspNetTemplate~/` — 서버가 `/admin` 경로로 이 페이지를 정적 파일로 서빙
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
│   ├── index.html      1,494줄(최초 3,950). CSS 1,287줄 + 인라인 JS 171줄(플레이스홀더·프리뷰 mock)
│   ├── main.tsx        React 진입점 — #root 에 <App/> 마운트
│   ├── App.tsx         로그인 화면 ↔ 어드민 껍데기 분기
│   ├── shared/         api / supabase / toast / types / Modal / colResize / castValue / env / chart
│   └── features/       화면 단위 폴더
│       ├── auth/       로그인 · 세션 구독
│       ├── shell/      껍데기 — 레이아웃·사이드바·툴바·라우팅·키맵·AdminContext
│       └── …           admins / audit / table / cross / player / config
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
> 표 레이아웃에 갇혀 엉뚱한 곳에 뜬다. 해당 요소는 `.icon-grid-overlay`(모달)와 `.ss-pop`(검색 드롭다운).
> 모달은 `shared/Modal.tsx`, 드롭다운은 `SearchSelect` 가 각각 portal + 좌표 계산을 담당한다.
> z-index 순서: `.ss-pop`(1100) > `.icon-grid-overlay`(1090) — 모달 안에서도 드롭다운이 보여야 한다.

## 플레이스홀더 변수

배포 시 SupaRun이 아래 플레이스홀더를 실제 값으로 치환합니다.

| 플레이스홀더 | 타입 | 설명 |
|-------------|------|------|
| `{{SUPABASE_URL}}` | string | Supabase 프로젝트 URL (`https://xxx.supabase.co`) |
| `{{SUPABASE_ANON_KEY}}` | string | Supabase Anonymous Key |
| `{{AUTH_PROVIDERS_JSON}}` | JSON 배열 | OAuth 프로바이더 목록 (예: `["google","kakao"]`) |

## 주요 기능

### 인증
- **이메일 로그인/회원가입**: Supabase Auth `signInWithPassword` / `signUp`
- **OAuth 로그인**: `{{AUTH_PROVIDERS_JSON}}`에 설정된 프로바이더 동적 생성 (Google, Kakao, Apple)
- **세션 관리**: `onAuthStateChange`로 토큰 자동 갱신, 만료 시 재로그인 유도
- **첫 번째 가입자 자동 admin 승인**: `admin_users` 테이블 비어있으면 첫 유저가 admin

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

### 내보내기/가져오기
- **내보내기**: Config 데이터를 JSON 파일로 다운로드 (`/_export`)
- **가져오기**: JSON 파일 업로드로 기존 데이터 교체 (`/_import`)

### UI/UX
- Tabler CSS 프레임워크 (다크 사이드바 + 라이트 콘텐츠)
- Toast 알림 (success/error/info, 3초 자동 소멸)
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
