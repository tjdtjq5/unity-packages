# Changelog

## [4.0.0] - 2026-06-14

### Breaking Changes
- **UIStateBinder 전면 교체** — 소비 프로젝트에서 발전시킨 버전으로 승격. public API 비호환:
  - `BindingFeatures` 재정의: `Exclusive` 제거 + `Tween`(scale·color) → scale 전용으로 축소. `Text`/`Sprite`/`Alpha` feature 신규
  - **Enter/Exit 양방향 바인딩** 도입 — 각 feature가 진입·이탈 배열을 분리 보유
  - `AnimatorParamType`: `Int`/`Float` 제거 → `Play`(클립 직접 재생) 추가
  - `_initialState` / `_exclusivePool` SerializeField 제거
  - `SetDefaultState()` / `ResetToInitial()` API 추가
  - 기존 씬/프리팹의 UIStateBinder 직렬화 데이터는 재설정 필요

### Added
- Text(`TMP_Text`) / Sprite(`Image`) / Alpha(`CanvasGroup`) 독립 feature
- Enter/Exit 양방향 — 상태 이탈 시 별도 연출 가능 (`exit*` 배열)
- Animator `Play` 파라미터 타입 (`Animator.Play` 직접 호출)
- 커스텀 에디터: 드래그앤드롭 리오더 + all-exit 합산 프리뷰(씬 스냅샷 자동 복원)

### Changed
- Tween: DOTweenAnimation(Pro 컴포넌트) 의존 제거 → **LitMotion scale 트윈** 내장 (`TweenConfig`: duration/ease/delay/useUnscaledTime). v3 트윈 철학(zero-alloc) 유지
- 네임스페이스 동일: `Tjdtjq5.UIFramework`

### Migration Notes
- 기존 UIStateBinder를 쓰던 prefab/scene은 BindingFeatures 재설정 필요
- `Exclusive` 사용처 → `Objects`의 activate/deactivate 조합으로 대체
- color 트윈이 필요하면 Visual feature 즉시 적용 + 별도 처리 (이번 버전 Tween은 scale 전용)

## [3.5.0] - 2026-05-02

### Added
- **Unity-UI-Extensions Tier 1 컴포넌트 4종 흡수** (BSD-3 라이선스, 우리 namespace로 이식)
  - `Components/Accordion/Accordion.cs` + `AccordionElement.cs` — 펼침/접힘 컨테이너 (Toggle 상속, LitMotion height tween)
  - `Components/FlowLayoutGroup.cs` — 가변폭 자동 wrap LayoutGroup (태그/뱃지/칩 표시)
  - `Components/Tooltip/Tooltip.cs` + `TooltipTrigger.cs` — 호버/long-press 툴팁 (Singleton 제거 + 모바일 long-press + ScreenSpaceOverlay/Camera 자동 보정)
  - `Components/SegmentedControl/SegmentedControl.cs` + `Segment.cs` — iOS 스타일 토글 버튼 묶음 (단순화: ColorTint만 지원)

### 우리 스타일 적응
- DOTween/LeanTween/FloatTween → **LitMotion**
- Coroutine → **UniTask** (TooltipTrigger long-press)
- Singleton (`ToolTip.Instance`) 제거 → 명시적 SerializeField wire
- TMP_Text 권장 (Unity 6 정합성)
- BSD-3 라이선스 표기 (각 파일 상단)

### 단순화
- SegmentedControl: Sprite cutting (8-slice 자동 잘라내기) 제거 — 디자이너가 좌/우 sprite 직접 준비
- SegmentedControl: SpriteSwap/Animation transition 제거, ColorTint만 지원
- Tooltip: Worldspace 미지원 (ScreenSpaceOverlay/Camera만)

### 사용 예
```csharp
// Accordion
[RequireComponent(VerticalLayoutGroup, ContentSizeFitter, ToggleGroup)]
GameObject + Accordion script
└── AccordionElement (Toggle 상속) ×N

// FlowLayoutGroup
GameObject + FlowLayoutGroup script
└── 자식들 자동 wrap

// Tooltip
Canvas
├── Tooltip (prefab, CanvasGroup + Image + TMP_Text 자식)
└── Button + TooltipTrigger (_tooltip slot에 위 Tooltip 와이어, _text 입력)

// SegmentedControl
GameObject + SegmentedControl script
├── Button + Segment (자동 index 부여)
├── Button + Segment
└── Button + Segment
```

