# AddrX

Unity Addressables를 감싸는 **안전한 에셋 관리 래퍼**.
`SafeHandle<T>` 기반 자동 해제, 누수 감지, 에디터 분석 도구를 제공한다.

---

## 구조

```
Assets/AddrX/
├── Runtime/          ← 빌드에 포함되는 런타임 코드
│   ├── Core/         SafeHandle, AddrX, HandleReleaser
│   ├── Loading/      배치 로딩, 인스턴스화, ComponentLoader
│   ├── Logging/      통합 로깅 (AddrXLog)
│   └── Settings/     AddrXSettings (ScriptableObject)
├── Debug/            ← 에디터 + 개발빌드 전용 (#if guard)
│   ├── HandleTracker, LeakDetector, DebugHUD
│   └── HandleInfo
├── Editor/           ← 에디터 전용
│   ├── Settings/     Project Settings 연동
│   ├── Setup/        초기 설정 위자드, 자동 등록, 톱니바퀴 Settings
│   ├── Analysis/     중복·건강도·예산·Diff 분석
│   └── Windows/      AddrX Manager 윈도우
└── Resources/        AddrXSettings.asset, AddrXSetupRules.asset
```

---

## 시작하기

### 초기화

Unity 게임 실행 시(Play 또는 빌드 앱) 첫 씬 로드 전에 **자동 초기화**된다 (`AddrXSettings.AutoInitialize = true`).
내부적으로 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`를 사용하여 `Addressables.InitializeAsync()`를 호출한다.

```csharp
// 수동 초기화가 필요한 경우 (AutoInitialize = false일 때)
await AddrX.Initialize();

// 초기화 여부 확인
if (AddrX.IsInitialized) { ... }
```

---

## Runtime API

### 에셋 로딩 — `AddrX.LoadAsync<T>`

```csharp
// 주소(string)로 로딩
SafeHandle<GameObject> handle = await AddrX.LoadAsync<GameObject>("hero_prefab");

// AssetReference로 로딩
[SerializeField] AssetReference _heroRef;
SafeHandle<GameObject> handle = await AddrX.LoadAsync<GameObject>(_heroRef);
```

### 에셋 해제 — 3가지 패턴

#### 패턴 1: using 블록 (권장)

스코프를 벗어나면 자동 해제.

```csharp
await using (var handle = await AddrX.LoadAsync<Sprite>("icon_gold"))
{
    _image.sprite = handle.Value;
    // ... 사용
}
// ← 자동 Release
```

#### 패턴 2: BindTo (GameObject 수명 연동)

GameObject가 파괴되면 함께 해제. UI나 컴포넌트에서 가장 실용적.

```csharp
var handle = await AddrX.LoadAsync<Sprite>("icon_gold");
handle.BindTo(gameObject);  // OnDestroy 시 자동 해제

// 체이닝도 가능
(await AddrX.LoadAsync<Material>("mat_glow")).BindTo(gameObject);
```

#### 패턴 3: 수동 Dispose

필드에 저장하고 직접 관리.

```csharp
SafeHandle<Texture2D> _tex;

async void Start()
{
    _tex = await AddrX.LoadAsync<Texture2D>("bg_main");
}

void OnDestroy()
{
    _tex?.Dispose();
}
```

### SafeHandle\<T\> 속성

| 속성 | 타입 | 설명 |
|------|------|------|
| `Value` | `T` | 로드된 에셋 (해제/로딩 중이면 예외) |
| `IsValid` | `bool` | 유효 여부 (해제되지 않았고 핸들이 유효) |
| `Status` | `HandleStatus` | `None` / `Loading` / `Succeeded` / `Failed` |
| `Progress` | `float` | 로딩 진행률 (0.0 ~ 1.0) |

### 배치 로딩 — `AddrX.LoadBatchAsync<T>`

여러 에셋을 **병렬**로 로딩. 진행률 콜백 지원.

```csharp
var keys = new[] { "icon_a", "icon_b", "icon_c" };

SafeHandle<Sprite>[] handles = await AddrX.LoadBatchAsync<Sprite>(keys,
    onProgress: p => Debug.Log($"진행률: {p:P0}")  // 33% → 66% → 100%
);

foreach (var h in handles)
{
    h.BindTo(gameObject);
}
```

### 인스턴스화 — `AddrX.InstantiateAsync`

프리팹 로드 + Instantiate를 한번에. **Dispose하면 인스턴스도 파괴**됨.

```csharp
SafeHandle<GameObject> handle = await AddrX.InstantiateAsync("enemy_prefab", parentTransform);

// 사용 후 Dispose → 인스턴스 파괴 + 핸들 해제
handle.BindTo(gameObject);
```

### 컴포넌트 로더 — `ComponentLoader`

코드 없이 인스펙터에서 에셋 로딩. GameObject에 부착하고 Address만 입력.

```csharp
// ComponentLoader가 부착된 오브젝트에서:
// - Awake()에서 자동 로딩
// - OnDestroy()에서 자동 해제

