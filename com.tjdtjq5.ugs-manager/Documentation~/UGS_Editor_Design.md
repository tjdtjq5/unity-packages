# UGS Manager EditorWindow 설계

## 개요

UGS CLI(`ugs`)를 래핑한 Unity EditorWindow. Dashboard 없이 에디터 안에서 UGS 서비스를 관리한다.

## 아키텍처

### 클래스 계층

```
EditorTabBase (기존, 색상 + 드로잉 유틸)
  └── UGSTabBase (신규, CLI 실행 + UGS 공통)
        ├── EnvironmentTab
        ├── RemoteConfigTab
        ├── CloudCodeTab
        └── DeployTab

UGSWindow (EditorWindow, 탭 관리 + 상태바)
UGSCliRunner (static 유틸, Process 래핑)
```

### 기존 패턴과의 관계

| 기존 | UGS |
|------|-----|
| TableWindow | UGSWindow |
| TableTabBase | UGSTabBase |
| EnemyTableTab | EnvironmentTab, RemoteConfigTab, ... |
| (없음) | UGSCliRunner (CLI 전용 유틸) |

EditorTabBase를 그대로 상속하므로 `DrawSectionFoldout`, `BeginBody`, `DrawColorBtn` 등 기존 드로잉 유틸 전부 사용 가능.

---

## 폴더 구조

```
_Editor/
└── UGS/
    ├── UGSWindow.cs              # EditorWindow 메인 (Tools > UGS Manager, Ctrl+Shift+U)
    ├── UGSTabBase.cs             # 탭 베이스 (CLI 결과 캐싱, Refresh, 에러 표시)
    ├── UGSCliRunner.cs           # CLI 실행 유틸 (async Process, 타임아웃, JSON 파싱)
    ├── EnvironmentTab.cs         # 환경 관리 탭
    ├── RemoteConfigTab.cs        # Remote Config 탭
    ├── CloudCodeTab.cs           # Cloud Code 탭
    ├── DeployTab.cs              # 배포 탭
    └── UGS_Editor_Design.md     # 이 문서
```

---

## 핵심 클래스 설계

### UGSCliRunner (static 유틸)

```csharp
/// CLI 실행 결과
public struct CliResult
{
    public bool Success;
    public string Output;
    public string Error;
    public int ExitCode;
}

public static class UGSCliRunner
{
    // 동기 실행 (짧은 명령용)
    public static CliResult Run(string arguments);

    // 비동기 실행 (배포 등 긴 명령용)
    public static void RunAsync(string arguments, Action<CliResult> onComplete);

    // JSON 출력 파싱 (-j 플래그)
    public static T RunJson<T>(string arguments);

    // CLI 설치 여부 확인
    public static bool IsInstalled();

    // 로그인 상태 확인
    public static bool IsLoggedIn();

    // 현재 설정 조회
    public static string GetProjectId();
    public static string GetEnvironment();
    public static string GetCliVersion();
}
```

**구현 포인트:**
- `System.Diagnostics.Process` 사용
- `RedirectStandardOutput` + `RedirectStandardError`
- `-j` (JSON) 플래그로 파싱 용이한 출력
- `-q` (quiet) 플래그로 불필요한 로그 제거
- 타임아웃: 기본 30초, Deploy는 120초
- EditorApplication.update에서 비동기 완료 체크

### UGSTabBase

```csharp
public abstract class UGSTabBase : EditorTabBase
{
    // CLI 실행 상태
    protected bool _isLoading;
    protected string _lastError;
    protected DateTime _lastRefresh;

    // 공통 UI
    protected void DrawToolbar();           // [Refresh] + 마지막 갱신 시간 + 로딩 스피너
    protected void DrawError();             // 에러 메시지 (빨간 박스)
    protected void DrawLoading();           // 로딩 표시
    protected void DrawNoCliWarning();      // CLI 미설치 경고

    // 데이터 캐싱
    protected abstract void FetchData();    // CLI 호출 → 데이터 캐싱
    protected virtual bool AutoRefresh => false;  // 자동 갱신 여부

    // 라이프사이클
    public override void OnEnable() => FetchData();
    public override void OnUpdate()
    {
        if (AutoRefresh && 마지막 갱신 후 5초 경과)
            FetchData();
    }
}
```

### UGSWindow

```csharp
public class UGSWindow : EditorWindow
{
    [MenuItem("Tools/UGS Manager %#u")]  // Ctrl+Shift+U
    static void Open();

    // 탭 목록
    UGSTabBase[] _tabs;
    int _activeTab;

    // 상태바 정보 (캐싱)
    string _environment;
    string _cliVersion;
    bool _isLoggedIn;

    void OnEnable()
    {
        _tabs = new UGSTabBase[]
        {
            new EnvironmentTab(),
            new RemoteConfigTab(),
            new CloudCodeTab(),
            new DeployTab(),
        };

        // 상태바 정보 초기 로드
        RefreshStatus();
    }

    void OnGUI()
    {
        DrawTabBar();       // 탭 전환 버튼
        DrawContent();      // 현재 탭 콘텐츠
        DrawStatusBar();    // 하단 상태바
    }

    void DrawStatusBar()
    {
        // ● production | CLI: 1.8.0 | ✓ Logged in
        // 또는
        // ⚠ CLI not found | [Install Guide]
    }
}
```

