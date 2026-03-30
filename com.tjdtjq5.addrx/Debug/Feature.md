# Debug

- **상태**: stable
- **용도**: 핸들 추적, 누수 감지, 인게임 디버그 HUD -- 개발/에디터 빌드 전용

## 의존성

| 폴더 | 관계 |
|------|------|
| `../Runtime/Core/` | HandleTracking 이벤트 구독, HandleStatus 참조 |
| `../Runtime/Logging/` | 로그 출력 (AddrXLog) |
| `../Runtime/Settings/` | AddrXSettings에서 Tracking/LeakDetection 설정 읽기 |

## 구조

```
Debug/
├── HandleTracker.cs      # 모든 SafeHandle 생성/해제를 Dictionary로 추적 (static class)
├── HandleInfo.cs         # 추적 중인 핸들 정보 readonly struct
├── LeakDetector.cs       # 씬 전환 시 미해제 핸들 경고 + 수동 누수 체크
├── DebugHUD.cs           # IMGUI 디버그 오버레이 (F9 토글)
└── Tjdtjq5.AddrX.Debug.asmdef  # 별도 어셈블리 정의
```

## API

### HandleTracker (static class)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 프로퍼티 | `IReadOnlyList<HandleInfo> ActiveHandles` | 현재 활성 핸들 목록 |
| 프로퍼티 | `int ActiveCount` | 현재 활성 핸들 수 |
| 프로퍼티 | `int TotalLoaded` | 누적 로드 횟수 |
| 프로퍼티 | `int TotalReleased` | 누적 해제 횟수 |
| 이벤트 | `event Action<HandleInfo> OnHandleCreated` | 핸들 생성 시 발생 |
| 이벤트 | `event Action<HandleInfo> OnHandleReleased` | 핸들 해제 시 발생 |
| 메서드 | `HandleInfo? FindByAddress(string address)` | 특정 주소의 활성 핸들 검색 |

### HandleInfo (readonly struct)

| 필드 | 타입 | 설명 |
|------|------|------|
| `Id` | `int` | 핸들 고유 ID |
| `Address` | `string` | Addressable 주소/키 |
| `AssetType` | `Type` | 에셋 타입 |
| `CreatedAt` | `float` | 생성 시점 (realtimeSinceStartup) |
| `StackTrace` | `string` | 할당 위치 스택 트레이스 |
| `Age` | `float` | 생성 후 경과 시간(초, 프로퍼티) |

### LeakDetector (static class)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 프로퍼티 | `bool AutoCheckOnSceneChange` | 씬 전환 시 자동 누수 체크 활성화 여부 (get/set) |
| 메서드 | `LeakReport CheckForLeaks()` | 현재 활성 핸들 기준 누수 리포트 생성 |

### LeakReport (readonly struct)

| 멤버 | 시그니처 | 설명 |
|------|---------|------|
| 프로퍼티 | `int LeakCount` | 감지된 미해제 핸들 수 |
| 프로퍼티 | `IReadOnlyList<HandleInfo> Leaks` | 미해제 핸들 목록 |

### DebugHUD (MonoBehaviour)

| 기능 | 설명 |
|------|------|
| F9 토글 | IMGUI 오버레이 표시/숨김 (키 변경 가능) |
| 요약 표시 | Active / Loaded / Released 카운트 |
| 상세 모드 | 개별 핸들 목록 (ID, 주소, 타입, 경과 시간) |
| Check Leaks 버튼 | 수동 누수 체크 실행 |

## 주의사항

- 모든 파일이 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 조건부 컴파일. 릴리스 빌드에는 포함되지 않는다.
- `HandleTracker`는 `RuntimeInitializeOnLoadMethod`에서 `HandleTracking.Created/Released` 이벤트를 자동 구독한다.
- `LeakDetector.AutoCheckOnSceneChange`는 `AddrXSettings.EnableLeakDetection` 설정에 따라 자동 활성화된다.
- `DebugHUD`는 씬에 직접 배치해야 동작한다. 자동 생성되지 않는다.
- 별도 어셈블리(`Tjdtjq5.AddrX.Debug`)로 분리되어 있으므로 Runtime 코드에서 Debug 코드를 참조하지 않는다.
