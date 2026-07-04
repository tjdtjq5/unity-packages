# Terminal

Unity 에디터 툴바에서 원하는 터미널을 프로젝트 루트로 바로 여는 런처.

> `com.tjdtjq5.claude`의 후속 패키지. Claude/Discord/워크트리 기능을 전부 걷어내고
> "터미널을 간편하게 연다" 하나에 집중한 재설계다.

## 설치

`Packages/manifest.json`의 `dependencies`에 추가:

```json
"com.tjdtjq5.terminal": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.terminal#terminal/v1.0.0"
```

## 사용법

- **툴바 버튼 좌클릭** — 선택된 터미널로 프로젝트 루트를 연다
- **툴바 버튼 우클릭** — 터미널 전환(라디오) / 목록 편집
- 메뉴: `Tjdtjq/Terminal/터미널 열기`, `Tjdtjq/Terminal/목록 편집`

## 터미널 목록 (완전 데이터 주도)

목록 전체가 편집 가능한 데이터다 (EditorPrefs, 머신별 저장).
첫 실행 시 **설치된 터미널만** 자동 시드된다.

| 기본 시드 | 명령 |
|-----------|------|
| Windows Terminal | `wt -d "{dir}"` |
| Warp (Win) | `warp://action/new_window?path={dirUri}` |
| PowerShell | `powershell` |
| Terminal (Mac) | `open -a Terminal "{dir}"` |
| iTerm2 (Mac) | `open -a iTerm "{dir}"` |
| Warp (Mac) | `open -a Warp "{dir}"` |

### 플레이스홀더

- `{dir}` — 프로젝트 루트 절대 경로
- `{dirUri}` — URL 인코딩된 경로 (URI 스킴용)
- 명령이 `scheme://` 으로 시작하면 URI로 실행, 아니면 `실행파일 + 인자`로 분리 실행
- 인자 없는 셸(`powershell` 등)은 WorkingDirectory가 프로젝트 루트로 설정됨

### 자동 감지 (주문형)

- 편집 창의 **[자동 감지]** — 설치돼 있는데 목록에 없는 알려진 터미널을 추가만 한다
  (사용자가 편집/삭제한 항목은 건드리지 않음, 이름 기준 중복 방지)
- 행 왼쪽 ✓/✗/- 는 설치 여부 (- 는 판단 불가). **[설치 확인 ↻]** 로 재검사

### 새 터미널 추가

새 터미널이 나오면 패키지 업데이트 없이 **[+ 추가]** 로 이름/명령만 입력하면 된다.

## 의존성

- `com.tjdtjq5.editor-toolkit` (툴바 삽입 + 에디터 UI 스타일)

## 주의

- 프로필 이름이 중복되면 선택이 첫 매치로 동작한다 — 이름은 고유하게 유지할 것
- Warp URI 스킴(`warp://action/new_window?path=`)은 Warp 버전에 따라 바뀔 수 있음 —
  안 열리면 목록 편집에서 명령만 고치면 된다