var loader = GetComponent<ComponentLoader>();
loader.LoadedAsset;  // 로드된 에셋 (Object)
loader.IsLoaded;     // 로드 완료 여부
loader.Status;       // HandleStatus
```

인스펙터에서 `[AddressableRef]` 어트리뷰트가 에셋 피커를 제공한다.

### 로깅 — `AddrXLog`

모든 내부 로그는 `[AddrX][태그] 메시지` 형식으로 출력.

```csharp
// 로그 레벨 제어 (코드에서)
AddrXLog.Level = LogLevel.Warning;  // Warning 이상만 출력

// 직접 사용 (커스텀 로그가 필요할 때)
AddrXLog.Verbose("MySystem", "상세 정보");
AddrXLog.Info("MySystem", "일반 정보");
AddrXLog.Warning("MySystem", "경고");
AddrXLog.Error("MySystem", "에러");
```

| LogLevel | 출력 범위 |
|----------|----------|
| `Verbose` | 모든 로그 |
| `Info` | Info + Warning + Error |
| `Warning` | Warning + Error |
| `Error` | Error만 |
| `Off` | 로그 없음 |

---

## Debug (에디터 + 개발빌드)

`#if UNITY_EDITOR || DEVELOPMENT_BUILD`로 가드되어 릴리스 빌드에는 포함되지 않는다.

### HandleTracker — 핸들 추적

모든 `SafeHandle` 생성/해제를 실시간 추적.

```csharp
HandleTracker.ActiveCount;       // 현재 활성 핸들 수
HandleTracker.TotalLoaded;       // 누적 로드 수
HandleTracker.TotalReleased;     // 누적 해제 수
HandleTracker.ActiveHandles;     // IReadOnlyList<HandleInfo>

// 주소로 검색
HandleInfo? info = HandleTracker.FindByAddress("hero_prefab");

// 이벤트 구독
HandleTracker.OnHandleCreated += info => { ... };
HandleTracker.OnHandleReleased += info => { ... };
```

**HandleInfo 속성:**

| 속성 | 타입 | 설명 |
|------|------|------|
| `Id` | `int` | 고유 추적 ID |
| `Address` | `string` | 로드 주소/키 |
| `AssetType` | `Type` | 에셋 타입 |
| `CreatedAt` | `float` | 생성 시각 (realtimeSinceStartup) |
| `StackTrace` | `string` | 할당 콜스택 |
| `Age` | `float` | 생성 후 경과 시간 (초) |

### LeakDetector — 누수 감지

씬 전환 시 해제되지 않은 핸들을 자동 감지하여 콘솔에 경고.

```csharp
// 자동 감지 ON/OFF (AddrXSettings.EnableLeakDetection으로도 제어)
LeakDetector.AutoCheckOnSceneChange = true;

// 수동 체크
LeakReport report = LeakDetector.CheckForLeaks();
report.LeakCount;   // 누수 수
report.Leaks;       // IReadOnlyList<HandleInfo>
```

### DebugHUD — 인게임 오버레이

아무 GameObject에 `DebugHUD` 컴포넌트를 부착.

- **F9** 키로 토글
- Active / Loaded / Released 실시간 표시
- 상세 모드: 개별 핸들의 ID, Address, Type, Age 목록
- "Check Leaks" 버튼으로 즉석 누수 검사

---

## Editor — AddrX Manager 윈도우

**메뉴:** `Window > AddrX > Manager`
**단축키:** `Alt + Shift + A`

3개 탭 + 톱니바퀴 Settings로 구성된 통합 에디터 윈도우.

### Setup 탭

최초 실행 시 **스텝 위자드**로 초기 설정 안내. 완료 후에는 **대시보드** 표시.

**위자드 (4단계):**

| Step | 내용 | 완료 조건 |
|------|------|----------|
| 1. Package | Addressables 설치 확인 | 패키지 존재 (자동 완료) |
| 2. Addressables | Settings SO 생성 (Local 기본) | Settings 존재 |
| 3. Folders | 그룹/라벨 편집 → 폴더 일괄 생성 | 루트 폴더 존재 |
| 4. AddrX | AddrXSettings SO 생성 + 기본값 확인 | SO 존재 |

**대시보드:**
- 상태 요약 (Addressables 버전, 그룹/라벨 수)
- 에셋 상태 (Registered / Unregistered / Conflicts)
- 전체 동기화 버튼

**자동 등록:**
- `Assets/Addressables/` 하위 에셋을 AssetPostprocessor로 자동 등록
- 1뎁스 = 그룹, 2뎁스 = 라벨, 주소 = `그룹/파일명`
- 폴더 추가/삭제 → Rules SO 양방향 동기화

### ⚙ 톱니바퀴 Settings

우측 상단 기어 아이콘 클릭 시 본문을 대체하여 표시. 뒤로가기로 복귀.