---

## 탭별 상세 설계

### 1. EnvironmentTab

```
┌─ 환경 관리 ──────────────────────────────────┐
│                                              │
│  ● 현재 환경: production                      │
│                                              │
│  ┌──────────────┬──────────────────┬────────┐│
│  │ 환경 이름     │ Environment ID    │ 액션   ││
│  ├──────────────┼──────────────────┼────────┤│
│  │ ● production │ ab866b34-c736... │ [활성] ││
│  │   staging    │ cd123e56-f789... │ [전환] ││
│  └──────────────┴──────────────────┴────────┘│
│                                              │
│  ▸ 새 환경 생성                                │
│    이름: [__________] [생성]                   │
│                                              │
└──────────────────────────────────────────────┘
```

**CLI 매핑:**
| UI 동작 | CLI 명령 |
|---------|---------|
| 목록 로드 | `ugs env list -j` |
| 환경 전환 | `ugs config set environment-name <name>` |
| 환경 생성 | `ugs env create <name>` |

**데이터 모델:**
```csharp
class EnvironmentInfo
{
    public string Name;
    public string Id;
    public bool IsActive;
}
List<EnvironmentInfo> _environments;
```

### 2. RemoteConfigTab

```
┌─ Remote Config ──────────────────────────────┐
│  [Refresh]  [Fetch ↓]  [Push ↑]              │
│                                              │
│  ▾ 현재 설정값 (7개 키)                        │
│  ┌──────────────────┬───────┬───────┬──────┐ │
│  │ 키                │ 타입   │ 값     │ 액션 │ │
│  ├──────────────────┼───────┼───────┼──────┤ │
│  │ enemy_hp_multi.. │ FLOAT │ [1.0] │ [✕]  │ │
│  │ enemy_spawn_co.. │ INT   │ [10]  │ [✕]  │ │
│  │ daily_reward_g.. │ INT   │ [100] │ [✕]  │ │
│  │ drop_rate_bonus  │ FLOAT │ [1.0] │ [✕]  │ │
│  │ event_active     │ BOOL  │ [☐]   │ [✕]  │ │
│  │ event_name       │ STR   │ [none]│ [✕]  │ │
│  │ welcome_message  │ STR   │ [Sur.]│ [✕]  │ │
│  └──────────────────┴───────┴───────┴──────┘ │
│                                              │
│  ▸ 키 추가                                    │
│    키: [________] 타입: [FLOAT▾] 값: [___]    │
│    [추가]                                     │
│                                              │
│  ▸ .rc 파일 경로                               │
│    [Assets/.../RemoteConfig.rc] [열기]         │
│                                              │
└──────────────────────────────────────────────┘
```

**CLI 매핑:**
| UI 동작 | CLI 명령 |
|---------|---------|
| 전체 조회 | `ugs remote-config get -j` |
| 값 수정 | `ugs remote-config set <key> <value>` → 개별 Apply |
| Fetch (서버→로컬) | `ugs fetch ./path -s remote-config` |
| Push (로컬→서버) | `ugs deploy ./path -s remote-config` |

**데이터 모델:**
```csharp
class RemoteConfigEntry
{
    public string Key;
    public string Type;     // FLOAT, INT, BOOL, STRING
    public string Value;
    public string EditValue; // 편집 중인 값 (Apply 전)
    public bool IsDirty;     // 수정됨 표시
}
List<RemoteConfigEntry> _entries;
```

**수정 플로우:**
```
값 편집 → IsDirty = true (노란색 하이라이트)
  → [Apply] 클릭 → ugs remote-config set → Refresh
  → [Revert] 클릭 → EditValue = Value, IsDirty = false
```

### 3. CloudCodeTab

```
┌─ Cloud Code ─────────────────────────────────┐
│  [Refresh]  [Deploy All]                     │
│                                              │
│  ▾ Scripts (3개)                              │
│  ┌────────────────────┬────────────┬────────┐│
│  │ 이름                │ 최종 수정    │ 액션   ││
│  ├────────────────────┼────────────┼────────┤│
│  │ HelloWorld         │ 2026-03-10 │ [삭제] ││
│  │ GrantDailyReward   │ 2026-03-10 │ [삭제] ││
│  │ DebugContext       │ 2026-03-10 │ [삭제] ││
│  └────────────────────┴────────────┴────────┘│
│                                              │
│  ▸ 배포 설정                                   │
│    스크립트 폴더: [Assets/.../CloudCode/]       │
│    [선택]  [Deploy]                            │
│                                              │
│  ▾ 모듈 (0개)                                  │
│    (등록된 모듈 없음)                            │
│                                              │
└──────────────────────────────────────────────┘
```