### 의존성
변경 없음.

## [3.4.0] - 2026-05-02

### Added
- **Default Assets** (`Runtime/DefaultAssets/`) — out-of-box 사용 가능한 자산 9개
  - `DefaultModalBackdrop.prefab` — 검정 50% 알파 + click-to-close + Fade In/Out 자동 wire
  - `Transitions/DefaultFadeIn.asset` / `DefaultFadeOut.asset` — alpha 페이드 (0.3s, OutQuad/InQuad)
  - `Transitions/DefaultScaleIn.asset` / `DefaultScaleOut.asset` — scale + 페이드 (OutBack/InBack, fromScale=0.8)
  - `Transitions/DefaultSlideInRight.asset` / `DefaultSlideOutLeft.asset` — Page push 표준 navigation
  - `Transitions/DefaultSlideInLeft.asset` / `DefaultSlideOutRight.asset` — Page pop 역방향
- **Sub-feature Feature.md** 5개 — Screens 시스템 탐색 효율 향상
  - `Runtime/Screens/Feature.md` — 시스템 전체 개요
  - `Runtime/Screens/Core/Feature.md` — IScreenContainer + Transitions
  - `Runtime/Screens/Sheet/Feature.md` — 5-step lifecycle
  - `Runtime/Screens/Page/Feature.md` — 8-step + history stack
  - `Runtime/Screens/Modal/Feature.md` — 8-step + backdrop strategy

### 사용 가치
- 새 Sheet/Page/Modal 만들 때 default 트랜지션 SO를 드래그-드롭만 하면 즉시 동작
- ModalContainer의 _backdropPrefab 슬롯에 DefaultModalBackdrop 드래그하면 표준 모달 동작 완성
- 사용자가 default 에셋을 열어 매개변수 (duration, ease) 학습 가능
- 자기 커스텀 트랜지션은 기존 CreateAssetMenu로 만들어 default 대체

### 의존성
변경 없음.

## [3.3.0] - 2026-05-02

### Added
- **Screen 시스템 Phase 3: Modal** — backdrop 기반 다중 stacking 모달
  - `Tjdtjq5.UIFramework.Screens.Modal`
    - `IModalLifecycle` — 8단계 lifecycle (Page와 동일 시그니처, 의미는 다름)
    - `Modal` — MB 베이스. 2 transition slot (enter/exit). PushExit/PopEnter는 lifecycle hook만
    - `ModalContainer` — push/pop/popTo/popAll. BackdropStrategy + AddrX + UniTask + R3 + VContainer
    - `ModalEvents` — R3 Observable 8종
    - `ModalBackdrop` — 자체 enter/exit 애니메이션 + click-to-close 옵션
    - `ModalBackdropStrategy` — enum (4 종)
  - `Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers` (internal)
    - `IModalBackdropHandler` — strategy 추상화
    - `ModalBackdropHandlerFactory` — strategy → handler 생성
    - `NoBackdropHandler` — backdrop prefab null 시 fallback
    - `GeneratePerModalBackdropHandler` — 기본. modal마다 backdrop 생성
    - `OnlyFirstBackdropHandler` — 첫 modal만 backdrop
    - `ChangeOrderBackdropHandler` — 단일 backdrop 재사용 + sibling 이동

### API
- `PushAsync(addressableKey, playAnimation, modalId, ct) → string modalId` — modal push (항상 stack)
- `PopAsync(playAnimation, popCount, ct)` — popCount만큼 동시 pop (모든 exit modal이 병렬 애니메이션)
- `PopToAsync(destinationModalId, ...)` — 특정 modal까지 pop
- `PopAllAsync(...)` — 모든 modal pop (clean)
- `TopModal / TopModalId / StackModalIds / CanPop` — stack 상태 조회

