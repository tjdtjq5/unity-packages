# Remote Config 탭 설계 v2

## 개요

`.rc` 파일(UGS 표준)은 플랫 key-value 유지.
별도 `.schema.json`으로 재사용 가능한 enum 정의, 타입별 LIST, 그룹 탭을 지원한다.
에디터에서 스키마까지 편집 가능.

## 파일 구조

```
11_Studdy/UGS/
├── RemoteConfig.rc              ← UGS 표준 (Deploy 대상, 플랫 key-value STRING)
└── RemoteConfig.schema.json     ← UI 메타데이터 (에디터 전용, 자동 저장)
```

---

## 스키마 파일 v2

```json
{
  "groups": [
    { "name": "적 밸런스", "color": "#66BBEE", "keys": ["enemy_hp_multiplier", "enemy_spawn_count"] },
    { "name": "이벤트", "color": "#EEAA33", "keys": ["event_active", "event_name", "drop_rate_bonus"] },
    { "name": "보상", "color": "#66DD77", "keys": ["daily_reward_gold", "welcome_message", "reward_tiers", "allowed_events"] }
  ],
  "enumDefs": {
    "EventType": ["none", "lunar_new_year", "summer_fest", "halloween", "christmas"],
    "Difficulty": ["easy", "normal", "hard"]
  },
  "enumMap": {
    "event_name": "EventType"
  },
  "lists": {
    "reward_tiers": { "itemType": "INT" },
    "allowed_events": { "itemType": "ENUM", "enumSchema": "EventType" },
    "bonus_rates": { "itemType": "FLOAT" }
  }
}
```

### v1 → v2 변경점

| v1 | v2 | 이유 |
|----|-----|------|
| `enums.{키이름}.options` | `enumDefs.{스키마이름}` + `enumMap.{키}` | 재사용 가능한 enum 정의 |
| `lists.{키}.itemType` = STRING/INT/FLOAT | + BOOL, ENUM | 타입별 LIST |
| (없음) | `lists.{키}.enumSchema` | ENUM 리스트일 때 스키마 참조 |

---

## 데이터 모델

### ConfigEntry

```csharp
class ConfigEntry
{
    public string Key;
    public string OrigKey;           // 키 이름 변경용 (Revert)

    // 타입
    public string DisplayType;       // FLOAT, INT, BOOL, STRING, ENUM, LIST
    public string OrigDisplayType;   // Revert용
    public string BaseType;          // .rc 기준 (ENUM/LIST → STRING)

    // 값
    public string Value;             // 원본 값 (저장된 상태)
    public string EditValue;         // 편집 중 값
    public bool IsDirty;

    // ENUM
    public string EnumSchemaKey;     // 사용 중인 enum 스키마 이름 (예: "EventType")
    public string OrigEnumSchemaKey; // Revert용
    public string[] EnumOptions;     // 현재 enum 옵션들 (EnumDefs에서 가져옴)
    public int EnumIndex;

    // LIST
    public string ListItemType;      // 아이템 타입: INT, FLOAT, STRING, BOOL, ENUM
    public string OrigListItemType;  // Revert용
    public string ListEnumSchemaKey; // 아이템이 ENUM일 때 스키마
    public string OrigListEnumSchemaKey; // Revert용
    public List<string> ListItems;
    public List<string> OrigListItems; // Revert용 (깊은 복사)
}
```

### SchemaData

```csharp
class SchemaData
{
    public List<GroupInfo> Groups;
    public Dictionary<string, List<string>> EnumDefs;  // 스키마이름 → 옵션 리스트 (편집 가능)
    public Dictionary<string, string> EnumMap;          // 키 → 스키마이름
    public Dictionary<string, ListSchemaInfo> Lists;    // 키 → 리스트 스키마
}

struct GroupInfo { string Name; Color Color; string[] Keys; }
struct ListSchemaInfo { string ItemType; string EnumSchema; }
```

---

## UI 설계

### 전체 레이아웃

