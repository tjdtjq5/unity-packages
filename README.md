# unity-packages

Unity 6000.1+ 커스텀 패키지 모노리포.

> 모든 패키지는 `manifest.json`에 Git URL을 추가하면 자동 설치됩니다.

---

## Packages

### Editor Toolkit `v1.1.0`

Odin 스타일 커스텀 Attribute + 에디터 프레임워크

| 기능 | 내용 |
|------|------|
| Inspector Attributes | `[ReadOnly]` `[ShowIf]` `[HideIf]` `[BoxGroup]` `[Required]` 등 18종 |
| EditorTabBase | 탭 기반 에디터 윈도우 공통 베이스 (색상, 폴드아웃, 카드 드로잉) |
| SceneBookmark | 씬 즐겨찾기 툴바 + 게임 속도 조절 |

```json
"com.tjdtjq5.editor-toolkit": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-toolkit#editor-toolkit/v1.1.0"
```

---

### Editor Windows `v0.1.0`

TableWindow / TestWindow 탭 기반 에디터 윈도우

| 기능 | 내용 |
|------|------|
| TableWindow | 데이터 테이블 SO 일괄 관리 (`Ctrl+Shift+D`) |
| TestWindow | Play 모드 테스트 도구 (`Ctrl+Shift+T`) |

```json
"com.tjdtjq5.editor-windows": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-windows#editor-windows/v0.1.0"
```

> 의존: `editor-toolkit >= 1.0.0`

---

### UGS Manager `v1.2.0`

Unity Gaming Services CLI 래핑 에디터 윈도우

| 기능 | 내용 |
|------|------|
| Remote Config | 키-값 조회, 수정, 스키마 동기화 |
| Cloud Code | 스크립트 목록, 업로드, 실행 |
| Environment | 환경 전환 (dev / staging / production) |
| Deploy | 원클릭 배포 |

```json
"com.tjdtjq5.ugs-manager": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ugs-manager#ugs-manager/v1.2.0"
```

> 의존: `editor-toolkit >= 1.0.0`

---

### UI Framework `v1.0.0`

모바일 게임 UI 프레임워크 (DOTween + VContainer + EditorToolkit)

| 카테고리 | 컴포넌트 |
|----------|----------|
| Components | SafeAreaFitter, ButtonClickEffect, UIShake, NumberCounter, UIStateBinder, UIFlyEffect, UIFollowWorld, UIProgressBar, UITabGroup, UIToast, UITutorialMask |
| Popup | UITransition, UIPopup (스택/큐), UIManager, UIDialog (확인/취소) |
| DI | UILifetimeScope (VContainer 자동 등록) |
| Editor | UIStateBinderEditor (커스텀 Inspector) |
| Shaders | TutorialMask (Stencil 기반 하이라이트) |

```json
"com.tjdtjq5.ui-framework": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ui-framework#ui-framework/v1.0.0"
```

> 의존: `editor-toolkit >= 1.0.0`, DOTween, VContainer

---

### Claude Launcher `v0.1.0`

Unity 에디터에서 Claude Code CLI 실행 + git worktree 자동 관리

| 기능 | 내용 |
|------|------|
| 터미널 런처 | 에디터 메뉴에서 Claude Code 세션 시작 |
| Worktree 관리 | 작업별 git worktree 자동 생성 / 정리 |

```json
"com.tjdtjq5.claude-launcher": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.claude-launcher#claude-launcher/v0.1.0"
```

---

## 의존성 관계

```
editor-toolkit (독립)
  ├── editor-windows
  ├── ugs-manager
  └── ui-framework (+ DOTween, VContainer)

claude-launcher (독립)
```

## 요구사항

- Unity **6000.1** 이상
- UGS Manager: `ugs` CLI 설치 필요 (`npm install -g ugs`)
