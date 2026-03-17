# unity-packages

Unity 6000.1+ 커스텀 패키지 모노리포.

> 모든 패키지는 `manifest.json`에 Git URL을 추가하면 자동 설치됩니다.

## 설치 방법

`Packages/manifest.json`의 `dependencies`에 아래 URL을 추가:

```json
"com.tjdtjq5.패키지이름": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.패키지이름#태그"
```

또는 Unity 에디터 → `Window > Package Manager > + > Add package from git URL...` 에 URL 입력.

---

## Packages

### Editor Toolkit `v1.1.2`

Odin 스타일 커스텀 Attribute + 에디터 프레임워크

| 기능 | 내용 |
|------|------|
| Inspector Attributes | `[ReadOnly]` `[ShowIf]` `[HideIf]` `[BoxGroup]` `[Required]` 등 18종 |
| EditorTabBase | 탭 기반 에디터 윈도우 공통 베이스 (알림, 탭 바, 리사이저블 컬럼, 테이블, JSON 유틸) |
| JsonHelper | 경량 JSON 파서 (GetString/GetInt/GetObject/FindBrace 등) |
| SceneBookmark | 씬 즐겨찾기 툴바 + 게임 속도 조절 |

```json
"com.tjdtjq5.editor-toolkit": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-toolkit#editor-toolkit/v1.1.2"
```

> 의존: 없음 (독립)

---

### UGS Manager `v1.3.0`

Unity Gaming Services CLI 래핑 에디터 윈도우 (`Tools > UGS Manager` 또는 `Ctrl+Shift+U`)

| 탭 | 기능 |
|----|------|
| Env | 환경 전환/생성 + 통합 배포/Fetch + 전체 환경 동기화 |
| Config | Remote Config 키-값 관리, 그룹, 환경 비교(diff) |
| Cloud Code | Scripts/Schedules/Triggers 통합, @param 파싱, 프롬프트 복사 |
| Economy | 통화/아이템/구매 관리, 인라인 편집, Deploy+Publish |
| LB | Leaderboards 관리, 티어, 리셋, REST API 순위 조회 |
| Player | Cloud Save 플레이어 데이터 조회/편집, 북마크 |
| Custom | Cloud Save 커스텀 엔티티 조회/편집 |

```json
"com.tjdtjq5.ugs-manager": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ugs-manager#ugs-manager/v1.3.0"
```

> 의존: `editor-toolkit >= 1.1.0`

**사전 설정:**
1. UGS CLI 설치: `npm install -g ugs`
2. Service Account 로그인: `ugs login --service-key-id <KEY_ID> --secret-key-stdin`
3. 프로젝트/환경 설정: `ugs config set project-id <ID>` + `ugs config set environment-name dev`
4. UGS Manager 열기: `Tools > UGS Manager` → ⚙ Settings에서 조직 ID, 경로 설정

---

### UI Framework `v1.0.1`

모바일 게임 UI 프레임워크

| 카테고리 | 컴포넌트 |
|----------|----------|
| Components | SafeAreaFitter, ButtonClickEffect, UIShake, NumberCounter, UIStateBinder, UIFlyEffect, UIFollowWorld, UIProgressBar, UITabGroup, UIToast, UITutorialMask |
| Popup | UITransition, UIPopup (스택/큐), UIManager, UIDialog (확인/취소) |
| DI | UILifetimeScope (VContainer 자동 등록) |
| Editor | UIStateBinderEditor (커스텀 Inspector) |
| Shaders | TutorialMask (Stencil 기반 하이라이트) |

```json
"com.tjdtjq5.ui-framework": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ui-framework#ui-framework/v1.0.1"
```

> 의존: `editor-toolkit >= 1.1.0`, DOTween, VContainer

**사전 설정:**
1. DOTween 설치 (Asset Store 또는 OpenUPM)
2. VContainer 설치 (`manifest.json`에 Git URL 추가)
3. **DOTween ASMDEF 생성 필수**: Unity 에디터 → `Tools > Demigiant > DOTween Utility Panel > Create ASMDEF...`
4. 생성된 `DOTween.Modules.asmdef`에 `overrideReferences: true` + `precompiledReferences: ["DOTween.dll"]` 설정

> ⚠ DOTween ASMDEF를 생성하지 않으면 `DOFade`, `DOAnchorPos` 등 확장 메서드를 찾을 수 없어 컴파일 에러가 발생합니다.

---

### Claude `v0.2.1`

Unity 에디터에서 Claude Code CLI 실행 + git worktree 자동 관리

| 기능 | 내용 |
|------|------|
| 툴바 버튼 | 메인 툴바에 ✦ Claude 버튼 (좌클릭: 터미널, 우클릭: 설정) |
| 터미널 런처 | Windows Terminal 탭 / PowerShell fallback |
| Worktree 관리 | 첫 실행: 메인, 이후: git worktree 자동 생성 + 새 탭 |
| 설정 | 탭 색상, 윈도우 이름, 추가 인자 (EditorPrefs 기반) |

```json
"com.tjdtjq5.claude": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.claude#claude/v0.2.1"
```

> 의존: `editor-toolkit >= 1.0.0`

---

## 의존성 관계

```
editor-toolkit (독립)
  ├── ugs-manager
  └── ui-framework (+ DOTween, VContainer)

claude (→ editor-toolkit)
```

## 요구사항

- Unity **6000.1** 이상
- UGS Manager: `ugs` CLI 설치 필요 (`npm install -g ugs`)
- UI Framework: DOTween + VContainer + **DOTween ASMDEF 생성 필수**

## 트러블슈팅

| 문제 | 해결 |
|------|------|
| `Tjdtjq5.UIFramework` 네임스페이스 못 찾음 | `editor-toolkit >= 1.1.0` 설치 확인 |
| `DOFade`, `DOAnchorPos` 컴파일 에러 | DOTween Utility Panel에서 ASMDEF 생성 |
| `will not be compiled, no scripts` 경고 | 무시 가능 (빈 asmdef) |
| UGS CLI 명령 실패 | `ugs status`로 로그인 상태 확인 |