```
┌─ Config ──────────────────────────────────────────────┐
│ [Refresh] [Save All] [Deploy ↑]     [검색🔍] Dash ↗  │
├───────────────────────────────────────────────────────┤
│ [적 밸런스] [이벤트] [보상]                              │
├───────────────────────────────────────────────────────┤
│  키              │ 타입     │ 값                │      │
│ ─────────────────┼──────────┼───────────────────┼──── │
│  enemy_hp_multi  │ FLOAT ▾  │ [1.0         ]    │ ✓↺✕ │
│  event_name      │ ENUM ▾   │[EventType▾][hall▾]│ ✓↺✕ │
│  reward_tiers    │ LIST ▾   │ 아이템:[INT▾] 3개  │ ✓↺✕ │
│                  │          │ [0] [100     ] ✕  │     │
│                  │          │ [1] [200     ] ✕  │     │
│                  │          │ [2] [500     ] ✕  │     │
│                  │          │ +                  │     │
├───────────────────────────────────────────────────────┤
│ ▸ 키 추가                                             │
│ ▸ Enum 스키마 관리                                     │
│ ▸ 파일 경로                                            │
│ ▸ Deploy 결과                                          │
└───────────────────────────────────────────────────────┘
```

### 1. ENUM 편집 UI (스키마 선택 + 값 선택)

```
│ event_name │ ENUM ▾ │ [EventType ▾] [halloween ▾] │ ✓ ↺ ✕
│            │        │  스키마 선택    값 선택        │
```

- 스키마 선택 드롭다운: `enumDefs`의 키 목록
- 값 선택 드롭다운: 선택된 enum 스키마의 옵션 목록
- 스키마 변경 시 값은 새 스키마의 첫 번째 옵션으로 초기화

### 2. LIST 편집 UI (타입별 아이템)

#### INT 리스트

```
│ reward_tiers │ LIST ▾ │ 아이템:[INT ▾]         3개 │ ✓ ↺ ✕
│              │        │ [0] [100          ]    ✕  │
│              │        │ [1] [200          ]    ✕  │
│              │        │ [2] [500          ]    ✕  │
│              │        │ +                         │
```

#### BOOL 리스트

```
│ flags │ LIST ▾ │ 아이템:[BOOL ▾]           2개 │ ✓ ↺ ✕
│       │        │ [0] [☑]                   ✕  │
│       │        │ [1] [☐]                   ✕  │
│       │        │ +                             │
```

#### ENUM 리스트

```
│ allowed │ LIST ▾ │ 아이템:[ENUM ▾] 스키마:[EventType ▾] │ ✓↺✕
│         │        │ [0] [halloween    ▾]            ✕   │
│         │        │ [1] [summer_fest  ▾]            ✕   │
│         │        │ +                                    │
```

### 3. Enum 스키마 관리 섹션

```
▾ Enum 스키마 관리
┌──────────────────────────────────────────────────┐
│ EventType:                                       │
│   [none] [lunar_new_year] [summer_fest]          │
│   [halloween] [christmas]                        │
│   새 옵션: [__________] [+]               [삭제] │
│                                                  │
│ Difficulty:                                      │
│   [easy] [normal] [hard]                         │
│   새 옵션: [__________] [+]               [삭제] │
│                                                  │
│ 새 스키마: [__________] [생성]                     │
└──────────────────────────────────────────────────┘
```

- 옵션 클릭으로 이름 편집 (인라인 TextField)
- 옵션 옆 [✕]로 개별 삭제
- [+]로 새 옵션 추가
- 변경 시 `.schema.json` 자동 저장
- 스키마 삭제 시 해당 enum을 사용 중인 키들은 STRING으로 전환

### 4. 키 삭제

```
│ event_name │ ENUM ▾ │ [EventType ▾] [halloween ▾] │ ✓ ↺ ✕
│            │        │                              │     ↑ 삭제
```

- 각 행 오른쪽 끝에 `✕` 삭제 버튼
- 확인 다이얼로그 후 삭제 → `.rc` 자동 저장

### 5. 키 이름 변경

- 키 컬럼을 클릭하면 TextField로 전환 (인라인 편집)
- Enter 또는 포커스 이탈 시 확정
- 키 변경 시 `.rc` + `.schema.json`(groups, enumMap, lists) 모두 업데이트

### 6. 검색/필터

```
│ [Refresh] [Save All] [Deploy ↑]  [🔍 검색...     ] Dashboard ↗
```

- 툴바에 검색 TextField
- 키 이름으로 실시간 필터링 (대소문자 무시)
- 빈 문자열이면 전체 표시

### 7. 변경사항 일괄 Save

```
│ [Refresh] [Save All] [Deploy ↑]
```

- `Save All` 버튼: 모든 dirty 엔트리를 한 번에 저장
- dirty 엔트리 수 표시: `Save All (3)`
- dirty가 없으면 비활성

### 8. Deploy 결과 섹션

