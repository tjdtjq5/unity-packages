# Claude

Unity 에디터에서 Claude Code CLI 실행 + git worktree 관리.

## 기능

### Manager 윈도우 (`Tools > Claude Code > Manager`)
- **메인 실행**: 프로젝트 루트에서 Claude Code 실행
- **워크트리 생성**: 독립 환경(git worktree) 생성 + 선택적 자동 실행
- **워크트리 목록**: 활성 워크트리 현황 (dirty/clean 상태 표시)
- **개별/전체 삭제**: 워크트리 정리

### 툴바 버튼
- `✦ Claude [N]` — 좌클릭: Manager 윈도우, 우클릭: 설정
- 활성 워크트리 수가 벳지로 표시됨

### Settings (`Tools > Claude Code > Settings`)
- **기본 설정**: 모델(sonnet/opus/haiku, settings.json 저장) + Effort(low/medium/high/max, `--effort` CLI 인자로 전달)
- 추가 인자 (claude CLI 옵션)
- 탭 색상 (메인/워크트리)
- Windows Terminal 윈도우 이름
- AutoLaunch (워크트리 생성 후 자동 실행)
- Discord 연동 (3단계 위자드)
- Remote Control

## 설치

```json
"com.tjdtjq5.claude": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.claude#claude/v1.1.2"
```

## 요구사항
- Unity 6000.1+
- Windows (Windows Terminal 권장, PowerShell fallback)
- `com.tjdtjq5.editor-toolkit` 패키지

## 상태
v1.1.2
