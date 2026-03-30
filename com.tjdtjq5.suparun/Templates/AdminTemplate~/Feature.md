# AdminTemplate

> **상태**: stable
> **용도**: Supabase Auth 기반 로그인과 Config/Table CRUD를 제공하는 어드민 웹 페이지 SPA 템플릿

## 의존성

- `../AspNetTemplate~/` — 서버가 `/admin` 경로로 이 페이지를 정적 파일로 서빙
- 외부 CDN: Tabler CSS/JS, Bootstrap 5, Supabase JS v2, Chart.js

## 구조

| 파일 | 설명 |
|------|------|
| `index.html` | 단일 파일 SPA (HTML + CSS + JS, ~1440줄). 로그인/어드민 화면 전체 포함 |

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

### JSON 편집
- **Rewards 모달**: `rewards` / `*_rewards` 필드 전용 — 재화/아이템 타입 + ID 드롭다운 + 수량
- **범용 JSON 배열 에디터**: 기타 JSON 필드 — 첫 항목 기반 스키마 자동 감지, 행 추가/삭제

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
