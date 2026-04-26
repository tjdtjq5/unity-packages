# Changelog

## [1.1.5] - 2026-04-26

### Fixed
- macOS cmux 런처에서 데몬이 이미 떠있는데도 매번 `open -a cmux`를 호출하던 동작 제거 — 일부 환경에서 워크스페이스가 attach되지 않은 빈 윈도우가 추가로 뜨는 부작용을 차단
- 새 cmux 워크스페이스 생성 후 그 워크스페이스가 속한 윈도우를 명시적으로 `focus-window`로 활성화 — 새 워크트리 탭이 다른 창/백그라운드에 있어 사용자가 못 보는 경우를 막음

## [1.1.4] - 2026-04-26

### Added
- macOS cmux 터미널 지원 — cmux.app + CLI 감지 시 자동 사용 (iTerm2/Terminal.app 폴백 우선순위 추가)
- Settings 창 "기본 설정" 에 **권한 프롬프트 우회** 체크박스 — `<project>/.claude/settings.local.json` 의 `defaultMode = "bypassPermissions"` 토글

### Changed
- `ClaudeCodeSettings.ReadSettingsKey/WriteSettingsKey` 를 path 인자 받는 형태로 일반화 (글로벌 + 프로젝트 로컬 settings.json 모두 지원)

## [1.1.3] - 2026-04-25

### Added
- macOS 지원: iTerm2 우선 → Terminal.app 폴백 (AppleScript 기반)
- `PlatformTerminalLauncher` 헬퍼 — 터미널 실행 플랫폼 분기 추상화 (Main/Worktree TabRole)

### Changed
- 터미널 런처 로직을 `ClaudeCodeLauncher` → `PlatformTerminalLauncher`로 분리 이동
- Channel Bridge Named Pipe 경로를 .NET `CoreFxPipe_` 컨벤션에 맞춰 macOS Unix Domain Socket과 호환 (`/tmp/CoreFxPipe_claude-unity-{hash}`)

## [1.1.2] - 2026-04-09

### Changed
- Effort 레벨을 `--effort` CLI 인자로 직접 전달 (settings.json → EditorPrefs로 저장 변경)
- Settings UI 힌트 텍스트 개선 (모델: settings.json, Effort: CLI 인자 구분 표시)

## [1.1.1] - 2026-04-09

### Added
- Settings 창에 "기본 설정" 섹션 추가 (모델/Effort 드롭다운)
- `~/.claude/settings.json` 직접 읽기/쓰기 지원 (Newtonsoft.Json)

### Changed
- ArgOptions에서 `--model`/`--effort` 항목 제거 (기본 설정 UI로 대체)

## [1.1.0] - 2026-04-06

### Added
- Remote Control 지원 (`--remote-control` 자동 플래그)
- Channel Bridge MCP 서버 연동
- Discord 3단계 위자드 설정
