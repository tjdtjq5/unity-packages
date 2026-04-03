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

### Editor Toolkit `v1.3.3`

커스텀 Attribute + 에디터 프레임워크

| 기능 | 내용 |
|------|------|
| Inspector Attributes | `[ReadOnly]` `[ShowIf]` `[HideIf]` `[BoxGroup]` `[Required]` 등 18종 |
| EditorTabBase | 탭 기반 에디터 윈도우 공통 베이스 (알림, 탭 바, 리사이저블 컬럼, 테이블, JSON 유틸) |
| JsonHelper | 경량 JSON 파서 (GetString/GetInt/GetObject/FindBrace 등) |
| SceneBookmark | 씬 즐겨찾기 툴바 + 게임 속도 조절 |

```json
"com.tjdtjq5.editor-toolkit": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-toolkit#editor-toolkit/v1.3.3"
```

> 의존: 없음 (독립)

---

### CI/CD `v0.5.4`

GameCI 기반 Unity CI/CD 파이프라인 자동 생성

| 기능 | 내용 |
|------|------|
| SetupWizard | 6단계 초기 설정 (gh CLI, Unity 라이선스, 플랫폼, 배포, Secrets) |
| WorkflowGenerator | GitHub Actions yml 자동 생성 (GameCI unity-builder) |
| Release | 에디터에서 버전 태그 + 빌드 트리거 + 상태 모니터링 |
| Secret 자동 등록 | gh secret set으로 자동 등록 |
| 버전 관리 | git tag 기반 자동 버전 (IPreprocessBuildWithReport) |
| 알림 | Discord / Slack / Custom 웹훅 |

```json
"com.tjdtjq5.cicd": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.cicd#cicd/v0.5.4"
```

> 의존: `editor-toolkit >= 1.3.0`

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

### UI Toolkit `v2.0.1`

범용 UI 도구 모음 — 독립적으로 사용 가능한 UI 컴포넌트

| 컴포넌트 | 용도 |
|----------|------|
| RecycleScrollView | 무한 재사용 스크롤 (Vertical/Horizontal, Grid, Cell/Page 스냅) |
| UIStateBinder | enum/string 상태 전환 바인딩 (Exclusive/Animator/Visual/Tween) |
| UIFlyEffect | 베지어 곡선 플라이 애니메이션 (코인 획득 등) |
| ButtonClickEffect | 버튼 누르기 스케일 펀치 |
| NumberCounter | 숫자 카운팅 트윈 (TMP) |
| SafeAreaFitter | 모바일 노치/SafeArea 자동 적응 |
| UIShake | RectTransform 흔들림 효과 |
| UITutorialMask | 스텐실 기반 튜토리얼 스포트라이트 |

```json
"com.tjdtjq5.ui-framework": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ui-framework#ui-framework/v2.0.1"
```

> 의존: `editor-toolkit >= 1.1.0`, DOTween

**사전 설정:**
1. DOTween 설치 (Asset Store 또는 OpenUPM)
2. **DOTween ASMDEF 생성 필수**: Unity 에디터 → `Tools > Demigiant > DOTween Utility Panel > Create ASMDEF...`
3. 생성된 `DOTween.Modules.asmdef`에 `overrideReferences: true` + `precompiledReferences: ["DOTween.dll"]` 설정

> ⚠ v2.0.0에서 Popup 시스템 제거됨 (UIPopup, UIManager, UITransition, UIDialog). VContainer 의존성도 제거. 기존 v1.x Popup 사용자는 로컬 이관 필요.

---

### AddrX `v0.1.4`

Unity Addressables 안전 래퍼 — SafeHandle 기반 자동 해제, 누수 감지, 에디터 분석 도구

| 기능 | 내용 |
|------|------|
| SafeHandle\<T\> | `using` 자동 해제 / `BindTo(GO)` 수명 연동 / 수동 Dispose |
| AddrX API | `LoadAsync`, `LoadBatchAsync`, `InstantiateAsync` 정적 메서드 |
| HandleTracker | 활성 핸들 실시간 추적, 콜스택 기록 |
| LeakDetector | 씬 전환 시 미해제 핸들 자동 감지 |
| DebugHUD | 인게임 오버레이 (F9 토글) |
| 자동 등록 | `Assets/Addressables/` 하위 에셋 자동 그룹/라벨 등록 |
| Analysis | 중복 검사, 그룹 건강도, 크기 예산, 에디터/빌드 Diff |
| AddrX Manager | Setup / Tracker / Analysis 3탭 에디터 윈도우 (`Alt+Shift+A`) |

