# Changelog

## [0.2.0] - 2026-05-02

### Changed (Breaking) — UniTask 마이그레이션

Runtime 전반을 `Task` → `UniTask`로 전환. 외부 시그니처 변경이 다수 발생하므로 minor 범프(0.x).

- **Core** — `AddrX.LoadAsync<T>(object key)`, `AddrX.Initialize()`, 내부 `InitializeCore`/`WaitForResourceLocators` 시그니처 `Task` → `UniTask`. `Task.CompletedTask` → `UniTask.CompletedTask`, `Task.Yield()` → `UniTask.Yield()`.
- **Loading** — `LoadAsync(AssetReference)`, `LoadBatchAsync<T>`, `InstantiateAsync`, `LoadByLabelAsync<T>`(2 오버로드), 내부 `LoadFromLocations`/`EnsureInitialized` 시그니처 `UniTask`로 변경. `Task.WhenAll`은 Addressables 자체 Task[] await 호환 유지.
- **Download** — `CheckCatalogUpdatesAsync`, `UpdateCatalogsAsync`(`AddrX.Download.cs`), `CatalogChecker.CheckForUpdatesAsync`/`UpdateCatalogsAsync`, `AddrXDownloader.GetTotalSizeAsync`/`StartAsync`/`DownloadWithRetry` 시그니처 `UniTask`. `Task.Delay` → `UniTask.Delay`.
- **ComponentLoader** — `WaitForLoad()` 시그니처 `Task` → `UniTask`. 내부 `_loadTask`는 `Task` 유지 (다중 호출자가 같은 작업 await — UniTask는 struct + 1회 await 제약).
- **`_initTask` 캐싱** — Core의 `_initTask` 필드는 `Task` 유지. 외부 API는 `.AsUniTask()`로 변환해 `UniTask` 노출.

### Migration Guide
호출자 측은 대부분 `await`만 사용하므로 변경 불요 (Task ↔ UniTask awaiter 호환). 단:
- 외부에서 `Task` 타입을 명시적으로 변수 선언/필드로 보유한 코드는 `UniTask`로 변경 또는 `.AsTask()` 사용
- `Tjdtjq5.AddrX.Runtime.asmdef` references에 `"UniTask"` 추가됨 (의존자 asmdef는 UniTask reference 자동 transitive)

## [0.1.7] - 2026-04-25

### Fixed
- `AddrXAutoRegister.OnPostprocessAllAssets` — Addressables 변경 알림을 `EntryModified` → `BatchModification`으로 교체
  - `EntryModified`는 Entry 데이터를 요구하는 이벤트인데 일괄 작업 완료 마커로 null을 넘기면 외부 구독자(예: Quantum의 `QuantumAssetObjectPostprocessor`)가 null 캐스트에서 NRE로 터짐
  - `BatchModification`은 일괄 작업 완료를 알리는 의도에 맞고 Entry data를 요구하지 않아 null 안전

## [0.1.6] - 2026-04-05

### Fixed
- 에셋 복제 시 importedAssets/movedAssets 중복으로 인한 자동 등록 오탐 수정 (FilterAssets 경로 중복 제거)
- DetectDuplicates에서 newPaths 내 같은 GUID 이중 카운트 방지

## [0.1.5] - 2026-04-05

### Fixed
- 자동 등록 중복 감지에서 기존 에셋 재임포트(내용 수정)를 오탐하는 버그 수정 — 동일 GUID 스킵

## [0.1.4] - 2026-04-01

### Improved
- SafeHandle Finalizer 경고에 Key + 할당 스택 트레이스 포함 — 누수 원인 즉시 추적 가능

## [0.1.3] - 2026-04-01

### Fixed
- InitializeCore 3단계 안전 초기화 (Unity 자동 초기화 경합 해결, WaitForResourceLocators 폴백)
- EnsureInitialized 경합 조건 — 실패 후 자동 Initialize() 재시도
- CatalogChecker/UpdateCatalogsAsync try-finally로 핸들 누수 방지
- LoadBatchAsync/LoadFromLocations completed++ → Interlocked.Increment (동시성 안전)
- AddrXAutoRegister LINQ Concat().Where().ToList() → 수동 루프 (GC 할당 감소)
- AnalysisTab 중복 LINQ Count/Any 호출 → 캐싱 변수로 통합
- DuplicateScanner GetDependencies 결과 캐싱 (O(n²) 완화)
- AddrXFolderColorizer 조기 반환 강화 + RootPath 캐싱
- VersionRouteManager 비숫자 버전 파트 경고 로그 추가
- BuildHashComparer null 방어 + 파싱 한계 문서화
- UpdateTab FindContentStateFile 중복 파일 경고 추가
- AddrXDownloader 재시도 대기 매직 넘버 → RetryBaseDelayMs 상수화

## [0.1.2] - 2026-03-30

### Fixed
- AutoInitialize BeforeSceneLoad → AfterSceneLoad 변경 (Addressables 미초기화 상태에서 invalid handle 에러)
- Addressables 이미 초기화 시 InitializeAsync 재호출 방지 (ResourceLocators 체크)
- Initialize() race condition 수정 (lock + double-check)
- 초기화 무한 재시도 방지 (MaxInitAttempts=3 제한)
- AutoInitialize에서 _initTask 덮어쓰기 방지 (??= 사용)
- ComponentLoader async void → Task 기반 API 전환 (WaitForLoad 추가)
- LoadByLabelAsync 리소스 해제를 try-finally로 단일화

### Added
- SafeHandle.IsReady 프로퍼티 (Value 접근 안전 보장)
- Feature.md 7개 + FEATURE_INDEX.md (피처 구조 도입)

## [0.1.1] - 2026-03-29

### Fixed
- SetupTab.GetCurrentStep()에서 AddrXSetupRules 미생성 시 NullReferenceException 수정
- UpdateDashboardCounts(), SyncAll()에서 동일 null 가드 추가
- 첫 설치 후 AddrX Manager 윈도우 열 때 크래시 해결

## [0.1.0] - 2026-03-29

### Added
- SafeHandle<T> 기반 안전한 에셋 로딩/해제 (using, BindTo, 수동 Dispose)
- AddrX 정적 API: LoadAsync, LoadBatchAsync, InstantiateAsync
- HandleTracker / LeakDetector / DebugHUD (Debug 모듈)
- Editor 분석 도구: DuplicateScanner, GroupHealthScore, BundleSizeBudget, BehaviorDiffChecker
- AddrX Manager 윈도우 (Setup / Tracker / Analysis 탭)
- 자동 등록 시스템 (AssetPostprocessor 기반)
- 폴더 템플릿 생성기
- Project Settings 연동
- 통합 로깅 (AddrXLog)
