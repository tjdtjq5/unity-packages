# UI Toolkit

범용 UI 도구 모음 패키지 — 독립적으로 사용 가능한 UI 컴포넌트 제공.

## 설치

```json
"com.tjdtjq5.ui-framework": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ui-framework#ui-framework/v3.5.0"
```

## 의존성

- Unity 6000.1+
- [LitMotion](https://github.com/AnnulusGames/LitMotion) v2.0.0+ (트윈 엔진)
- [UniTask](https://github.com/Cysharp/UniTask) v2.5+ (async)
- [R3](https://github.com/Cysharp/R3) v1.0+ (Observable)
- [VContainer](https://github.com/hadashiA/VContainer) v1.17+ (DI)
- [com.tjdtjq5.addrx](https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.addrx) v0.1.7+ (Addressable)
- [com.tjdtjq5.editor-toolkit](https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-toolkit) v1.1.0+
- TextMeshPro

## 컴포넌트

| 컴포넌트 | 용도 |
|---------|------|
| **UIStateBinder** | enum/string 상태 전환 바인딩 (Exclusive/Animator/Visual/Tween) |
| **RecycleScrollView** | 무한 재사용 스크롤 (Vertical/Horizontal, Grid, 스냅) |
| **UIFlyEffect** | 베지어 곡선 플라이 애니메이션 (코인 획득 등) |
| **ButtonClickEffect** | 버튼 누르기 스케일 펀치 |
| **NumberCounter** | 숫자 카운팅 트윈 (TMP) |
| **SafeAreaFitter** | 모바일 노치/SafeArea 자동 적응 |
| **UIShake** | RectTransform 흔들림 효과 |
| **UITutorialMask** | 스텐실 기반 튜토리얼 스포트라이트 |
| **FlowLayoutGroup** (v3.5) | 가변폭 자동 wrap LayoutGroup (태그/뱃지/칩) |
| **Accordion + AccordionElement** (v3.5) | 펼침/접힘 컨테이너 (LitMotion height tween) |
| **Tooltip + TooltipTrigger** (v3.5) | 호버/long-press 툴팁 (모바일 대응) |
| **SegmentedControl + Segment** (v3.5) | iOS 스타일 토글 버튼 묶음 |

## Screen 시스템 (v3.1+)

탭/Modal/Page 류의 화면 전환 관리.

### Sheet (탭형 — v3.1+)

| API | 용도 |
|-----|------|
| **SheetContainer.RegisterAsync(key)** | Addressable 프리팹 로드 + 등록 → sheetId 반환 |
| **SheetContainer.ShowAsync(sheetId)** | 지정 Sheet 표시 (이전 active sheet 자동 숨김) |
| **SheetContainer.HideAsync()** | 현재 활성 Sheet 숨김 |
| **SheetContainer.Events.OnDidEnter** | R3 Observable<Sheet> — 전환 완료 관찰 |
| **Sheet (abstract)** | Initialize/WillEnter/DidEnter/WillExit/DidExit/Cleanup override |

### Page (history stack — v3.2+)

| API | 용도 |
|-----|------|
| **PageContainer.PushAsync(key, ...)** | 새 페이지 push → pageId 반환. `stack: false`면 이전 페이지 destroy (replace) |
| **PageContainer.PopAsync(playAnimation, popCount)** | 1개 또는 여러 개 한 번에 pop |
| **PageContainer.PopToAsync(pageId)** | 특정 페이지까지 pop |
| **PageContainer.PopToRootAsync()** | root까지 pop |
| **PageContainer.CurrentPage / StackPageIds** | stack 상태 |
| **Page (abstract)** | 8-step lifecycle (push/pop × enter/exit × will/did) + 4 transition slot |

### Modal (다중 stacking — v3.3+)

| API | 용도 |
|-----|------|
| **ModalContainer.PushAsync(key, ...)** | modal push → modalId 반환. 항상 stack |
| **ModalContainer.PopAsync(playAnimation, popCount)** | 동시 pop (모든 exit modal 병렬 애니메이션) |
| **ModalContainer.PopToAsync(modalId)** | 특정 modal까지 pop |
| **ModalContainer.PopAllAsync()** | 모든 modal pop (clean) |
| **ModalContainer.TopModal / StackModalIds** | stack 상태 |
| **Modal (abstract)** | 8-step lifecycle + 2 transition slot (enter/exit) |
| **ModalBackdrop** | 자체 enter/exit + `_closeModalWhenClicked` 옵션 |

**Backdrop 전략** (`ModalBackdropStrategy` enum):
- `GeneratePerModal` (기본): 모달마다 backdrop 새로 생성
- `OnlyFirstBackdrop`: 첫 모달만 backdrop, 추가 모달은 overlay
- `ChangeOrderBeforeAnimation` / `ChangeOrderAfterAnimation`: 단일 backdrop 재사용

### 사용 예 — Sheet (탭)

```csharp
[Inject] private SheetContainer _sheetContainer;

async UniTask SetupTabsAsync()
{
    var homeId = await _sheetContainer.RegisterAsync("UI/Sheets/HomeSheet");
    var shopId = await _sheetContainer.RegisterAsync("UI/Sheets/ShopSheet");

    await _sheetContainer.ShowAsync(homeId);  // 처음에는 Home

    // 탭 클릭 시
    await _sheetContainer.ShowAsync(shopId);

    _sheetContainer.Events.OnDidEnter
        .Subscribe(sheet => Debug.Log($"Entered: {sheet.name}"))
        .AddTo(this);
}
```

### 사용 예 — Page (시퀀스)

```csharp
[Inject] private PageContainer _pageContainer;

async UniTask GoToProductDetailAsync(int productId)
{
    // 새 페이지 push
    var detailPageId = await _pageContainer.PushAsync("UI/Pages/ProductDetailPage");

    // 페이지에 데이터 전달 (onLoad 패턴 또는 직접 캐스팅)
    var page = (ProductDetailPage)_pageContainer.Pages[detailPageId];
    page.SetProduct(productId);
}

async UniTask BackButtonAsync()
{
    if (_pageContainer.CanPop)
        await _pageContainer.PopAsync();
}

async UniTask SignOutAsync()
{
    // 모든 페이지 닫고 로그인 페이지로 (replace)
    await _pageContainer.PopToRootAsync();
    await _pageContainer.PushAsync("UI/Pages/LoginPage", stack: false);
}
```

### 사용 예 — Modal (다중 stacking)

```csharp
[Inject] private ModalContainer _modalContainer;

async UniTask ShowSettingsAsync()
{
    var settingsId = await _modalContainer.PushAsync("UI/Modals/SettingsModal");
}

async UniTask ConfirmDeleteAsync()
{
    // 다중 stacking — settings 모달 위에 confirm 모달
    var confirmId = await _modalContainer.PushAsync("UI/Modals/ConfirmDialog");
    // backdrop이 자동으로 settings 모달의 입력을 차단

    // 닫기 버튼 클릭 시
    await _modalContainer.PopAsync();  // confirm 닫음, settings 다시 활성
}

async UniTask CloseAllModalsAsync()
{
    await _modalContainer.PopAllAsync();
}
```

### Default 자산 (out-of-box) — v3.4+

`Runtime/DefaultAssets/`에 미리 만들어진 자산. 드래그-드롭으로 즉시 사용:

**ModalContainer의 `_backdropPrefab` 슬롯**:
- `DefaultModalBackdrop.prefab` — 검정 50% 알파 + click-to-close + Fade

**Sheet/Page/Modal의 transition 슬롯**:
- `Transitions/DefaultFadeIn.asset` / `DefaultFadeOut.asset` — 단순 페이드
- `Transitions/DefaultScaleIn.asset` / `DefaultScaleOut.asset` — popup용 (OutBack)
- `Transitions/DefaultSlideInRight.asset` / `DefaultSlideOutLeft.asset` — Page push (표준 좌→우)
- `Transitions/DefaultSlideInLeft.asset` / `DefaultSlideOutRight.asset` — Page pop (반대 방향)

Page의 4개 슬롯 권장 조합 (표준 navigation):
```
_pushEnterAnimation = DefaultSlideInRight
_pushExitAnimation  = DefaultSlideOutLeft
_popEnterAnimation  = DefaultSlideInLeft
_popExitAnimation   = DefaultSlideOutRight
```

### 커스텀 트랜지션 생성

CreateAssetMenu로 자기 SO 생성:
- `Tjdtjq5/UIFramework/Transition/Fade` — alpha 페이드
- `Tjdtjq5/UIFramework/Transition/Scale` — 스케일 + 페이드
- `Tjdtjq5/UIFramework/Transition/Slide` — anchoredPosition 슬라이드 (4방향)

duration / ease / 방향 / fromScale 등 자유 조정.

## 사용법

### RecycleScrollView

```csharp
// 1. 셀 프리팹에 IScrollCell 구현
public class MyCell : MonoBehaviour, IScrollCell
{
    public void OnUpdateCell(int index) { /* 데이터 바인딩 */ }
    public void OnRecycled() { /* 정리 */ }
}

// 2. RecycleScrollView 초기화
recycleScrollView.Init(totalCount: 1000);

// 3. 데이터 변경 시
recycleScrollView.UpdateCount(newCount);
recycleScrollView.RefreshAll();
recycleScrollView.ScrollTo(index);
```

### UIStateBinder

```csharp
// Inspector에서 상태별 Visual/Animator/Tween 타겟 설정
[SerializeField] private UIStateBinder _binder;

_binder.SetState(MyEnum.Active);  // GC-free enum 상태 전환
```

## v3.5.0

- **Unity-UI-Extensions Tier 1 흡수** (BSD-3, 우리 namespace로 이식)
  - Accordion + AccordionElement — 펼침/접힘 (LitMotion height tween)
  - FlowLayoutGroup — 가변폭 자동 wrap
  - Tooltip + TooltipTrigger — 호버/long-press (Singleton 제거 + 모바일 long-press)
  - SegmentedControl + Segment — iOS 스타일 토글 묶음 (단순화: ColorTint만)
- 의존성 변경 없음

## v3.4.0

- **Default 자산 추가** — `Runtime/DefaultAssets/` 9개 (ModalBackdrop 1 + Transitions 8)
- 새 Sheet/Page/Modal 만들 때 드래그-드롭으로 즉시 동작
- Screens 하위 Feature.md 5개 추가 (탐색 효율)
- 의존성 변경 없음

## v3.3.0

- **Modal 시스템 추가** — Phase 3 (backdrop strategy, 다중 stacking, popAll)
- Screen 시스템 (Page/Modal/Sheet) 완성
- 의존성 변경 없음

## v3.2.0

- **Page 시스템 추가** — Phase 2 (history stack, push/pop/popTo/popToRoot)
- 의존성 변경 없음 (v3.1과 동일)

## v3.1.0

- **Screen 시스템 도입 (Sheet)** — Page/Modal/Sheet 흡수의 Phase 1
- 의존성 4개 추가 (AddrX, UniTask, R3, VContainer) — Components 사용처에는 영향 없음 (asmdef 같지만 코드 사용 안 하면 OK)

## v3.0.0 Breaking Changes

- 트윈 라이브러리: DOTween → **LitMotion** (zero allocation, Burst+JobSystem)
- public API 동일 — 사용처 코드 변경 없음
- 의존성만 변경: `DOTween.Modules` 제거 → `com.annulusgames.lit-motion` 추가

## v2.0.0 Breaking Changes

- Popup 시스템 제거 (UIPopup, UIManager, UITransition, UIDialog)
- VContainer 의존성 제거
- Popup을 쓰던 프로젝트는 로컬로 이관 필요
