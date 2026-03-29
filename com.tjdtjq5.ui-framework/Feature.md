# UI Toolkit

## 상태
stable

## 용도
범용 UI 도구 모음 패키지 — 독립적으로 사용 가능한 UI 컴포넌트 제공 (v2.0.0)

## 의존성
- com.tjdtjq5.editor-toolkit — Inspector Attribute (SectionHeader, Required, BoxGroup 등)
- DOTween — 트윈 애니메이션 엔진
- TextMeshPro — 텍스트 렌더링 (NumberCounter, UITutorialMask)

## 구조

```
com.tjdtjq5.ui-framework/
├── package.json                     # v2.0.0
├── Feature.md
├── README.md
├── Runtime/
│   ├── Tjdtjq5.UIFramework.Runtime.asmdef
│   ├── Components/
│   │   ├── ButtonClickEffect.cs     # 버튼 누르기 스케일 펀치 애니메이션
│   │   ├── NumberCounter.cs         # 숫자 카운팅 트윈 (TMP)
│   │   ├── SafeAreaFitter.cs        # 노치/SafeArea 자동 적응
│   │   ├── UIFlyEffect.cs           # 베지어 곡선 플라이 애니메이션
│   │   ├── UIShake.cs               # RectTransform 흔들림 효과
│   │   ├── UIStateBinder.cs         # enum/string 상태 전환 바인딩
│   │   ├── UITutorialMask.cs        # 스텐실 기반 튜토리얼 스포트라이트
│   │   └── RecycleScroll/
│   │       ├── Feature.md
│   │       ├── IScrollCell.cs       # 셀 인터페이스
│   │       └── RecycleScrollView.cs # 무한 재사용 스크롤
│   └── Shaders/
│       ├── TutorialMaskHolePunch.shader
│       └── TutorialMaskOverlay.shader
└── Editor/
    ├── Tjdtjq5.UIFramework.Editor.asmdef
    └── UIStateBinderEditor.cs       # UIStateBinder 커스텀 인스펙터
```

## API (외부 피처가 참조 가능)
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

## 주의사항
- v2.0.0 breaking change — Popup 시스템 (UIPopup, UIManager, UITransition, UIDialog) 전부 제거됨
- VContainer 의존성 완전 제거 — DI 없는 순수 도구 패키지
- Popup을 쓰던 프로젝트는 로컬로 이관 필요
