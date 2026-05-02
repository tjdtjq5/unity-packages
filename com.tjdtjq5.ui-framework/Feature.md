# UI Toolkit

## 상태
stable

## 용도
범용 UI 도구 모음 패키지 — 독립적인 UI 컴포넌트 + Screen 시스템(Page/Modal/Sheet) + 기본 자산 + Unity-UI-Extensions Tier 1 흡수 (v3.5.0)

## 의존성
- com.tjdtjq5.editor-toolkit — Inspector Attribute (SectionHeader, Required, BoxGroup 등)
- com.tjdtjq5.addrx — Addressable 에셋 로드 (Sheet 등록용)
- com.annulusgames.lit-motion — 트윈 엔진 (zero allocation, Burst+JobSystem)
- com.cysharp.unitask — async lifecycle
- com.cysharp.r3 — Observable 이벤트 노출
- jp.hadashikick.vcontainer — DI (Sheet에 의존성 자동 주입)
- TextMeshPro — 텍스트 렌더링 (NumberCounter, UITutorialMask)

## 구조

```
com.tjdtjq5.ui-framework/
├── package.json                     # v3.5.0
├── Feature.md
├── README.md
├── Runtime/
│   ├── Tjdtjq5.UIFramework.Runtime.asmdef
│   ├── Components/                          # 단순 UI 컴포넌트 (LitMotion 기반)
│   │   ├── ButtonClickEffect.cs     # 버튼 누르기 스케일 펀치 애니메이션
│   │   ├── NumberCounter.cs         # 숫자 카운팅 트윈 (TMP)
│   │   ├── SafeAreaFitter.cs        # 노치/SafeArea 자동 적응
│   │   ├── UIFlyEffect.cs           # 베지어 곡선 플라이 애니메이션
│   │   ├── UIShake.cs               # RectTransform 흔들림 효과
│   │   ├── UIStateBinder.cs         # enum/string 상태 전환 바인딩
│   │   ├── UITutorialMask.cs        # 스텐실 기반 튜토리얼 스포트라이트
│   │   ├── FlowLayoutGroup.cs       # 가변폭 자동 wrap LayoutGroup (v3.5)
│   │   ├── RecycleScroll/
│   │   │   ├── IScrollCell.cs       # 셀 인터페이스
│   │   │   └── RecycleScrollView.cs # 무한 재사용 스크롤
│   │   ├── Accordion/                       # v3.5 — UI-Extensions 흡수
│   │   │   ├── Accordion.cs                 # 펼침/접힘 컨테이너
│   │   │   └── AccordionElement.cs          # 펼침/접힘 요소 (Toggle 상속)
│   │   ├── Tooltip/                         # v3.5
│   │   │   ├── Tooltip.cs                   # 툴팁 박스 (위치 자동 보정)
│   │   │   └── TooltipTrigger.cs            # 호버/long-press 트리거
│   │   └── SegmentedControl/                # v3.5
│   │       ├── SegmentedControl.cs          # iOS 스타일 토글 버튼 묶음
│   │       └── Segment.cs                   # 개별 segment
│   ├── Screens/                             # Page/Modal/Sheet 시스템 (v3.1+)
│   │   ├── Core/
│   │   │   ├── IScreenContainer.cs        # 컨테이너 공통 인터페이스
│   │   │   └── Transitions/
│   │   │       ├── ITransitionAnimation.cs # 전환 애니메이션 추상화
│   │   │       ├── TransitionAnimationObject.cs # SO 베이스
│   │   │       ├── FadeTransition.cs
│   │   │       ├── ScaleTransition.cs
│   │   │       └── SlideTransition.cs
│   │   ├── Sheet/                            # 탭형 (history 없음, v3.1)
│   │   │   ├── ISheetLifecycle.cs           # 5-step lifecycle
│   │   │   ├── Sheet.cs                     # MB 베이스
│   │   │   ├── SheetContainer.cs            # 등록/표시/숨김 관리
│   │   │   └── SheetEvents.cs               # R3 Observable
│   │   ├── Page/                             # 시퀀스 (history stack, v3.2)
│   │   │   ├── IPageLifecycle.cs            # 8-step lifecycle
│   │   │   ├── Page.cs                      # MB 베이스 (4 transition slot)
│   │   │   ├── PageContainer.cs             # push/pop/popTo/popToRoot 관리
│   │   │   └── PageEvents.cs                # R3 Observable 8종
│   │   ├── Modal/                            # 다중 stacking modal (v3.3)
│   │   │   ├── IModalLifecycle.cs           # 8-step lifecycle
│   │   │   ├── Modal.cs                     # MB 베이스 (2 transition slot, push/pop 시 시각 변화 미니멀)
│   │   │   ├── ModalContainer.cs            # push/pop/popTo/popAll + BackdropHandler 통합
│   │   │   ├── ModalEvents.cs               # R3 Observable 8종
│   │   │   ├── ModalBackdrop.cs             # 자체 enter/exit + click-to-close
│   │   │   ├── ModalBackdropStrategy.cs     # enum (4종 전략)
│   │   │   └── BackdropHandlers/            # internal — strategy 구현 5종
│   │   └── Feature.md (각 하위 폴더에도)     # 탐색용 Feature.md (v3.4)
│   ├── DefaultAssets/                        # out-of-box 사용 자산 (v3.4)
│   │   ├── DefaultModalBackdrop.prefab      # 검정 50% 알파 + click-to-close
│   │   └── Transitions/                      # 8개 preset SO
│   │       ├── DefaultFadeIn.asset / DefaultFadeOut.asset
│   │       ├── DefaultScaleIn.asset / DefaultScaleOut.asset
│   │       └── DefaultSlideIn{Right,Left}.asset / DefaultSlideOut{Left,Right}.asset
│   └── Shaders/
│       ├── TutorialMaskHolePunch.shader
│       └── TutorialMaskOverlay.shader
└── Editor/
    ├── Tjdtjq5.UIFramework.Editor.asmdef
    └── UIStateBinderEditor.cs       # UIStateBinder 커스텀 인스펙터
```

