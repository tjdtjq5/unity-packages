# Screens.Page

## 상태
stable (v3.2+)

## 용도
**history stack 기반 시퀀스 화면** 관리. push/pop 패턴. push/pop 시 lifecycle이 구분되어 partner page 인지하는 시너지 transition 가능.

## 의존성 (Core 외)
- AddrX — `PushAsync(addressableKey)`로 프리팹 로드 (push마다 새 인스턴스)
- UniTask — 8-step lifecycle async
- R3 — `Events`로 8종 lifecycle 노출

## 구조
```
Page/
├── IPageLifecycle.cs        # 8-step + Initialize + Cleanup
├── Page.cs                  # MB 베이스 (4 transition slot)
├── PageContainer.cs         # push/pop/popTo/popToRoot
└── PageEvents.cs            # R3 Observable 8종
```

## API

### PageContainer
- `UniTask<string> PushAsync(string addressableKey, bool playAnimation, bool stack, string pageId, CancellationToken)` — push.
  - `stack: false`면 이전 page를 history에 보존하지 않고 destroy (replace 패턴)
  - `pageId` 명시 안 하면 GUID 자동 생성
- `UniTask PopAsync(bool playAnimation, int popCount, CancellationToken)` — popCount만큼 pop. top page만 transition.
- `UniTask PopToAsync(string destinationPageId, ...)` — 특정 pageId까지 pop.
- `UniTask PopToRootAsync(...)` — root까지 pop (가장 처음 push한 page만 남김).
- `string CurrentPageId / Page CurrentPage` — 현재 활성 page.
- `IReadOnlyList<string> StackPageIds` — stack 순서 (마지막 = 활성).
- `bool CanPop` — `_orderedPageIds.Count >= 2`.
- `PageEvents Events`.

### Page (abstract)
4 transition slot (모두 SerializeField):
- `_pushEnterAnimation` — push 시 새 page 진입
- `_pushExitAnimation` — push 시 이전 page 나감
- `_popEnterAnimation` — pop 시 이전 page 복귀
- `_popExitAnimation` — pop 시 현재 page 나감

8-step lifecycle override:
- `UniTask Initialize()` — 첫 로드 시 1회
- `UniTask WillPushEnter()` / `void DidPushEnter()`
- `UniTask WillPushExit()` / `void DidPushExit()`
- `UniTask WillPopEnter()` / `void DidPopEnter()`
- `UniTask WillPopExit()` / `void DidPopExit()`
- `UniTask Cleanup()` — page release 직전

### PageEvents (R3)
8개 Observable: OnWill/Did × Push/Pop × Enter/Exit.

## 사용 패턴
```csharp
[Inject] PageContainer _container;

async UniTask GoToProductDetailAsync(int productId)
{
    var detailId = await _container.PushAsync("UI/Pages/ProductDetailPage");
    var page = (ProductDetailPage)_container.Pages[detailId];
    page.SetProduct(productId);
}

async UniTask BackButtonAsync()
{
    if (_container.CanPop) await _container.PopAsync();
}

async UniTask SignOutAsync()
{
    await _container.PopToRootAsync();
    await _container.PushAsync("UI/Pages/LoginPage", stack: false);
}
```

## Default 자산 활용
권장 transition 조합 (4 slot 모두):
- pushEnterAnimation = `DefaultSlideInRight`
- pushExitAnimation = `DefaultSlideOutLeft`
- popEnterAnimation = `DefaultSlideInLeft`
- popExitAnimation = `DefaultSlideOutRight`

→ 표준 좌→우 navigation 효과 (다음 페이지가 오른쪽에서 들어옴, 뒤로 가기는 반대).

## 주의사항
- push마다 새 prefab 인스턴스 생성 (Sheet과 다름). 메모리 관리 주의.
- popCount > 1 시 가장 위 page만 transition 애니메이션 재생, 중간 page는 cleanup만 호출.
- `stack: false`로 push한 후 다음 push 시 이전 page 자동 destroy.
