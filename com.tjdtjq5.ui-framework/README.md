# UI Toolkit

범용 UI 도구 모음 패키지 — 독립적으로 사용 가능한 UI 컴포넌트 제공.

## 설치

```json
"com.tjdtjq5.ui-framework": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.ui-framework#ui-framework/v2.0.1"
```

## 의존성

- Unity 6000.1+
- DOTween
- TextMeshPro
- [com.tjdtjq5.editor-toolkit](https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-toolkit) v1.1.0+

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

## v2.0.0 Breaking Changes

- Popup 시스템 제거 (UIPopup, UIManager, UITransition, UIDialog)
- VContainer 의존성 제거
- Popup을 쓰던 프로젝트는 로컬로 이관 필요