```json
"com.tjdtjq5.addrx": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.addrx#addrx/v0.1.4"
```

> 의존: `editor-toolkit >= 1.3.0`, `com.unity.addressables >= 2.2.2`

---

### Claude `v1.1.0`

Unity 에디터에서 Claude Code CLI 실행 + Channel Bridge(MCP) + Discord 연동 + Remote Control + git worktree 관리

| 기능 | 내용 |
|------|------|
| 툴바 버튼 | ✦ Claude [N] ● — 좌클릭: Manager, 우클릭: Settings, ● 연결 상태 |
| 터미널 런처 | Windows Terminal 탭 / PowerShell fallback |
| Channel Bridge | Unity 콘솔/컴파일 에러 → Named Pipe → MCP Channel → Claude 자동 전달 |
| Discord 연동 | 3단계 모드 (없음/알림/적극적 사용), 양방향 통신, !mode/!mute/!status |
| Remote Control | `--rc` 플래그로 claude.ai/code, 모바일 앱에서 세션 접속 |
| Worktree 관리 | git worktree 자동 생성/삭제 + 새 탭 |
| 설정 UI | 모니터/Discord/RC/탭 색상/CLI 인자 통합 설정 |

```json
"com.tjdtjq5.claude": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.claude#claude/v1.1.0"
```

> 의존: `editor-toolkit >= 1.0.0`
> Bridge 의존: Node.js, `@modelcontextprotocol/sdk`, `discord.js` (Bridge~/에서 npm install)

---

### SupaRun `v0.3.2`

Unity Editor에서 게임 서버 인프라를 관리하는 올인원 패키지. Supabase + Cloud Run 자동 배포.

| 기능 | 내용 |
|------|------|
| [Table] / [Config] | 어트리뷰트로 DB 테이블 + CRUD 자동 생성 |
| [Service] / [API] | 서버 로직 작성 → 타입 안전 프록시 자동 생성 |
| LocalGameDB | 미배포 로직은 Unity 내에서 즉시 실행 |
| Dashboard | Status / Deploy / Monitor 3탭 대시보드 |
| Auth | 게스트 자동 로그인 + OAuth 12종 + GPGS/GameCenter |
| Features | Currency, Inventory, Shop 등 9종 템플릿 (+ SeasonPass) |
| Admin | Tabler 웹 어드민 (Config CRUD, Table 조회/차트, 크로스 검색) |
| 배포 | 원버튼 → GitHub push → Cloud Run 자동 배포 |

```json
"com.tjdtjq5.suparun": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.suparun#suparun/v0.3.6"
```

> 의존: `editor-toolkit >= 1.1.0`

---

## 의존성 관계

```
editor-toolkit (독립)
  ├── cicd
  ├── ugs-manager
  ├── ui-framework (+ DOTween)
  ├── addrx (+ Addressables)
  ├── suparun
  └── claude
```

## 요구사항

- Unity **6000.1** 이상
- UGS Manager: `ugs` CLI 설치 필요 (`npm install -g ugs`)
- UI Toolkit: DOTween + **DOTween ASMDEF 생성 필수**
- AddrX: `com.unity.addressables >= 2.2.2`

## 트러블슈팅

| 문제 | 해결 |
|------|------|
| `Tjdtjq5.UIFramework` 네임스페이스 못 찾음 | `editor-toolkit >= 1.1.0` 설치 확인 |
| `DOFade`, `DOAnchorPos` 컴파일 에러 | DOTween Utility Panel에서 ASMDEF 생성 |
| `will not be compiled, no scripts` 경고 | 무시 가능 (빈 asmdef) |
| UGS CLI 명령 실패 | `ugs status`로 로그인 상태 확인 |
| AddrX Manager 윈도우 크래시 | `v0.1.1`로 업데이트 (첫 설치 시 null 가드 수정됨) |
