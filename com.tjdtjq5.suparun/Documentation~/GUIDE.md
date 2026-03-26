# SupaRun 사용 가이드

## 시작하기

SupaRun 패키지를 사용하면 서버 코드를 Unity 안에서 작성하고,
배포 없이도 Unity Play 모드에서 바로 테스트할 수 있습니다.

---

## 1. 데이터 정의: [Table]

게임 플레이 중 변하는 데이터 (플레이어, 아이템, 길드 등)

### 기본 사용법

```csharp
using Tjdtjq5.SupaRun;

[Table]
public class Player
{
    [PrimaryKey] public string id;
    public string nickname;
    public int level;
    public int gold;
}
```

### 필드 어트리뷰트

```csharp
[Table]
public class Player
{
    // ── 필수 ──
    [PrimaryKey] public string id;         // 기본 키 (필수, 고유 식별자)

    // ── 제약 조건 ──
    [NotNull] public string nickname;      // null 불가
    [MaxLength(20)] public string bio;     // 문자열 최대 20자
    [Unique] public string email;          // 중복 불가
    [Default(1)] public int level;         // 기본값 1 (값을 안 넣으면 자동)
    [Default(100)] public int gold;        // 기본값 100

    // ── 관계 ──
    [ForeignKey(typeof(Guild))] public string guildId;  // 다른 테이블 참조

    // ── 검색 성능 ──
    [Index] public int level;              // 인덱스 (자주 검색하는 필드에)

    // ── 보안 ──
    [Hidden] public string secretData;     // 클라이언트에 노출 안 됨

    // ── 시간 자동 기록 ──
    [CreatedAt] public long createdAt;     // 생성 시간 (Save 시 자동, 최초 1회)
    [UpdatedAt] public long updatedAt;     // 수정 시간 (Save 시마다 자동 갱신)

    // ── 마이그레이션 ──
    [RenamedFrom("nickname")]              // 필드 이름 변경 시 (DB 데이터 유지)
    public string displayName;
}
```

### 데이터 읽기 (클라이언트)

```csharp
// 단건 조회
var result = await SupaRun.Get<Player>("player_001");
if (result.success)
    Debug.Log($"닉네임: {result.data.nickname}");

// 조건 검색 (Source Generator 자동 생성)
var topPlayers = await ServerAPI.QueryPlayers(q => q
    .LevelGreaterThan(50)
    .OrderByGoldDesc()
    .Limit(10)
);

foreach (var p in topPlayers.data)
    Debug.Log($"{p.nickname} Lv.{p.level} Gold:{p.gold}");
```

### 쿼리에서 사용 가능한 메서드

| 필드 타입 | 자동 생성 메서드 |
|----------|----------------|
| `string` | `{Field}Equals(string)`, `{Field}Contains(string)` |
| `int`, `long`, `float`, `double` | `{Field}Equals(T)`, `{Field}GreaterThan(T)`, `{Field}LessThan(T)`, `{Field}Between(T, T)` |
| `bool` | `{Field}Equals(bool)` |
| `DateTime` | `{Field}After(DateTime)`, `{Field}Before(DateTime)` |
| 모든 타입 | `OrderBy{Field}Asc()`, `OrderBy{Field}Desc()` |
| - | `Limit(int)`, `Offset(int)` |

### N:N 관계

중간 테이블을 직접 정의합니다:

```csharp
[Table]
public class Guild
{
    [PrimaryKey] public string id;
    public string name;
}

[Table]
public class GuildMember
{
    [PrimaryKey] public string id;
    [ForeignKey(typeof(Player))] public string playerId;
    [ForeignKey(typeof(Guild))] public string guildId;
    public string role;        // "leader", "member"
    [CreatedAt] public long joinedAt;
}
```

---

## 2. 설정 데이터: [Config]

게임 기획 시 정하는 고정 데이터 (상점 가격, 레벨 테이블, 스킬 스펙 등)
클라이언트에서 **읽기만** 가능.

### 기본 사용법

```csharp
using Tjdtjq5.SupaRun;

[Config]
public class ShopItem
{
    [PrimaryKey] public string id;
    [NotNull] public string name;
    public int price;
    public string iconPath;
    public string description;
}

[Config]
public class LevelTable
{
    [PrimaryKey] public int level;
    public int requiredExp;
    public int maxHp;
    public int maxMp;
}
```

### 데이터 읽기

```csharp
// 전체 조회
var allItems = await SupaRun.GetAll<ShopItem>();
foreach (var item in allItems.data)
    Debug.Log($"{item.name}: {item.price}G");

// 단건 조회
var sword = await SupaRun.Get<ShopItem>("sword_01");

// 조건 검색
var expensive = await ServerAPI.QueryShopItems(q => q
    .PriceGreaterThan(500)
    .OrderByPriceAsc()
);
```

