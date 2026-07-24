# Changelog

## [2.0.0] - 2026-07-24

### Breaking Changes
- **`com.tjdtjq5.editor-toolkit` 의존 완전 제거** — 툴바 주입 헬퍼(`ToolbarHelper`)를 패키지에 내장, 목록 윈도우의 스타일 호출을 바닐라 IMGUI로 치환. 이제 의존성 없이 단독 설치 가능.

## [1.0.3] - 2026-07-04

### 수정
- WezTerm/Alacritty 실행 실패 수정: 기본 설치 경로로 감지된 경우(PATH 미등록) 명령도 절대경로 사용
  - 기존 시드된 행은 자동 수정되지 않음 — 해당 행 삭제 후 [자동 감지]로 재등록 필요

### 신규
- 자동 감지에 orchterm 추가 (`%LOCALAPPDATA%\orchterm\app.exe`, CWD 상속 방식)

## [1.0.2] - 2026-07-04

### 신규
- 자동 감지에 winghostty 추가 (Ghostty의 Windows 포트, `Program Files\winghostty` + `where` 폴백)
  - `--working-directory` 명시 플래그 사용 (v1.3.116 config docs에서 확인)

## [1.0.1] - 2026-07-04

### 신규
- 자동 감지 테이블 6종 확장: Alacritty, WezTerm, Git Bash(Win), Tabby, cmder(Win), Ghostty(Mac)
  - Win: `where` + 기본 설치 경로 확인, Mac: `/Applications/*.app` 확인
  - cmder는 portable 앱 특성상 `CMDER_ROOT` 환경변수 또는 PATH 등록 시에만 감지됨
  - Tabby/Ghostty의 디렉토리 열기 명령은 실기기 검증 전 — 문제 시 목록 편집에서 명령 수정

## [1.0.0] - 2026-07-04

`com.tjdtjq5.claude` v1.2.x를 대체하는 신규 패키지. "터미널을 간편하게 연다" 하나로 재설계.

### 신규
- 완전 데이터 주도 터미널 프로필 목록 (`{name, command}`, EditorPrefs 머신별 저장)
  - `{dir}` / `{dirUri}` 플레이스홀더, `scheme://` URI 실행 지원
  - 첫 실행 시 설치된 터미널만 자동 시드 (Win: Windows Terminal/Warp/PowerShell, Mac: Terminal/iTerm2/Warp)
- 툴바 버튼: 좌클릭 = 선택된 터미널로 프로젝트 루트 열기, 우클릭 = 터미널 전환/목록 편집 메뉴
  - 버튼 라벨 = 현재 선택된 터미널 이름
- 목록 편집 창: 추가/수정/삭제, 주문형 [자동 감지], 설치 여부 ✓/✗ 표시
- Warp 지원 (Windows: URI 스킴, Mac: `open -a`)

### 제거 (구 com.tjdtjq5.claude 대비)
- Claude Code 실행/설정 전부 (모델/effort/args/BypassPermissions/settings.json 조작)
- git worktree 생성/관리/벳지 폴링
- Discord 연동 (ChannelBridge, Bridge~ Node 앱, 설정 위자드)
- Remote Control, `.mcp.json` 자동 생성
- cmux/iTerm2 AppleScript 네이티브 연동 (단순 `open -a` 실행으로 대체)