| 섹션 | 내용 |
|------|------|
| **AddrX** | Log Level, Tracking, Leak Detection, Auto Initialize |
| **Addressables** | 활성 프로필, Build/Load Path 표시, Groups/Profiles 창 바로가기 |

Project Settings (`Project > AddrX`)에서도 AddrX 설정 가능.

### Tracker 탭

실행 중인 핸들을 실시간 모니터링.

- **통계 카드**: Active (활성) / Loaded (누적 로드) / Released (누적 해제)
- **핸들 테이블**: ID | Address | Type | Age (리사이즈 가능)
- **검색**: 주소 또는 타입으로 필터링
- **Stack 버튼**: 핸들 생성 시점의 콜스택 확인
- **Check Leaks 버튼**: 수동 누수 검사 실행

### Analysis 탭

Addressables 설정을 분석하는 4개 서브탭.

#### Duplicates (중복 검사)

여러 Addressable 그룹에 중복 포함된 에셋 탐지.

- 직접 등록 + 의존성(dependency)까지 검사
- 에셋별 포함된 그룹 목록 표시
- 중복 수 기준 내림차순 정렬
- 에셋 클릭 시 Project 창에서 하이라이트

```csharp
// 코드에서 직접 호출
DuplicateReport report = DuplicateScanner.Scan();
// report.Entries → List<DuplicateEntry> { AssetPath, Groups }
```

#### Health (그룹 건강도)

그룹별 건강 점수를 0~100으로 평가.

| 기준 | 임계값 | 감점 |
|------|--------|------|
| 에셋 수 | > 100개 | -0.5/개 (최대 -30) |
| 크기 | > 10MB | -3/MB (최대 -30) |
| 의존성 비율 | > 20배 | -1/배 (최대 -20) |
| 빈 그룹 | 0개 | -10 |

점수별 색상: 🟢 80+ / 🟡 50~80 / 🔴 50 미만
문제가 있으면 구체적 이슈 메시지 표시 (너무 많은 에셋, 크기 초과, 의존성 복잡도 높음, 빈 그룹).

```csharp
List<GroupScore> scores = GroupHealthScore.Evaluate();
// score.GroupName, score.Score, score.EntryCount, score.TotalSizeBytes, score.Issues
```

#### Budget (크기 예산)

그룹별 크기 예산 초과 여부 검사. 기본 예산 10MB.

```csharp
// 그룹별 커스텀 예산 설정
BundleSizeBudget.SetBudget("UI_Atlas", 5 * 1024 * 1024);  // 5MB

// 검사
List<BudgetViolation> violations = BundleSizeBudget.Check();
// violation.GroupName, violation.ActualBytes, violation.BudgetBytes, violation.OverPercent
```

초과 그룹은 빨간색 카드로 표시. 실제 크기 / 예산 / 초과율(%) / 에셋 수 확인 가능.

#### Diff (에디터/빌드 동작 차이)

에디터와 빌드에서 동작이 달라지는 위험 패턴 감지. 3가지 내장 규칙:

| 규칙 | 감지 대상 |
|------|----------|
| **ResourcesFolderRule** | Resources/ 폴더에 있으면서 Addressables에도 등록된 에셋 (이중 로딩 위험) |
| **SceneDuplicateRule** | Build Settings와 Addressables에 동시 등록된 씬 (이중 포함 위험) |
| **SpriteAtlasRule** | Addressables에 등록된 SpriteAtlas (에디터는 개별 스프라이트, 빌드는 아틀라스 단위 로딩) |

```csharp
// 커스텀 규칙 추가
BehaviorDiffChecker.AddRule(new MyCustomRule());

// MyCustomRule은 IDiffRule 인터페이스 구현
public interface IDiffRule
{
    string Name { get; }
    List<DiffWarning> Check(AddressableAssetSettings settings);
}
```

---

## 설정 파일

`Assets/AddrX/Resources/AddrXSettings.asset`

ScriptableObject로 관리. 없으면 기본값으로 자동 생성.
코드에서 `AddrXSettings.Instance`로 접근.

---

## 빠른 참조

```
로딩:
  AddrX.LoadAsync<T>(string key)           → SafeHandle<T>
  AddrX.LoadAsync<T>(AssetReference ref)   → SafeHandle<T>
  AddrX.LoadBatchAsync<T>(keys, onProgress)→ SafeHandle<T>[]
  AddrX.InstantiateAsync(key, parent)      → SafeHandle<GameObject>

해제:
  await using (var h = await AddrX.LoadAsync<T>(...)) { }   // 자동
  handle.BindTo(gameObject);                                 // GO 수명 연동
  handle.Dispose();                                          // 수동

디버그:
  HandleTracker.ActiveHandles / ActiveCount
  LeakDetector.CheckForLeaks()
  DebugHUD 컴포넌트 (F9 토글)

에디터:
  Window > AddrX > Manager (Alt+Shift+A)
  Project Settings > AddrX
  ⚙ 톱니바퀴 → AddrX + Addressables 설정
```
