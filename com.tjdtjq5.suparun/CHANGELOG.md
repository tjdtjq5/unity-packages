# Changelog

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