**CLI 매핑:**
| UI 동작 | CLI 명령 |
|---------|---------|
| 스크립트 목록 | `ugs cc scripts list -j` |
| 스크립트 배포 | `ugs deploy ./CloudCode -s cloud-code-scripts` |
| 스크립트 삭제 | `ugs cc scripts delete <name>` |
| 모듈 목록 | `ugs cc modules list -j` |

### 4. DeployTab

```
┌─ Deploy ─────────────────────────────────────┐
│                                              │
│  ▾ 배포 대상 선택                               │
│    ☑ Remote Config    ./11_Studdy/UGS/       │
│    ☑ Cloud Code       ./11_Studdy/UGS/Cloud/ │
│    ☐ Economy                                 │
│    ☐ Leaderboards                            │
│                                              │
│    [Deploy Selected]  [Deploy All]            │
│                                              │
│  ▾ Fetch (서버 → 로컬)                         │
│    [Fetch All]                                │
│                                              │
│  ▾ 실행 로그                                   │
│  ┌──────────────────────────────────────────┐ │
│  │ [14:30:25] Deploying remote-config...    │ │
│  │ [14:30:28] ✓ remote-config deployed      │ │
│  │ [14:30:28] Deploying cloud-code...       │ │
│  │ [14:30:35] ✓ cloud-code deployed (3 scripts)│
│  │ [14:30:35] Deploy complete!              │ │
│  └──────────────────────────────────────────┘ │
│                                              │
└──────────────────────────────────────────────┘
```

**CLI 매핑:**
| UI 동작 | CLI 명령 |
|---------|---------|
| 전체 배포 | `ugs deploy ./path` |
| 선택 배포 | `ugs deploy ./path -s <service>` |
| 전체 Fetch | `ugs fetch ./path` |

---

## 비동기 CLI 실행 패턴

에디터 프리징 방지를 위한 비동기 패턴:

```csharp
// UGSCliRunner 내부
public static void RunAsync(string args, Action<CliResult> onComplete)
{
    var process = new Process { ... };
    process.EnableRaisingEvents = true;
    process.Exited += (s, e) =>
    {
        var result = new CliResult { ... };
        // 메인 스레드에서 콜백 실행
        EditorApplication.delayCall += () => onComplete(result);
    };
    process.Start();
}
```

**탭에서 사용:**
```csharp
protected override void FetchData()
{
    _isLoading = true;
    UGSCliRunner.RunAsync("env list -j", result =>
    {
        _isLoading = false;
        if (result.Success)
            _environments = JsonUtility.FromJson<List<EnvironmentInfo>>(result.Output);
        else
            _lastError = result.Error;
    });
}
```

---

## 상태바 설계

```
┌──────────────────────────────────────────────────────┐
│ ● production | CLI: 1.8.0 | ✓ Logged in | [Settings]│
└──────────────────────────────────────────────────────┘
```

| 항목 | 소스 | 갱신 시점 |
|------|------|----------|
| 환경 이름 | `ugs config get environment-name` | 윈도우 열 때 + 환경 전환 시 |
| CLI 버전 | `ugs --version` | 윈도우 열 때 |
| 로그인 상태 | `ugs status` | 윈도우 열 때 |

CLI 미설치 시:
```
┌──────────────────────────────────────────────────────┐
│ ⚠ UGS CLI가 설치되지 않았습니다.                       │
│   npm install -g ugs  명령으로 설치하세요.              │
└──────────────────────────────────────────────────────┘
```

---

## 에러 처리

| 상황 | UI 표시 |
|------|---------|
| CLI 미설치 | 전체 탭 비활성 + 설치 안내 |
| 미로그인 | 전체 탭 비활성 + 로그인 안내 |
| 명령 실패 | 해당 탭에 빨간 에러 박스 |
| 타임아웃 | "CLI 응답 시간 초과" 메시지 |
| 네트워크 오류 | CLI stderr 내용 그대로 표시 |

---

## 구현 순서

| 단계 | 파일 | 내용 |
|------|------|------|
| 1 | UGSCliRunner.cs | CLI 실행 유틸 (동기 + 비동기) |
| 2 | UGSTabBase.cs | 탭 베이스 (로딩/에러/Refresh UI) |
| 3 | UGSWindow.cs | 윈도우 껍데기 (탭바 + 상태바) |
| 4 | EnvironmentTab.cs | 환경 관리 (가장 단순) |
| 5 | RemoteConfigTab.cs | Remote Config (인라인 편집) |
| 6 | CloudCodeTab.cs | Cloud Code (목록 + 배포) |
| 7 | DeployTab.cs | 통합 배포 (로그 출력) |

---

## 확장 계획 (향후)

| 탭 | CLI 명령 | 우선순위 |
|----|---------|---------|
| EconomyTab | `ugs economy` | 중 |
| LeaderboardsTab | `ugs leaderboards` | 중 |
| CloudSaveTab | `ugs cloud-save` | 낮 |
| SchedulerTab | `ugs scheduler` | 낮 |
| TriggersTab | `ugs triggers` | 낮 |
| LobbyTab | `ugs lobby` | 낮 |