### 핵심 설계
- **항상 stack**: Modal은 push 시 항상 stack에 쌓임 (Page의 `stack: false` 옵션 없음)
- **이전 modal 가시 유지**: Modal이 background로 갈 때 SetActive(true) 유지. Backdrop이 raycast 차단으로 입력 막음
- **PushExit/PopEnter 시각 변화 없음**: lifecycle hook만 호출. 따라서 SerializeField slot 2개로 충분 (enter/exit)
- **Backdrop strategy pattern**: 4가지 전략 inspector 선택 가능. prefab null이면 NoBackdrop fallback

### Source
- UnityScreenNavigator의 ModalContainer/ModalBackdropHandler 4-strategy 패턴 흡수
- Coroutine → UniTask, Resources.Load → AddrX, CallbackReceiver → R3 Observable
- Loxodon의 Activation/Passivation 명시 분리는 도입 안 함 (Backdrop의 raycast 차단으로 충분)

### 향후 확장 가능 (도입 안 함)
- ModalQueue (Toast/공지 자동 순차 표시) — Loxodon QUEUED_POPUP 패턴, 필요 시 v3.4
- WindowType 분리 (DIALOG/PROGRESS) — 우리는 Page/Modal/Sheet로 이미 분리

## [3.2.0] - 2026-05-02

### Added
- **Screen 시스템 Phase 2: Page** — history stack 기반 시퀀스 화면
  - `Tjdtjq5.UIFramework.Screens.Page`
    - `IPageLifecycle` — 8단계 lifecycle (Initialize/WillPushEnter/DidPushEnter/WillPushExit/DidPushExit/WillPopEnter/DidPopEnter/WillPopExit/DidPopExit/Cleanup)
    - `Page` — MB 베이스 (4종 transition slot: pushEnter/pushExit/popEnter/popExit). Partner page 인지하는 transition 지원
    - `PageContainer` — push/pop/popTo/popToRoot. AddrX 로드 + UniTask + R3 events + VContainer DI + history stack
    - `PageEvents` — R3 Observable 8종 (push/pop × enter/exit × will/did)

### API
- `PushAsync(addressableKey, playAnimation, stack, pageId, ct) → string pageId` — 새 페이지 push
- `PopAsync(playAnimation, popCount, ct)` — 1개 또는 여러 개 한 번에 pop
- `PopToAsync(destinationPageId, ...)` — 특정 페이지까지 pop
- `PopToRootAsync(...)` — root까지 pop
- `CurrentPage / CurrentPageId / StackPageIds / CanPop` — stack 상태 조회

### Source
- UnityScreenNavigator의 PageContainer 패턴 흡수 + ScreenManager의 popCount 인사이트
- Coroutine → UniTask, Resources.Load → AddrX, Static cache → VContainer DI, CallbackReceiver → R3 Observable

### Phase 3 Modal로 이월된 패턴 (학습 결과)
- Activation/Passivation 분리 (Loxodon) — Modal 다중 stacking 시 진짜 가치
- WindowQueue / QUEUED_POPUP — Toast/공지 자동 순차 표시
- HideOnForegroundLost (deVoid) — Modal 활성화 시 메인 화면 동작 옵션

## [3.1.0] - 2026-05-02

### Added
- **Screen 시스템 도입 (Phase 1: Sheet)** — Page/Modal/Sheet 흡수 첫 단계
  - `Tjdtjq5.UIFramework.Screens.Core`
    - `IScreenContainer` — Page/Modal/Sheet 공통 인터페이스
    - `ITransitionAnimation` — 전환 애니메이션 추상화
    - `TransitionAnimationObject` — ScriptableObject 베이스
    - `FadeTransition` / `ScaleTransition` / `SlideTransition` — LitMotion 기반 preset 3종 (CreateAssetMenu 등록)
  - `Tjdtjq5.UIFramework.Screens.Sheet`
    - `ISheetLifecycle` — 5단계 lifecycle (Initialize/WillEnter/DidEnter/WillExit/DidExit/Cleanup)
    - `Sheet` — MB 베이스 (사용처가 상속)
    - `SheetContainer` — 등록·표시·숨김 관리 (AddrX 로드 + UniTask async + R3 events + VContainer DI)
    - `SheetEvents` — R3 Observable로 lifecycle 노출 (OnWillEnter/OnDidEnter/OnWillExit/OnDidExit)

