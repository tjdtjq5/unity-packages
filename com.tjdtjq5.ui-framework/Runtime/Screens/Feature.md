# Screens

## 상태
stable (v3.3+)

## 용도
Page / Modal / Sheet 3종 화면 시스템. 각자 다른 UX 패턴을 추상화한다.

| 종류 | 패턴 | 사용 케이스 |
|------|------|------------|
| **Page** | history stack (push/pop) | 화면 시퀀스 — Title → Lobby → InGame, ProductList → ProductDetail |
| **Modal** | overlay stack + backdrop | 다이얼로그, 설정 창, 확인 모달 (다중 stacking 가능) |
| **Sheet** | 단일 active (history 없음) | 탭 UI, 메인 메뉴 카테고리 전환 |

## 의존성 (패키지에서 상속)
- com.tjdtjq5.addrx — 프리팹 로드
- com.cysharp.unitask — async lifecycle
- com.cysharp.r3 — Observable lifecycle 이벤트
- jp.hadashikick.vcontainer — 선택적 DI (Sheet/Page/Modal에 의존성 자동 주입)
- com.annulusgames.lit-motion — 트랜지션 애니메이션

## 구조
```
Screens/
├── Core/                    # 공통 추상화 (IScreenContainer, ITransitionAnimation)
├── Sheet/                   # 5-step lifecycle, 단일 active
├── Page/                    # 8-step lifecycle, history stack
└── Modal/                   # 8-step lifecycle, 다중 stacking + backdrop
```

각 하위 폴더에 자체 Feature.md.

## API 흐름
1. **Container 배치**: 씬에 SheetContainer/PageContainer/ModalContainer GameObject 추가
2. **VContainer 등록 (선택)**: `builder.RegisterComponent(_container).AsSelf()` — DI 주입 시
3. **사용처에서 주입**: `[Inject] PageContainer _pageContainer`
4. **Push/Show**: `await _pageContainer.PushAsync("UI/Pages/MainPage")`
5. **Pop/Hide**: `await _pageContainer.PopAsync()`
6. **이벤트 구독**: `_pageContainer.Events.OnDidPushEnter.Subscribe(...).AddTo(this)`

## Default 자산 (DefaultAssets/)
- `DefaultModalBackdrop.prefab` — 검정 50% 알파 + click-to-close + Fade 자동 wire
- `Transitions/Default{Fade|Scale|Slide}{In|Out}*.asset` — 8개 preset

사용처: Inspector에서 Sheet/Page/Modal/ModalContainer의 transition slot에 드래그-드롭.

## 주의사항
- 모든 Container는 RectTransform + CanvasGroup 자식. 보통 Canvas 아래 풀스크린 배치.
- Sheet: 등록한 sheet은 unregister 전까지 메모리 보존 (재사용 모델).
- Page: stack 끝에 push, 가장 위가 활성. Pop 시 이전 page로 복귀.
- Modal: 항상 stack에 쌓임. 이전 modal은 visible 유지, backdrop이 raycast 차단.
