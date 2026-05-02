# Screens.Sheet

## 상태
stable (v3.1+)

## 용도
탭 UI 같은 **단일 active 화면** 관리. history 없음. 등록된 sheet 중 하나만 보이고 나머지는 비활성.

## 의존성 (Core 외)
- AddrX — `RegisterAsync(addressableKey)`로 프리팹 로드
- UniTask — 5-step lifecycle async
- R3 — `Events`로 lifecycle 노출

## 구조
```
Sheet/
├── ISheetLifecycle.cs       # 5-step (Initialize/WillEnter/DidEnter/WillExit/DidExit/Cleanup)
├── Sheet.cs                 # MB 베이스 (사용처 상속)
├── SheetContainer.cs        # 등록·표시·숨김 관리
└── SheetEvents.cs           # R3 Observable
```

## API

### SheetContainer
- `UniTask<int> RegisterAsync(string addressableKey, CancellationToken)` — Addressable 프리팹을 로드해 등록. ID 반환.
- `UniTask UnregisterAsync(int sheetId)` — 등록 해제 + GameObject 파괴.
- `UniTask ShowAsync(int sheetId, bool playAnimation, CancellationToken)` — 표시 (이전 active sheet 자동 숨김).
- `UniTask HideAsync(bool playAnimation, CancellationToken)` — 현재 active sheet 숨김.
- `int ActiveSheetId` — 현재 활성 sheet ID. 없으면 -1.
- `IReadOnlyDictionary<int, Sheet> Sheets` — 등록된 모든 sheet.
- `SheetEvents Events` — R3 lifecycle 관찰.

### Sheet (abstract)
사용처가 상속해 lifecycle override:
- `UniTask Initialize()` — 등록 시 1회
- `UniTask WillEnter()` / `void DidEnter()` — 표시 직전/직후
- `UniTask WillExit()` / `void DidExit()` — 숨김 직전/직후
- `UniTask Cleanup()` — unregister 또는 컨테이너 파괴 시

### SheetEvents (R3)
- `Observable<Sheet> OnWillEnter / OnDidEnter / OnWillExit / OnDidExit`

## 사용 패턴
```csharp
[Inject] SheetContainer _container;

async UniTask SetupAsync()
{
    var homeId = await _container.RegisterAsync("UI/Sheets/HomeSheet");
    var shopId = await _container.RegisterAsync("UI/Sheets/ShopSheet");

    await _container.ShowAsync(homeId);  // 처음에는 Home

    // 탭 클릭 시
    await _container.ShowAsync(shopId);
}
```

## 주의사항
- **재사용 모델**: 등록된 sheet은 unregister/컨테이너 destroy 전까지 메모리 보존. Initialize는 1회만 호출.
- Sheet은 단일 active — 동시에 둘 이상 보일 수 없음 (다중 stacking이 필요하면 Modal 사용)
- ShowAsync는 같은 sheetId 호출 시 no-op (이미 표시 중)