### [Table] vs [Config] 차이

| | [Table] | [Config] |
|---|---|---|
| 용도 | 플레이어 데이터 (변동) | 게임 설정 (고정) |
| 예시 | Player, Item, GuildMember | ShopItem, LevelTable |
| 누가 수정 | [Service]에서만 | 개발자 (대시보드에서) |
| 클라이언트 | 읽기만 | 읽기만 |
| 캐싱 | 매번 조회 | 서버 메모리 캐싱 |

---

## 3. 서버 로직: [Service]

데이터를 **수정**하는 모든 작업은 [Service]으로 작성합니다.
(상점 구매, 레벨업, 닉네임 변경, 매칭 등)

### 기본 사용법

```csharp
using System.Threading.Tasks;
using Tjdtjq5.SupaRun;

[Service]
public class PlayerService
{
    readonly IGameDB _db;
    public PlayerService(IGameDB db) => _db = db;

    [API]
    public async Task<Player> CreatePlayer(string id, string nickname)
    {
        var player = new Player
        {
            id = id,
            nickname = nickname,
            level = 1
            // gold는 [Default(100)]으로 자동 설정
            // createdAt, updatedAt는 자동 설정
        };
        await _db.Save(player);
        return player;
    }

    [API]
    public async Task ChangeNickname(string playerId, string newNickname)
    {
        var player = await _db.Get<Player>(playerId);
        player.nickname = newNickname;
        await _db.Save(player);
    }
}
```

### 클라이언트에서 호출

Source Generator가 자동으로 `ServerAPI.PlayerService`를 생성합니다.

```csharp
// 타입 안전 프록시 (자동완성 지원)
var result = await ServerAPI.PlayerService.CreatePlayer("p1", "홍길동");
if (result.success)
    Debug.Log($"생성 완료: {result.data.nickname} Lv.{result.data.level}");

await ServerAPI.PlayerService.ChangeNickname("p1", "새닉네임");
```

### 상점 구매 예시 (트랜잭션)

```csharp
[Service]
public class ShopService
{
    readonly IGameDB _db;
    public ShopService(IGameDB db) => _db = db;

    [API]
    public async Task<BuyResult> BuyItem(string playerId, string itemId)
    {
        return await _db.Transaction(async tx =>
        {
            var player = await tx.Get<Player>(playerId);
            var shopItem = await tx.Get<ShopItem>(itemId);

            if (player.gold < shopItem.price)
                return new BuyResult { success = false, error = "골드 부족" };

            player.gold -= shopItem.price;
            await tx.Save(player);
            await tx.Save(new Item
            {
                id = System.Guid.NewGuid().ToString(),
                playerId = playerId,
                itemName = shopItem.name,
                count = 1
            });

            return new BuyResult { success = true, remainingGold = player.gold };
        });
    }
}

// 결과 DTO
public class BuyResult
{
    public bool success;
    public string error;
    public int remainingGold;
}
```

### 클라이언트에서:

```csharp
var result = await ServerAPI.ShopService.BuyItem("p1", "sword_01");
if (result.success)
    Debug.Log($"구매 성공! 남은 골드: {result.data.remainingGold}");
else
    Debug.Log($"구매 실패: {result.data.error}");
```

### IGameDB 메서드

| 메서드 | 설명 |
|--------|------|
| `Get<T>(primaryKey)` | 단건 조회 |
| `GetAll<T>()` | 전체 조회 |
| `Save<T>(entity)` | 저장 (생성 또는 수정) |
| `Delete<T>(primaryKey)` | 삭제 |
| `Query<T>(queryBuilder)` | 조건 검색 |
| `Transaction(action)` | 트랜잭션 (여러 작업을 하나로) |

---

## 4. 개발 흐름

### 개발 중 (서버 없이)

```
1. [Table], [Config] 클래스 작성
2. [Service] 클래스 작성
3. Unity Play 클릭
4. ServerAPI.XXX.Method() 호출
   → LocalGameDB에서 즉시 실행 (서버 불필요)
5. 결과 확인
```

**서버, Docker, .NET SDK 아무것도 필요 없습니다.**

### 배포 후

```
1. [배포] 클릭
2. 배포된 로직은 자동으로 Cloud Run 서버 호출
3. 미배포 로직은 여전히 LocalGameDB 실행
4. 새 로직 추가 → 로컬에서 테스트 → [배포] → 서버로 전환
```

### 로직별 자동 판단

