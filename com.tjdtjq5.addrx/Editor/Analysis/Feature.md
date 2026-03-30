# Analysis Feature

- **상태**: stable
- **용도**: Addressable 그룹/에셋의 품질 분석 도구 모음 (중복, 건강도, 예산, 동작 차이, 임팩트, 비결정성)

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| AddrXLog | `../../Runtime/` | 로그 출력 |
| Windows/AnalysisTab | `../Windows/AnalysisTab.cs` | UI 진입점 (전체 분석 실행 + 결과 시각화) |
| EditorToolkit | 외부 패키지 `Tjdtjq5.EditorToolkit.Editor` | AnalysisTab UI 렌더링 유틸 |

## 구조

```
Editor/Analysis/
  BehaviorDiffChecker.cs   -- 에디터/빌드 간 동작 차이를 룰 기반으로 스캔
  BundleSizeBudget.cs      -- 그룹별 크기 예산 설정 및 초과 검사
  DuplicateScanner.cs      -- 여러 그룹에 중복 포함된 에셋 탐지 (암시적 의존성 포함)
  GroupHealthScore.cs      -- 그룹별 건강도 점수(0~100) 계산 (에셋 수, 크기, 의존성 복잡도)
  ImpactAnalyzer.cs        -- 에셋 로드 시 연쇄 로드되는 번들/그룹 분석
  NondeterminismScanner.cs -- AssetPostprocessor/ScriptedImporter의 비결정성 패턴 감지
```

## API

### BehaviorDiffChecker

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Check()` | `static List<DiffWarning> Check()` | 모든 룰 실행, 경고 목록 반환 |
| `AddRule()` | `static void AddRule(IDiffRule rule)` | 커스텀 검사 룰 추가 |
| `Rules` | `static IReadOnlyList<IDiffRule> Rules` | 등록된 룰 목록 |

기본 룰: `ResourcesFolderRule` (Resources+Addressables 이중 등록), `SceneDuplicateRule` (Build Settings+Addressables 씬 중복), `SpriteAtlasRule` (SpriteAtlas 에디터/빌드 동작 차이)

### IDiffRule (인터페이스)

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Name` | `string Name { get; }` | 룰 이름 |
| `Check()` | `List<DiffWarning> Check(AddressableAssetSettings)` | 검사 실행 |

### DiffWarning (struct)

| 필드 | 타입 | 설명 |
|------|------|------|
| `RuleName` | `string` | 발생 룰 이름 |
| `Message` | `string` | 경고 메시지 |
| `AssetPath` | `string` | 관련 에셋 경로 (nullable) |

### BundleSizeBudget

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `SetBudget()` | `static void SetBudget(string groupName, long bytes)` | 그룹별 크기 예산 설정 (바이트) |
| `GetBudget()` | `static long GetBudget(string groupName)` | 예산 반환 (미설정 시 기본 10MB) |
| `Check()` | `static List<BudgetViolation> Check()` | 모든 그룹 예산 초과 검사 |

### BudgetViolation (struct)

| 필드/속성 | 타입 | 설명 |
|-----------|------|------|
| `GroupName` | `string` | 그룹 이름 |
| `ActualBytes` | `long` | 실제 크기 |
| `BudgetBytes` | `long` | 예산 |
| `EntryCount` | `int` | 에셋 수 |
| `OverPercent` | `float` | 초과 퍼센트 |

### DuplicateScanner

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Scan()` | `static DuplicateReport Scan()` | 전체 그룹 스캔, 중복 리포트 반환 |

### DuplicateReport (struct)

| 필드/속성 | 타입 | 설명 |
|-----------|------|------|
| `Entries` | `List<DuplicateEntry>` | 중복 에셋 목록 |
| `Count` | `int` | 중복 에셋 수 |

### DuplicateEntry (struct)

| 필드 | 타입 | 설명 |
|------|------|------|
| `AssetPath` | `string` | 에셋 경로 |
| `Groups` | `List<string>` | 포함된 그룹 이름 목록 |

### GroupHealthScore

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Evaluate()` | `static List<GroupScore> Evaluate()` | 모든 그룹 건강도 평가 (점수 오름차순 정렬) |

