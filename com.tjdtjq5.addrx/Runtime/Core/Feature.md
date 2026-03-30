# Core

- **상태**: stable
- **용도**: AddrX 패키지 진입점 -- Addressables 초기화, 에셋 로드, SafeHandle 수명 관리

## 의존성

| 폴더 | 관계 |
|------|------|
| `../Logging/` | 로그 출력 (AddrXLog) |
| `../Settings/` | 전역 설정 (AddrXSettings) |

## 구조

```
Runtime/Core/
├── AddrX.cs              # static partial class 진입점 -- 초기화 + LoadAsync<T>
├── SafeHandle.cs         # AsyncOperationHandle<T> 래퍼 -- IDisposable, BindTo 수명 바인딩
├── HandleReleaser.cs     # GameObject 파괴 시 바인딩된 핸들 자동 Dispose (internal)
├── HandleStatus.cs       # SafeHandle 로드 상태 enum (None/Loading/Succeeded/Failed)
└── HandleTracking.cs     # 핸들 생성/해제 이벤트 버스 (EDITOR/DEV 빌드 전용)
```

## API

### AddrX (static partial class)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 프로퍼티 | `bool IsInitialized` | 초기화 완료 여부 |
| 메서드 | `Task Initialize()` | 수동 초기화 (이미 완료됐으면 즉시 반환) |
| 메서드 | `Task<SafeHandle<T>> LoadAsync<T>(object key)` | Addressable 에셋을 로드하고 SafeHandle로 감싸 반환 |

### SafeHandle\<T\> (sealed class, IDisposable, IAsyncDisposable)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 프로퍼티 | `T Value` | 로드된 에셋 (해제/미완료 시 예외) |
| 프로퍼티 | `bool IsValid` | 해제되지 않았고 원본 핸들이 유효한 경우 true |
| 프로퍼티 | `float Progress` | 로딩 진행률 (0~1) |
| 프로퍼티 | `HandleStatus Status` | 현재 로드 상태 |
| 메서드 | `SafeHandle<T> BindTo(GameObject go)` | GO 파괴 시 자동 Dispose -- 체이닝 반환 |
| 메서드 | `void Dispose()` | 핸들 해제 (Addressables.Release 호출) |

### HandleStatus (enum)

| 값 | 의미 |
|----|------|
| `None` | 초기 상태 또는 해제됨 |
| `Loading` | 로드 진행 중 |
| `Succeeded` | 로드 성공 |
| `Failed` | 로드 실패 |

### HandleTracking (static class, EDITOR/DEV 전용)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 이벤트 | `event Action<int, string, Type, string> Created` | 핸들 생성 시 (id, address, assetType, stackTrace) |
| 이벤트 | `event Action<int> Released` | 핸들 해제 시 (id) |

## 주의사항

- `SafeHandle`은 반드시 `using` 블록 또는 `BindTo()`로 수명을 관리해야 한다. Dispose 없이 GC 수집되면 경고 로그 출력 (DEV 빌드).
- `HandleTracking`은 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 조건부 컴파일. 릴리스 빌드에서는 제거된다.
- `HandleReleaser`는 internal -- 외부에서 직접 사용하지 않고, `SafeHandle.BindTo()`를 통해서만 접근.
- `AddrXSettings.AutoInitialize`가 true면 `AfterSceneLoad`에서 자동 초기화된다. false면 `AddrX.Initialize()` 수동 호출 필요.
- Enter Play Mode Settings (도메인 리로드 비활성) 환경에서도 정상 동작하도록 `ResetStatics()` 처리됨.