```
ServerAPI.ShopService.BuyItem()    → 배포됨 → 서버 호출
ServerAPI.ShopService.SellItem()   → 미배포 → 로컬 실행 (방금 작성)
ServerAPI.PlayerService.Create()   → 배포됨 → 서버 호출
ServerAPI.MatchService.FindMatch() → 미배포 → 로컬 실행

개발자가 수동으로 전환할 필요 없음. 자동.
```

---

## 5. 에러 처리

### ServerResponse 구조

```csharp
var result = await ServerAPI.PlayerService.CreatePlayer("p1", "홍길동");

result.success    // true/false
result.data       // 반환 데이터 (Player)
result.error      // 에러 메시지 (실패 시)
result.errorType  // 에러 유형
result.statusCode // HTTP 상태 코드
```

### 에러 타입

| ErrorType | 설명 | 대처 |
|-----------|------|------|
| `None` | 성공 | - |
| `NetworkError` | 네트워크 끊김 | 재접속 안내 |
| `Timeout` | 시간 초과 | 자동 재시도 (내부) |
| `ServerError` | 서버 오류 | 자동 재시도 (내부) |
| `AuthExpired` | 토큰 만료 | 자동 갱신 (내부) |
| `AuthFailed` | 인증 실패 | 로그인 화면 |
| `BadRequest` | 잘못된 요청 | 에러 메시지 표시 |
| `NotFound` | 없음 | 에러 메시지 표시 |
| `RateLimit` | 요청 과다 | 잠시 후 재시도 |

### 에러 처리 예시

```csharp
var result = await ServerAPI.ShopService.BuyItem("p1", "sword_01");

if (result.success)
{
    ShowResult(result.data);
}
else if (result.errorType == ErrorType.NetworkError)
{
    UIDialog.Show("연결 끊김", "재접속 하시겠습니까?");
}
else
{
    UIToast.Show($"에러: {result.error}");
}
```

---

## 6. 어트리뷰트 전체 목록

### 클래스 어트리뷰트

| 어트리뷰트 | 대상 | 설명 |
|-----------|:----:|------|
| `[Table]` | class | 게임 데이터 테이블 (플레이어 데이터 등) |
| `[Config]` | class | 설정 데이터 (상점 가격, 레벨 테이블 등) |
| `[Service]` | class | 서버 로직 (데이터 수정하는 비즈니스 로직) |

### 메서드 어트리뷰트

| 어트리뷰트 | 대상 | 설명 |
|-----------|:----:|------|
| `[API]` | method | API로 노출할 메서드. 생략 시 모든 public 메서드 대상 |

### 필드 어트리뷰트

| 어트리뷰트 | 설명 | 예시 |
|-----------|------|------|
| `[PrimaryKey]` | 기본 키 (필수) | `[PrimaryKey] public string id;` |
| `[ForeignKey(typeof(T))]` | 다른 테이블 참조 | `[ForeignKey(typeof(Player))] public string playerId;` |
| `[Index]` | 검색 성능 향상 | `[Index] public int level;` |
| `[Unique]` | 중복 불가 | `[Unique] public string email;` |
| `[NotNull]` | null 불가 | `[NotNull] public string name;` |
| `[Default(값)]` | 기본값 | `[Default(100)] public int gold;` |
| `[MaxLength(n)]` | 최대 길이 | `[MaxLength(20)] public string nickname;` |
| `[Hidden]` | 클라이언트 비노출 | `[Hidden] public string hash;` |
| `[RenamedFrom("이전")]` | 필드 이름 변경 | `[RenamedFrom("nick")] public string nickname;` |
| `[CreatedAt]` | 생성 시간 자동 | `[CreatedAt] public long createdAt;` |
| `[UpdatedAt]` | 수정 시간 자동 | `[UpdatedAt] public long updatedAt;` |

### [CreatedAt] / [UpdatedAt] 지원 타입

| 필드 타입 | 저장 형식 |
|----------|----------|
| `long` | Unix timestamp (초) |
| `double` | Unix timestamp (밀리초/1000) |
| `DateTime` | UTC DateTime |
| `string` | ISO 8601 형식 |

---

## 7. 폴더 구조 권장

```
Assets/Scripts/
├── Table/               ← [Table] 클래스
│   ├── Player.cs
│   ├── Item.cs
│   └── GuildMember.cs
├── Config/              ← [Config] 클래스
│   ├── ShopItem.cs
│   └── LevelTable.cs
├── Service/             ← [Service] 클래스
│   ├── PlayerService.cs
│   ├── ShopService.cs
│   └── MatchService.cs
└── Game/                ← 게임 코드
    ├── UIShop.cs
    └── GameManager.cs
```