```
▾ Deploy 결과
┌──────────────────────────────────────────┐
│ [17:30:25] ✓ Deploy 완료 (7개 키)        │
│ [17:30:22] 배포 시작...                   │
│                                          │
│ [로그 지우기]                              │
└──────────────────────────────────────────┘
```

- Deploy 실행 시 결과 로그 표시 (성공/실패/키 수)
- 최신 로그가 위

---

## 타입 변환 규칙

### 단일 값 ↔ LIST

| 변환 | 동작 | 예시 |
|------|------|------|
| INT `10` → LIST(INT) | 1개 아이템 리스트 | `["10"]` |
| FLOAT `1.5` → LIST(FLOAT) | 1개 아이템 리스트 | `["1.5"]` |
| BOOL `true` → LIST(BOOL) | 1개 아이템 리스트 | `["true"]` |
| STRING `"hello"` → LIST(STRING) | 1개 아이템 리스트 | `["hello"]` |
| ENUM `halloween` → LIST(ENUM) | 1개 아이템 리스트 (같은 스키마) | `["halloween"]` |
| LIST `["10","20"]` → INT | 첫 번째 아이템 | `10` |
| LIST `["10","20"]` → STRING | 쉼표 결합 | `"10,20"` |
| LIST `["true","false"]` → BOOL | 첫 번째 아이템 | `true` |

### 타입 간 변환 (값 변환)

| 변환 | 가능 | 불가능 시 |
|------|------|----------|
| INT → FLOAT | `10` → `10.0` | - |
| FLOAT → INT | `1.5` → `1` (절삭) | - |
| STRING → INT | `"10"` → `10` | `"hello"` → `0` |
| STRING → BOOL | `"true"` → `true` | 그 외 → `false` |
| INT → STRING | `10` → `"10"` | - |
| ENUM → STRING | 값 그대로 | - |
| STRING → ENUM | 옵션에 있으면 선택, 없으면 첫 번째 | - |

### LIST 아이템 타입 변경

| 변환 | 동작 |
|------|------|
| LIST(INT) → LIST(STRING) | 각 아이템 그대로 |
| LIST(STRING) → LIST(INT) | 파싱 가능하면 유지, 불가 → `0` |
| LIST(INT) → LIST(BOOL) | 0 → `false`, 그 외 → `true` |
| LIST(any) → LIST(ENUM) | 옵션에 있으면 유지, 없으면 첫 번째 |

---

## Revert 범위 (전체)

| 항목 | 원본 필드 | 복원 |
|------|----------|------|
| 키 이름 | `OrigKey` | ✅ |
| 값 | `Value` | ✅ |
| 타입 | `OrigDisplayType` | ✅ |
| enum 스키마 | `OrigEnumSchemaKey` | ✅ |
| list 아이템 타입 | `OrigListItemType` | ✅ |
| list enum 스키마 | `OrigListEnumSchemaKey` | ✅ |
| list 아이템들 | `OrigListItems` | ✅ (깊은 복사) |

---

## .rc 저장 규칙 (변경 없음)

| 타입 | .rc 저장 형태 |
|------|-------------|
| FLOAT | `"key": 1.0` |
| INT | `"key": 10` |
| BOOL | `"key": true` |
| STRING | `"key": "value"` |
| ENUM | `"key": "halloween"` (STRING) |
| LIST | `"key": "100,200,500"` (STRING, 쉼표 구분) |

---

## 구현 순서

| 단계 | 내용 |
|------|------|
| 1 | SchemaData v2 모델 (EnumDefs, EnumMap, ListSchemaInfo) |
| 2 | 스키마 파싱/저장 v2 |
| 3 | ConfigEntry 확장 (OrigKey, EnumSchemaKey, ListItemType, OrigListItems 등) |
| 4 | ENUM UI (스키마 선택 + 값 선택 2단 드롭다운) |
| 5 | LIST UI (아이템 타입 드롭다운 + 타입별 편집기) |
| 6 | 타입 변환 로직 (단일 ↔ LIST, 타입 간) |
| 7 | Revert 로직 확장 |
| 8 | Enum 스키마 관리 섹션 UI |
| 9 | 키 삭제 기능 |
| 10 | 키 이름 변경 (인라인) |
| 11 | 검색/필터 |
| 12 | Save All 버튼 |
| 13 | Deploy 결과 로그 |
| 14 | .schema.json 자동 저장 (enum 편집, 키 변경 시) |
| 15 | 예제 스키마 v2 파일 생성 |

모든 변경은 `RemoteConfigTab.cs` + `RemoteConfig.schema.json`으로 완결.
