# SourceGen

> **상태**: stable
> **용도**: `[Service]`, `[Table]`, `[Config]` 어트리뷰트가 붙은 클래스에서 ServerAPI 프록시와 Query 클래스를 자동 생성하는 Roslyn Source Generator

## 의존성

- `Tjdtjq5.SupaRun` 런타임 네임스페이스 — `ServiceAttribute`, `APIAttribute`, `TableAttribute`, `ConfigAttribute`, `HiddenAttribute`, `QueryOptions`, `ServerResponse`, `ErrorType`, `DeployRegistry`, `SupaRun.LocalDB/Client` 참조
- NuGet: `Microsoft.CodeAnalysis.CSharp` 4.3.0, `Microsoft.CodeAnalysis.Analyzers` 3.3.4

## 구조

| 파일 | 설명 |
|------|------|
| `ServiceGenerator.cs` | `[Service]` + `[API]` 어트리뷰트 기반. `ServerAPI.{ServiceName}.{Method}()` 프록시 생성 |
| `TableQueryGenerator.cs` | `[Table]` / `[Config]` 어트리뷰트 기반. `{ClassName}Query` 빌더 + `ServerAPI.Query{Name}s()` 생성 |
| `Tjdtjq5.SupaRun.SourceGen.csproj` | netstandard2.0 Roslyn Source Generator 프로젝트 |

## API (생성되는 코드)

### ServiceGenerator → `ServerAPI.g.cs`

`[Service]` 클래스의 `[API]` 메서드마다 `ServerAPI.{ServiceName}.{MethodName}()` 정적 메서드 생성.

```csharp
// 입력
[Service]
public class CurrencyService
{
    public CurrencyService(IGameDB db) { ... }

    [API]
    public async Task<long> AddGold(string userId, int amount) { ... }
}

// 생성
public static partial class ServerAPI
{
    public static partial class CurrencyService
    {
        public static async Task<ServerResponse<long>> AddGold(string userId, int amount)
        {
            // UNITY_EDITOR: 미배포 시 LocalDB로 로컬 실행
            // 빌드/배포 후: HTTP POST "api/CurrencyService/AddGold"
        }
    }
}
```

**에디터 로컬 실행 경로**:
- `DeployRegistry.IsDeployed("ServiceName/MethodName")`이 false면 `SupaRun.LocalDB`로 서비스 인스턴스를 직접 생성하여 로컬 실행
- 생성자 파라미터 의존성을 재귀적으로 해결 (`IGameDB` → `SupaRun.LocalDB`, 다른 Service → `new` 생성)
- `#if UNITY_EDITOR` 블록으로 감싸 빌드에서는 완전히 제외

**빌드/배포 경로**:
- `SupaRun.Client.PostAsync<T>("api/{endpoint}", body)` 호출

### TableQueryGenerator → `{ClassName}Query.g.cs` + `ServerAPI.Queries.g.cs`

`[Table]` / `[Config]` 클래스의 public 필드(`[Hidden]` 제외)에서 타입별 쿼리 메서드를 가진 Query 빌더 클래스 생성.

```csharp
// 입력
[Table]
public class PlayerData
{
    public string id;
    public int level;
    public long gold;
    public bool isVip;
}

// 생성: PlayerDataQuery.g.cs
public class PlayerDataQuery
{
    internal QueryOptions _options = new QueryOptions();

    // string → Equals, Contains
    public PlayerDataQuery IdEquals(string value) { ... }
    public PlayerDataQuery IdContains(string value) { ... }

    // int/long/float/double → Equals, GreaterThan, LessThan, Between
    public PlayerDataQuery LevelEquals(int value) { ... }
    public PlayerDataQuery LevelGreaterThan(int value) { ... }
    public PlayerDataQuery LevelLessThan(int value) { ... }
    public PlayerDataQuery LevelBetween(int min, int max) { ... }

    // bool → Equals
    public PlayerDataQuery IsVipEquals(bool value) { ... }

    // 모든 필드: OrderBy{Field}Asc/Desc
    public PlayerDataQuery OrderByLevelAsc() { ... }
    public PlayerDataQuery OrderByLevelDesc() { ... }

    // 페이징
    public PlayerDataQuery Limit(int count) { ... }
    public PlayerDataQuery Offset(int count) { ... }
}

// 생성: ServerAPI.Queries.g.cs
public static partial class ServerAPI
{
    public static async Task<ServerResponse<List<PlayerData>>> QueryPlayerDatas(
        Func<PlayerDataQuery, PlayerDataQuery> queryBuilder) { ... }
}
```

**필드 타입별 생성 메서드**:

| 타입 | 메서드 |
|------|--------|
| `string` | `{Field}Equals`, `{Field}Contains` |
| `int`, `long`, `float`, `double` | `{Field}Equals`, `{Field}GreaterThan`, `{Field}LessThan`, `{Field}Between` |
| `bool` | `{Field}Equals` |
| `DateTime` | `{Field}After`, `{Field}Before` |

## 내부 데이터 모델

### ServiceGenerator

| 클래스 | 필드 | 설명 |
|--------|------|------|
| `SvcInfo` | `Name`, `FullName`, `CtorParams`, `Methods` | 서비스 클래스 정보 |
| `CtorParam` | `TypeName`, `FullTypeName`, `IsGameDB` | 생성자 파라미터 |
| `MInfo` | `Name`, `Inner`, `IsAsync`, `Params` | API 메서드 정보 |
| `CodeWriter` | `_sb`, `_indent` | 들여쓰기 관리 코드 빌더 |

### TableQueryGenerator

| 클래스 | 필드 | 설명 |
|--------|------|------|
| `DataInfo` | `Name`, `Fields` | 데이터 클래스 정보 |
| `FieldDef` | `Name`, `Type`, `TypeKind` | 필드 정보 |
| `FieldTypeKind` | enum | `String`, `Numeric`, `Bool`, `DateTime`, `Other` |

## 주의사항

- **netstandard2.0 필수**: Roslyn Source Generator는 netstandard2.0 타겟이어야 Unity에서 동작
- **IIncrementalGenerator 사용**: 성능을 위해 `ISourceGenerator` 대신 증분 생성기 사용
- **`[Hidden]` 필드 제외**: `HiddenAttribute`가 붙은 필드는 Query 메서드 생성에서 제외
- **의존성 재귀 해결**: ServiceGenerator는 생성자에 다른 Service가 주입되는 경우도 재귀적으로 인스턴스화 코드 생성
- **partial class 패턴**: `ServerAPI`는 `partial class`로 선언되어 ServiceGenerator와 TableQueryGenerator가 각각 파트를 생성
- **CodeWriter 공유**: 두 Generator가 동일한 `CodeWriter` 클래스를 사용하지만 각각 별도 네임스페이스에 정의 (ServiceGenerator 내부에 선언)