## API (외부 피처가 참조 가능)

### Components (단순 UI 컴포넌트)
- UIStateBinder.SetState<TEnum>(value) — 상태 전환 (GC-free) → Components/UIStateBinder.cs
- UIFlyEffect.Play(sprite, from, target, onComplete) — 베지어 플라이 → Components/UIFlyEffect.cs
- ButtonClickEffect — 버튼 스케일 펀치 (AddComponent만으로 사용) → Components/ButtonClickEffect.cs
- NumberCounter.SetValue(float) / SetValueImmediate(float) — 숫자 트윈 → Components/NumberCounter.cs
- SafeAreaFitter — 모바일 노치 자동 적응 (AddComponent만으로 사용) → Components/SafeAreaFitter.cs
- UITutorialMask.Show(target, text, onTap) — 튜토리얼 스포트라이트 → Components/UITutorialMask.cs
- UITutorialMask.ShowSequence(steps, onComplete) — 튜토리얼 시퀀스 → Components/UITutorialMask.cs
- UIShake.Shake() / Shake(strength, duration) — 흔들림 효과 → Components/UIShake.cs
- RecycleScrollView.Init(totalCount) — 무한 재사용 스크롤 초기화 → Components/RecycleScroll/RecycleScrollView.cs
- RecycleScrollView.ScrollTo(index) — 특정 셀로 이동 → Components/RecycleScroll/RecycleScrollView.cs

### UI-Extensions 흡수 (v3.5+, BSD-3)
- FlowLayoutGroup — 가변폭 자동 wrap (태그/뱃지/칩) → Components/FlowLayoutGroup.cs
- Accordion + AccordionElement — 펼침/접힘 (LitMotion height tween) → Components/Accordion/
- Tooltip + TooltipTrigger — 호버/long-press 툴팁 → Components/Tooltip/
- SegmentedControl + Segment — iOS 스타일 토글 묶음 → Components/SegmentedControl/

