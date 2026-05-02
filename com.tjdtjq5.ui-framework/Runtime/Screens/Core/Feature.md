# Screens.Core

## 상태
stable (v3.1+)

## 용도
Page/Modal/Sheet의 공통 추상화. 각 종류의 Container가 동일한 인터페이스를 구현.

## 구조
```
Core/
├── IScreenContainer.cs              # 컨테이너 공통 인터페이스
└── Transitions/
    ├── ITransitionAnimation.cs      # 전환 애니메이션 추상화
    ├── TransitionAnimationObject.cs # ScriptableObject 베이스
    ├── FadeTransition.cs            # CanvasGroup alpha 페이드
    ├── ScaleTransition.cs           # 스케일 + 페이드
    └── SlideTransition.cs           # 4방향 슬라이드
```

## API

### IScreenContainer
- `bool IsInTransition { get; }` — 전환 진행 여부
- `bool Interactable { get; set; }` — 입력 활성 (CanvasGroup.interactable)

### ITransitionAnimation
- `float Duration { get; }` — 전환 시간(초)
- `void Setup(RectTransform rectTransform)` — 대상 셋업
- `void SetPartner(RectTransform partner)` — 동시 전환 페이지 정보 (Sheet은 무시)
- `UniTask PlayAsync(IProgress<float>?, CancellationToken)` — 실행

### TransitionAnimationObject (ScriptableObject 베이스)
구체 구현 (FadeTransition/ScaleTransition/SlideTransition)은 모두 이 베이스 상속. CreateAssetMenu로 인스펙터에서 생성 가능.

## 사용 패턴
1. **Default 자산 사용** (권장): `Runtime/DefaultAssets/Transitions/`의 8개 preset 드래그-드롭
2. **커스텀 SO 생성**: `Project 우클릭 → Create → Tjdtjq5/UIFramework/Transition/[Fade|Scale|Slide]`
3. **코드로 직접 생성**: `var fade = ScriptableObject.CreateInstance<FadeTransition>()` — 보통 안 씀

## 주의사항
- TransitionAnimationObject는 SO이므로 여러 Sheet/Page/Modal이 한 에셋 공유 가능 (재사용 OK)
- Setup/SetPartner는 PlayAsync 직전 Container가 호출. 사용처가 직접 호출 필요 없음
- Partner 정보는 Page/Modal에서만 의미. Sheet은 항상 null 전달
