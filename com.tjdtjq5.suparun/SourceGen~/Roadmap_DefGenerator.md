# Config → Def 자동 생성 Source Generator

## 상태
wip

## 용도
[Config] 클래스에서 PascalCase Def 클래스를 자동 생성하는 Source Generator 확장. 수동 ServerDefs 매핑 코드를 완전히 제거.

## 의존성
- InGameCore DataRegistry 범용화 (Pillar 1) — id convention 기반 등록 필요

## 포함 기능

### Source Generator (DefGenerator.cs)
- **Def 클래스 자동 생성** — `[Config] EnemyConfig` → `EnemyDef.g.cs` 생성
  - snake_case 필드 → PascalCase 프로퍼티 (`max_hp` → `MaxHp { get; set; }`)
  - `[PrimaryKey]` 필드 → `string Id { get; set; }` 프로퍼티 추가 (DataRegistry convention)
  - 기본 타입 (int, float, string, bool) 그대로 매핑
- **[Json] 필드 자동 파싱** — `[Json] string animation_clips` → `List<EnemyAnimClipDef> AnimationClips { get; set; }`
  - [Json] 필드의 제네릭 타입 힌트 필요: `[Json(typeof(List<EnemyAnimClipDef>))]` 어트리뷰트 확장
  - 또는 주석/별도 어트리뷰트로 타입 지정
- **enum 필드 자동 변환** — `string trigger_type` + `[EnumType(typeof(SkillTriggerType))]` → `SkillTriggerType TriggerType { get; set; }`
- **Config → Def 변환 메서드 생성** — `EnemyDef.FromConfig(EnemyConfig c)` static 메서드 자동 생성
  - 기본 필드: 직접 대입
  - [Json] 필드: `JsonConvert.DeserializeObject<T>()` 호출
  - enum 필드: `Enum.TryParse()` 호출
- **Def 클래스명 규칙** — `{ConfigName}` → `Config` 접미사 제거 → `Def` 접미사 추가
  - `EnemyConfig` → `EnemyDef`
  - `SkillConfig` → `SkillDef`
  - `WaveConfig` → `WaveDef` (WaveConfigConfig → WaveDef 는 아님. Config 접미사 1회만 제거)

### Runtime 확장
- **[Json] 어트리뷰트 확장** — `Type` 프로퍼티 추가: `[Json(typeof(List<EnemyAnimClipDef>))]`
- **[EnumType] 어트리뷰트 신규** — enum 변환 대상 필드 마킹: `[EnumType(typeof(SkillTriggerType))]`
- **SupaRun.GetAllDef<T>() 편의 API** — Config 조회 → Def 변환 → 리스트 반환 (선택)

## 구조

```
SourceGen~/
├── TableQueryGenerator.cs    # 기존 (변경 없음)
├── ServiceGenerator.cs       # 기존 (변경 없음)
├── DefGenerator.cs           # 신규 — [Config] → Def 클래스 생성
└── Feature.md                # 업데이트

Runtime/Attributes/
├── ConfigAttribute.cs        # 기존 (변경 없음)
├── JsonAttribute.cs          # 수정 — Type 프로퍼티 추가
└── EnumTypeAttribute.cs      # 신규 — enum 변환 마킹
```

## 생성 예시

### 입력 (Config)
```csharp
[Config]
public class EnemyConfig
{
    [PrimaryKey] public string id;
    public string display_name;
    public float max_hp;
    public float move_speed;
    [Json(typeof(List<EnemyAnimClipDef>))] public string animation_clips;
    [ForeignKey(typeof(DropTableConfig))] public string drop_table_id;
}
```

### 출력 (생성된 Def)
```csharp
// EnemyDef.g.cs (자동 생성)
public class EnemyDef
{
    public string Id { get; set; }
    public string DisplayName { get; set; }
    public float MaxHp { get; set; }
    public float MoveSpeed { get; set; }
    public List<EnemyAnimClipDef> AnimationClips { get; set; }
    public string DropTableId { get; set; }

    public static EnemyDef FromConfig(EnemyConfig c)
    {
        return new EnemyDef
        {
            Id = c.id,
            DisplayName = c.display_name,
            MaxHp = c.max_hp,
            MoveSpeed = c.move_speed,
            AnimationClips = string.IsNullOrEmpty(c.animation_clips)
                ? new List<EnemyAnimClipDef>()
                : JsonConvert.DeserializeObject<List<EnemyAnimClipDef>>(c.animation_clips),
            DropTableId = c.drop_table_id,
        };
    }
}
```

## 주의사항
- Source Generator는 netstandard2.0 필수 (Unity 제약)
- [Json] 타입 힌트의 제네릭 타입은 Roslyn에서 해석 가능해야 함 → `typeof()` 방식 사용
- Def 클래스의 네임스페이스: Config이 global이므로 Def도 global이 기본. 프로젝트에서 using alias 사용 가능
- 기존 수동 Def 클래스와 이름 충돌 주의 → 프로젝트 마이그레이션(Pillar 3)에서 수동 Def 먼저 삭제
- EnemyAnimClipDef 같은 중첩 타입은 자동 생성 대상이 아님 → 프로젝트에서 직접 정의 유지
