# RecycleScrollView

## 상태
stable

## 용도
무한 재사용 스크롤 — 화면에 보이는 셀 + 버퍼만 유지하고 나머지는 재활용하여 대량 데이터를 렌더링

## 의존성
- DOTween — 스냅 애니메이션
- com.tjdtjq5.editor-toolkit — Inspector Attribute (SectionHeader, BoxGroup)

## 구조
- IScrollCell.cs — 셀 인터페이스 (OnUpdateCell, OnRecycled)
- RecycleScrollView.cs — 핵심 컴포넌트 (ScrollRect 래핑, 셀 풀링/재활용, 스냅)

## API (외부 피처가 참조 가능)

### IScrollCell (인터페이스)
- `OnUpdateCell(int index)` — 셀에 데이터 바인딩 → IScrollCell.cs
- `OnRecycled()` — 재활용 전 정리 → IScrollCell.cs

### RecycleScrollView (MonoBehaviour)
- `Init(int totalCount)` — 초기화 + 셀 표시 → RecycleScrollView.cs
- `UpdateCount(int totalCount)` — 데이터 개수 변경 + 갱신 → RecycleScrollView.cs
- `ScrollTo(int index)` — 특정 셀로 즉시 이동 → RecycleScrollView.cs
- `RefreshAll()` — 보이는 전체 셀 갱신 → RecycleScrollView.cs
- `RefreshCell(int index)` — 특정 셀만 갱신 → RecycleScrollView.cs

### Inspector 설정
- `ScrollDirection` — Vertical / Horizontal
- `_gridCount` — 1=List, 2+=Grid
- `SnapMode` — None / Cell / Page
- `_cellPrefab` — IScrollCell 구현 필수
- `_autoDetectCellSize` — Prefab RectTransform에서 자동 측정
- `_spacing` — 셀 간 간격
- `_padding` — Content 영역 패딩

## 주의사항
- cellPrefab에 IScrollCell 구현 MonoBehaviour가 반드시 있어야 함
- 균일 셀 크기 전제 — 가변 크기 미지원
- ScrollRect를 RequireComponent로 의존
- 스냅 사용 시 ScrollRect의 inertia 설정과 상호작용
- Grid에서 totalCount가 gridCount 배수가 아니면 마지막 줄 빈 셀은 비활성