### Screens (Sheet 시스템 — v3.1+)
- SheetContainer.RegisterAsync(addressableKey, ct) → int sheetId — Addressable 프리팹을 로드해 Sheet 등록
- SheetContainer.ShowAsync(sheetId, playAnimation, ct) — 지정 Sheet 표시 (이전 활성 sheet 자동 숨김)
- SheetContainer.HideAsync(playAnimation, ct) — 현재 활성 Sheet 숨김
- SheetContainer.UnregisterAsync(sheetId) — 등록 해제 + GameObject 파괴
- SheetContainer.Events.OnWillEnter / OnDidEnter / OnWillExit / OnDidExit — R3 Observable<Sheet>
- Sheet (abstract) — Initialize/WillEnter/DidEnter/WillExit/DidExit/Cleanup virtual override
- TransitionAnimationObject (SO) — Fade/Scale/Slide preset 또는 사용자 커스텀

### Screens (Page 시스템 — v3.2+)
- PageContainer.PushAsync(addressableKey, playAnimation, stack, pageId, ct) → string pageId — 새 페이지 push
- PageContainer.PopAsync(playAnimation, popCount, ct) — 1개 또는 여러 개 pop
- PageContainer.PopToAsync(destinationPageId, ...) — 특정 페이지까지 pop
- PageContainer.PopToRootAsync(...) — root까지 pop
- PageContainer.CurrentPage / CurrentPageId / StackPageIds / CanPop — stack 상태
- PageContainer.Events — R3 Observable 8종 (OnWillPushEnter/OnDidPushEnter/OnWillPushExit/OnDidPushExit/OnWillPopEnter/OnDidPopEnter/OnWillPopExit/OnDidPopExit)
- Page (abstract) — 8-step lifecycle override + 4종 transition slot (pushEnter/pushExit/popEnter/popExit)
- Page transitions은 partner page 인지 가능 (USN의 시너지 transition 패턴)

### Screens (Modal 시스템 — v3.3+)
- ModalContainer.PushAsync(addressableKey, playAnimation, modalId, ct) → string modalId — modal push (항상 stack)
- ModalContainer.PopAsync(playAnimation, popCount, ct) — popCount만큼 동시 pop (모든 exit modal 병렬 애니메이션)
- ModalContainer.PopToAsync(destinationModalId, ...) — 특정 modal까지 pop
- ModalContainer.PopAllAsync(...) — 모든 modal pop (clean)
- ModalContainer.TopModal / TopModalId / StackModalIds / CanPop — stack 상태
- ModalContainer.Events — R3 Observable 8종 (Page와 동일 시그니처)
- Modal (abstract) — 8-step lifecycle, 2종 transition slot (enter/exit). PushExit/PopEnter는 lifecycle hook만 (Modal은 background에서도 가시 유지)
- ModalBackdrop — _enterAnimation/_exitAnimation/_closeModalWhenClicked 슬롯
- ModalBackdropStrategy (enum) — GeneratePerModal(default) / OnlyFirstBackdrop / ChangeOrderBeforeAnimation / ChangeOrderAfterAnimation

### 전환 preset (CreateAssetMenu)
- Tjdtjq5/UIFramework/Transition/Fade — CanvasGroup alpha 페이드
- Tjdtjq5/UIFramework/Transition/Scale — 스케일 + 페이드 (Modal/Popup)
- Tjdtjq5/UIFramework/Transition/Slide — anchoredPosition 슬라이드 (4방향)

## 주의사항
- v3.5.0 — Unity-UI-Extensions Tier 1 흡수 (Accordion/FlowLayoutGroup/Tooltip/SegmentedControl, BSD-3). 의존성 변경 없음
- v3.4.0 — Default 자산 추가 (ModalBackdrop 1개 + Transitions 8개) + Screens 하위 Feature.md 5개. 의존성 변경 없음
- v3.3.0 — Modal 시스템 추가 (backdrop strategy, 다중 stacking, popAll). Screen 시스템 (Page/Modal/Sheet) 완성. 의존성 변경 없음
- v3.2.0 — Page 시스템 추가 (history stack, push/pop). 의존성 변경 없음
- v3.1.0 — Screen 시스템(Sheet) 추가. 의존성 4개 추가됨 (AddrX, UniTask, R3, VContainer)
- v3.0.0 breaking change — 트윈 라이브러리 DOTween → LitMotion 교체 (public API 동일, 의존성만 변경)
- v2.0.0 breaking change — Popup 시스템 (UIPopup, UIManager, UITransition, UIDialog) 전부 제거됨