### GroupScore (struct)

| 필드/속성 | 타입 | 설명 |
|-----------|------|------|
| `GroupName` | `string` | 그룹 이름 |
| `Score` | `float` | 건강도 점수 (0~100) |
| `EntryCount` | `int` | 에셋 수 |
| `TotalSizeBytes` | `long` | 총 크기 |
| `Issues` | `List<string>` | 발견된 이슈 목록 |
| `SizeText` | `string` | 읽기 편한 크기 문자열 |

감점 기준: 빈 그룹(-10), 에셋 100개 초과(최대 -30), 크기 10MB 초과(최대 -30), 의존성 비율 50 초과(최대 -20)

### ImpactAnalyzer

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Analyze()` | `static ImpactReport Analyze(string address)` | Addressable 주소로 임팩트 분석 |
| `AnalyzeByPath()` | `static ImpactReport AnalyzeByPath(string assetPath)` | 에셋 경로로 임팩트 분석 |
| `ScanAll()` | `static List<ImpactReport> ScanAll()` | 전체 에셋 임팩트 분석 (번들 수/크기 내림차순) |

### ImpactReport (struct)

| 필드/속성 | 타입 | 설명 |
|-----------|------|------|
| `Address` | `string` | Addressable 주소 |
| `AssetPath` | `string` | 에셋 경로 |
| `SourceGroup` | `string` | 소스 그룹 이름 |
| `Impacts` | `List<GroupImpact>` | 연쇄 로드되는 그룹 목록 |
| `TotalBytes` | `long` | 전체 크기 |
| `BundleCount` | `int` | 영향받는 번들 수 |
| `IsEmpty` | `bool` | 빈 리포트 여부 |
| `Empty` | `static ImpactReport` | 빈 리포트 상수 |

### GroupImpact (class)

| 필드/속성 | 타입 | 설명 |
|-----------|------|------|
| `GroupName` | `string` | 그룹 이름 |
| `Assets` | `List<string>` | 포함 에셋 경로 |
| `TotalBytes` | `long` | 합산 크기 |

### NondeterminismScanner

| 멤버 | 시그니처 | 설명 |
|------|----------|------|
| `Scan()` | `static List<NondeterminismWarning> Scan()` | 프로젝트 내 비결정성 패턴 스캔 |

검출 패턴: `DateTime.Now/UtcNow/Today`, `Random.*`, `Guid.NewGuid()`, `Environment.TickCount`, `GetHashCode()`

### NondeterminismWarning (struct)

| 필드 | 타입 | 설명 |
|------|------|------|
| `FilePath` | `string` | 파일 경로 |
| `Line` | `int` | 줄 번호 |
| `Pattern` | `string` | 매치된 정규식 |
| `Message` | `string` | 경고 메시지 |

## 주의사항

- 모든 분석기는 `AddressableAssetSettingsDefaultObject.Settings`가 null이면 빈 결과를 반환한다.
- `DuplicateScanner.Scan()`은 `AssetDatabase.GetDependencies(recursive: true)`로 암시적 의존성까지 체크하므로 대규모 프로젝트에서는 시간이 걸릴 수 있다.
- `ImpactAnalyzer.ScanAll()`도 전체 에셋의 의존성을 재귀 탐색하므로 동일하게 주의.
- `BundleSizeBudget`의 예산은 static 딕셔너리에 저장되므로 도메인 리로드 시 초기화된다.
- `BehaviorDiffChecker`에 커스텀 룰을 추가(`AddRule`)하면 도메인 리로드 전까지 유지된다.
- `NondeterminismScanner`는 `Assets/` 하위의 `.cs` 파일만 검사하며, 주석 라인은 스킵한다.
