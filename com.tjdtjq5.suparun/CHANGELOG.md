# Changelog

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
