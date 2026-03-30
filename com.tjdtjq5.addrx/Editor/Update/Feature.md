# Update Feature

- **상태**: stable
- **용도**: Addressables Content Update 워크플로우 가이드 + 빌드 해시 비교 + 앱 버전별 카탈로그 라우팅

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| AddrXLog | `../../Runtime/` | 로그 출력 |
| Windows/AddrXManagerWindow | `../Windows/AddrXManagerWindow.cs` | UpdateTab을 탭으로 호스팅 |
| EditorToolkit | 외부 패키지 `Tjdtjq5.EditorToolkit.Editor` | EditorTabBase 상속, EditorUI 유틸 |

## 구조

```
Editor/Update/
  BuildHashComparer.cs     -- 두 빌드의 카탈로그 JSON을 비교하여 변경된 번들/해시 추적
  UpdateTab.cs             -- Update 탭 UI: Workflow + Hash Compare + Version Route 서브탭
  VersionRouteManager.cs   -- version_route.json 관리 (앱 버전별 카탈로그 매핑)
```

## API

### BuildHashComparer

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Compare()` | `static CompareReport Compare(string oldCatalogPath, string newCatalogPath)` | 두 카탈로그 JSON 비교 |

카탈로그 JSON에서 `.bundle` 파일 참조를 파싱하고 `bundlename_hash.bundle` 패턴에서 번들명과 해시를 추출하여 비교.

### CompareReport (struct)

| 필드/속성 | 타입 | 설명 |
|-----------|------|------|
| `Added` | `List<BundleChange>` | 새로 추가된 번들 |
| `Changed` | `List<BundleChange>` | 해시가 변경된 번들 |
| `Removed` | `List<BundleChange>` | 제거된 번들 |
| `Unchanged` | `List<string>` | 동일한 번들 이름 목록 |
| `IsEmpty` | `bool` | 변경 없음 여부 |
| `TotalChanges` | `int` | 총 변경 수 (Added + Changed + Removed) |
| `Empty` | `static CompareReport` | 빈 리포트 상수 |

### BundleChange (struct)

| 필드 | 타입 | 설명 |
|------|------|------|
| `BundleName` | `string` | 번들 이름 |
| `OldHash` | `string` | 이전 해시 (Added이면 null) |
| `NewHash` | `string` | 새 해시 (Removed이면 null) |

### VersionRouteManager

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Load()` | `static VersionRoute Load(string filePath)` | JSON 파일에서 라우트 로드 |
| `Save()` | `static void Save(VersionRoute route, string filePath)` | JSON 파일로 저장 |
| `GetCatalogForVersion()` | `static string GetCatalogForVersion(VersionRoute route, string appVersion)` | 앱 버전에 해당하는 카탈로그 파일명 반환 |
| `GetCleanableEntries()` | `static List<RouteEntry> GetCleanableEntries(VersionRoute route)` | 최소 버전 미만인 라우트 엔트리 목록 |
| `GetUniqueCatalogCount()` | `static int GetUniqueCatalogCount(VersionRoute route)` | 사용 중인 고유 카탈로그 파일 수 |

### VersionRoute (Serializable class)

| 필드 | 타입 | 설명 |
|------|------|------|
| `minimum` | `string` | 최소 지원 앱 버전 |
| `routes` | `List<RouteEntry>` | 버전별 카탈로그 매핑 목록 |

### RouteEntry (Serializable class)

| 필드 | 타입 | 설명 |
|------|------|------|
| `appVersion` | `string` | 앱 버전 (시맨틱 버전) |
| `catalogFile` | `string` | 카탈로그 파일명 |

### UpdateTab (EditorTabBase)

3개 서브탭 구성:

| 서브탭 | 설명 |
|--------|------|
| **Workflow** | Content Update 3단계 가이드 (State 파일 선택 → 변경 감지+빌드 → 서버 업로드) |
| **Hash Compare** | 두 카탈로그 JSON 비교 UI (BuildHashComparer 호출) |
| **Version Route** | version_route.json 편집 UI (라우트 추가/삭제, 최소 버전 설정, cleanable 정리) |

`AddrXManagerWindow`에서 탭으로 사용.

## 주의사항

- `BuildHashComparer`는 카탈로그 JSON의 `.bundle` 파일 참조를 라인 단위로 파싱한다. Addressables 카탈로그 포맷이 변경되면 파싱 로직 수정이 필요할 수 있다.
- `VersionRouteManager`는 `JsonUtility`를 사용하므로 JSON 구조는 `VersionRoute` 클래스와 정확히 일치해야 한다.
- 버전 비교는 시맨틱 버전(`major.minor.patch`) 기반으로 각 세그먼트를 정수 비교한다.
- Workflow 서브탭의 `Check & Build Content Update`는 `ContentUpdateScript.BuildContentUpdate()`를 직접 호출하므로 실행 전 content_state.bin 파일이 올바른지 확인해야 한다.
- 고정 카탈로그 파일명 방식을 사용하는 프로젝트에서는 Version Route 기능이 불필요하다.
