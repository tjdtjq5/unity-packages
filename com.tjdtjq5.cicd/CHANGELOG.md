# Changelog

## [0.5.5] - 2026-04-19

### Changed
- **LicenseStep**: Unity 웹 활성화 페이지(.alf → .ulf) 방식 제거, Unity Hub 기반 `Unity_lic.ulf` 자동 탐색 방식으로 교체
  - Unity가 2024년 중반 Personal 라이선스 수동 활성화 페이지를 중단하면서 기존 흐름(`option-personal` 숨김 해제 꼼수)이 동작하지 않음
  - OS별 표준 경로에서 자동 탐색 → 발견 시 바로 로드, 미발견 시 Hub 실행 딥링크 + 4단계 체크리스트 가이드
  - `[직접 파일 선택]` 폴백 상시 노출
- **WorkflowGenerator**: GitHub Actions 캐시 자동 정리 (최신 1개만 유지)
  - `permissions: actions: write` 자동 추가
  - `gh actions-cache` 기반 오래된 캐시 삭제 스텝 추가

### Removed
- `LicenseStep.GenerateAlf()` — batchmode로 `.alf` 생성하던 죽은 로직
- Unity 웹 라이선스 활성화 링크 + "F12로 option-personal 숨김 해제" 가이드

## [0.1.0] - 2026-03-21

### Added
- 패키지 초기 생성
- SetupWizard: GameCI 기반 CI/CD 초기 설정 6단계
- WorkflowGenerator: GitHub Actions yml 자동 생성
- VersionPreprocessor: git tag 기반 버전 자동 관리
- SecretsChecklist: 필요 GitHub Secrets 동적 목록
- 지원 플랫폼: Android, iOS, Windows, WebGL
- 배포 대상: GitHub Releases, Google Play, App Store, Steam
- 알림: Discord, Slack, Custom 웹훅
