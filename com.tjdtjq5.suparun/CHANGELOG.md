# Changelog

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
