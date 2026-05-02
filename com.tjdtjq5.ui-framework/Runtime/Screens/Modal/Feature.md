# Screens.Modal

## 상태
stable (v3.3+)

## 용도
**다중 stacking 모달** 관리. backdrop으로 raycast 차단. Page와 다르게 이전 modal이 시각적으로 보이는 상태 유지.

## 의존성 (Core 외)
- AddrX — `PushAsync(addressableKey)`로 프리팹 로드
- UniTask — 8-step lifecycle async (Page와 동일 시그니처)
- R3 — `Events`로 lifecycle 노출

## 구조
```
Modal/
├── IModalLifecycle.cs           # 8-step (Page와 동일 시그니처)
├── Modal.cs                     # MB 베이스 (2 transition slot)
├── ModalContainer.cs            # push/pop/popTo/popAll + Backdrop 통합
├── ModalEvents.cs               # R3 Observable 8종
├── ModalBackdrop.cs             # 자체 enter/exit + click-to-close
├── ModalBackdropStrategy.cs     # enum (4종 전략)
└── BackdropHandlers/            # internal — strategy 구현 5종
```

## API

### ModalContainer
- `UniTask<string> PushAsync(string addressableKey, bool playAnimation, string modalId, CancellationToken)` — modal push (항상 stack).
- `UniTask PopAsync(bool playAnimation, int popCount, CancellationToken)` — popCount만큼 동시 pop. **모든 exit modal이 병렬 애니메이션** (Page와 다름).
- `UniTask PopToAsync(string destinationModalId, ...)` — 특정 modal까지 pop.
- `UniTask PopAllAsync(...)` — 모든 modal pop (clean state).
- `string TopModalId / Modal TopModal` — 가장 위 modal.
- `IReadOnlyList<string> StackModalIds`.
- `bool CanPop` — `_orderedModalIds.Count > 0`.
- `ModalEvents Events`.

### Modal (abstract)
2 transition slot:
- `_enterAnimation` — push 시 진입 / pop 시 (no — push 시 enter만)
- `_exitAnimation` — pop 시 퇴장

**Page와 핵심 차이**: PushExit / PopEnter는 시각 변화 없이 lifecycle hook만. Modal은 background에서도 가시 유지 (backdrop이 raycast 차단).

8-step lifecycle (Page와 동일):
- Initialize / WillPushEnter / DidPushEnter / WillPushExit / DidPushExit / WillPopEnter / DidPopEnter / WillPopExit / DidPopExit / Cleanup

### ModalBackdrop (MB)
- `_enterAnimation`, `_exitAnimation` — 자체 트랜지션 (Modal과 별개)
- `_closeModalWhenClicked` — Image+Button 자동 부착, click 시 ModalContainer.Pop()

### ModalBackdropStrategy (enum, ModalContainer Inspector)
- `GeneratePerModal` (기본) — 모달마다 backdrop 새로 생성
- `OnlyFirstBackdrop` — 첫 모달만 backdrop, 추가 모달은 overlay
- `ChangeOrderBeforeAnimation` / `ChangeOrderAfterAnimation` — 단일 backdrop sibling index 이동 (메모리 효율)

prefab이 null이면 자동으로 NoBackdropHandler — 모달이 backdrop 없이 overlay.

## 사용 패턴
```csharp
[Inject] ModalContainer _container;

async UniTask ShowSettingsAsync()
{
    await _container.PushAsync("UI/Modals/SettingsModal");
}

async UniTask ConfirmDeleteAsync()
{
    // 다중 stacking — settings 위에 confirm
    await _container.PushAsync("UI/Modals/ConfirmDialog");
    // backdrop이 자동으로 settings 입력 차단
}

async UniTask CloseAllAsync() => await _container.PopAllAsync();
```

## Default 자산 활용
- ModalContainer Inspector → `_backdropPrefab` 슬롯에 `DefaultModalBackdrop.prefab` 드래그
- Modal 프리팹의 transition 슬롯:
  - `_enterAnimation` = `DefaultScaleIn` (popup 효과)
  - `_exitAnimation` = `DefaultScaleOut`
- 또는 dialog 류는 Fade 사용:
  - `_enterAnimation` = `DefaultFadeIn`
  - `_exitAnimation` = `DefaultFadeOut`

## 주의사항
- Modal은 항상 stack (Page의 `stack: false` 옵션 없음)
- PushExit / PopEnter는 lifecycle hook만 — 사용처 override해도 시각 효과 없음 (의도)
- popCount > 1 시 모든 exit modal이 동시 애니메이션 + 각자의 backdrop 처리
- backdrop click-to-close는 IsInTransition 중이면 무시 (이중 호출 방지)