### Dependencies
- 추가: `com.tjdtjq5.addrx` ^0.1.7, `com.cysharp.unitask` ^2.5.0, `com.cysharp.r3` ^1.0.0, `jp.hadashikick.vcontainer` ^1.17.0
- asmdef references: Unity.Addressables, Unity.ResourceManager, Tjdtjq5.AddrX.Runtime, UniTask, R3, R3.Unity, VContainer, VContainer.Unity

### Source
- UnityScreenNavigator의 Page/Modal/Sheet 패턴을 흡수하되 우리 스택(R3/UniTask/AddrX/VContainer)으로 재작성
- Resources.Load 의존 제거, Coroutine → UniTask, Static cache → VContainer DI, CallbackReceiver → R3 Observable

## [3.0.0] - 2026-05-02

### Breaking Changes
- 트윈 라이브러리 교체: DOTween → LitMotion (zero allocation, Burst+JobSystem)
- asmdef references: `DOTween.Modules` 제거 → `LitMotion`, `LitMotion.Extensions` 추가
- package.json dependencies: `com.annulusgames.lit-motion` ^2.0.0 추가

### Migration Notes
- 사용처 변경 없음: ButtonClickEffect / NumberCounter / SafeAreaFitter / UIFlyEffect / UIShake / UIStateBinder / UITutorialMask 의 public API는 동일
- 단, Unity Editor에서 `[SerializeField] DG.Tweening.Ease` 등이 SerializeField로 노출된 적 없으므로 Inspector 깨짐 없음 (내부 Tweener만 변경)

### Changed
- ButtonClickEffect: `DOScale` → `LMotion.Create(...).BindToLocalScale(...)`
- NumberCounter: `DOTween.To` → `LMotion.Create(float, float).Bind(setter)`
- UIShake: `DOShakeAnchorPos` → `LMotion.Shake.Create(Vector2).BindToAnchoredPosition(...)`
- UITutorialMask: `CanvasGroup.DOFade` → `LMotion.Create(0,1,...).BindToAlpha(canvasGroup)`
- UIFlyEffect: `DOTween.To(0,1)` 베지어 setter 패턴 → `LMotion.Create(0f, 1f).Bind(setter)` (1:1 변환)
- UIStateBinder: `Tween.OnComplete` → 0-cost callback motion 패턴, `vt.target.DOColor` → `LMotion.Create(...).BindToColor(graphic)`
- RecycleScrollView: `DOTween.To` (스냅) → `LMotion.Create(from, to, duration).Bind(setter)`

### Why
- 모바일에서 동시 트윈 수 증가 시 GC 스파이크 제거
- Cysharp 스택(R3/UniTask)과 zero-allocation 철학 정합성
- DOTween Pro 등 추가 비용 없이 동등한 기능

## [2.0.1] - 2026-03-29

### Fixed
- CHANGELOG.md.meta 누락 수정 (immutable folder 경고 해결)

## [2.0.0] - 2026-03-29

### Breaking Changes
- Popup 시스템 전체 제거 (UIPopup, UIManager, UITransition, UIDialog, UILifetimeScope)
- VContainer 의존성 제거
- UIFollowWorld, UIProgressBar, UITabGroup, UIToast 제거

### Added
- RecycleScrollView — 무한 재사용 스크롤 (Vertical/Horizontal, Grid, Cell/Page 스냅)
- IScrollCell 인터페이스

### Changed
- 패키지 정체성 변경: "UI Framework" → "UI Toolkit" (순수 도구 모음)
- asmdef에서 VContainer 참조 제거
- DI/, Popup/ 폴더 제거

## [1.0.1] - 2026-03-22

### Added
- UIStateBinder, ButtonClickEffect, NumberCounter, SafeAreaFitter
- UIFlyEffect, UIFollowWorld, UIProgressBar, UIShake
- UITabGroup, UIToast, UITutorialMask
- UIPopup, UIManager, UITransition, UIDialog
- UILifetimeScope (VContainer DI)
- UIStateBinderEditor (커스텀 인스펙터)
