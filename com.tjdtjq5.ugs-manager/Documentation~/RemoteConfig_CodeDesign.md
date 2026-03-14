# Remote Config v2 — 상세 코드 설계

## 파일 분리 설계

현재 `RemoteConfigTab.cs` 1개(900줄+)를 4개로 분리한다.

```
_Editor/UGS/
├── RemoteConfigTab.cs          # 메인 탭 UI (OnDraw, 툴바, 그룹탭, 엔트리 테이블)
├── RemoteConfigModels.cs       # 데이터 모델 (ConfigEntry, SchemaData, 변환 로직)
├── RemoteConfigSchema.cs       # 스키마 파싱/저장 (.schema.json ↔ SchemaData)
├── RemoteConfigEnumEditor.cs   # Enum 스키마 관리 섹션 UI
└── (기존)
    ├── UGSWindow.cs
    ├── UGSTabBase.cs
    ├── UGSCliRunner.cs
    └── ...
```

---

## 1. RemoteConfigModels.cs

```csharp
#if UNITY_EDITOR
namespace _Project._2_Scripts._Editor.UGS
{
    /// <summary>Config 엔트리 데이터</summary>
    class ConfigEntry
    {
        // 키
        public string Key;
        public string OrigKey;           // Revert용
        public bool IsEditingKey;        // 키 이름 인라인 편집 중

        // 타입
        public string DisplayType;       // FLOAT, INT, BOOL, STRING, ENUM, LIST
        public string OrigDisplayType;
        public string BaseType;          // .rc 기준 (ENUM/LIST → STRING)

        // 값
        public string Value;             // 저장된 원본
        public string EditValue;         // 편집 중
        public bool IsDirty;

        // ENUM
        public string EnumSchemaKey;     // 사용 중인 enum 스키마 (예: "EventType")
        public string OrigEnumSchemaKey;
        public string[] EnumOptions;     // 현재 옵션 배열 (캐시)
        public int EnumIndex;

        // LIST
        public string ListItemType;      // INT, FLOAT, STRING, BOOL, ENUM
        public string OrigListItemType;
        public string ListEnumSchemaKey; // ENUM 리스트의 스키마
        public string OrigListEnumSchemaKey;
        public List<string> ListItems;
        public List<string> OrigListItems; // Revert용 (깊은 복사)
    }

    /// <summary>스키마 데이터</summary>
    class SchemaData
    {
        public List<GroupInfo> Groups = new();
        public Dictionary<string, List<string>> EnumDefs = new();  // 스키마이름 → 옵션 (편집 가능)
        public Dictionary<string, string> EnumMap = new();          // 키 → 스키마이름
        public Dictionary<string, ListSchemaInfo> Lists = new();
    }

    struct GroupInfo
    {
        public string Name;
        public Color Color;
        public string[] Keys;
    }

    struct ListSchemaInfo
    {
        public string ItemType;     // INT, FLOAT, STRING, BOOL, ENUM
        public string EnumSchema;   // ItemType==ENUM일 때
    }

    /// <summary>타입 변환 유틸</summary>
    static class ConfigTypeConverter
    {
        /// <summary>단일 값 → LIST 변환</summary>
        public static (List<string> items, string itemType, string enumSchema)
            SingleToList(ConfigEntry entry, SchemaData schema);

        /// <summary>LIST → 단일 값 변환</summary>
        public static string ListToSingle(ConfigEntry entry, string targetType);

        /// <summary>타입 간 값 변환</summary>
        public static string ConvertValue(string value, string fromType, string toType,
            string[] enumOptions = null);

        /// <summary>LIST 아이템 타입 변환</summary>
        public static List<string> ConvertListItems(List<string> items,
            string fromType, string toType, string[] enumOptions = null);
    }
}
#endif
```

### ConfigTypeConverter 변환 규칙 상세

```csharp
// ConvertValue 구현 핵심
static string ConvertValue(string value, string from, string to, string[] enumOpts)
{
    return (from, to) switch
    {
        // 숫자 ↔ 숫자
        ("INT", "FLOAT") => value,                                    // "10" → "10"
        ("FLOAT", "INT") => int.TryParse(value.Split('.')[0], out var i) ? i.ToString() : "0",

        // → BOOL
        (_, "BOOL") => (value == "true" || value == "1") ? "true" : "false",

        // → INT
        (_, "INT") => int.TryParse(value, out var n) ? n.ToString() : "0",

        // → FLOAT
        (_, "FLOAT") => float.TryParse(value, out var f) ? f.ToString() : "0",

        // → STRING
        (_, "STRING") => value,

        // → ENUM
        (_, "ENUM") => enumOpts != null && Array.IndexOf(enumOpts, value) >= 0
            ? value : (enumOpts?.Length > 0 ? enumOpts[0] : value),

        _ => value
    };
}

// SingleToList: 현재 값을 1개 아이템 리스트로
// - ENUM → LIST(ENUM): 같은 스키마 유지, ["halloween"]
// - INT 10 → LIST(INT): ["10"]
// - STRING "hello" → LIST(STRING): ["hello"]

// ListToSingle:
// - LIST → INT: 첫 번째 아이템 (int 파싱)
// - LIST → STRING: 쉼표 결합
// - LIST → ENUM: 첫 번째 아이템 (옵션에 있으면)
// - LIST → BOOL: 첫 번째 아이템
```

---

## 2. RemoteConfigSchema.cs

```csharp
#if UNITY_EDITOR
namespace _Project._2_Scripts._Editor.UGS
{
    /// <summary>.schema.json 파일 파싱 + 저장</summary>
    static class RemoteConfigSchema
    {
        /// <summary>스키마 파일 파싱</summary>
        public static SchemaData Parse(string json);

        /// <summary>스키마 파일 저장 (JSON 직렬화)</summary>
        public static void Save(string filePath, SchemaData schema);

        /// <summary>키 이름 변경 시 스키마 업데이트</summary>
        public static void RenameKey(SchemaData schema, string oldKey, string newKey);

        /// <summary>키 삭제 시 스키마 정리</summary>
        public static void RemoveKey(SchemaData schema, string key);
    }
}
#endif
```

### Save 출력 형식

```json
{
  "groups": [
    { "name": "적 밸런스", "color": "#66BBEE", "keys": ["enemy_hp_multiplier"] }
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
    "allowed_events": { "itemType": "ENUM", "enumSchema": "EventType" }
  }
}
```

### RenameKey 로직

```
1. groups[].keys에서 oldKey → newKey 교체
2. enumMap에서 oldKey → newKey 키 교체
3. lists에서 oldKey → newKey 키 교체
4. 자동 저장
```

### RemoveKey 로직

```
1. groups[].keys에서 key 제거
2. enumMap에서 key 제거
3. lists에서 key 제거
4. 자동 저장
```

---

## 3. RemoteConfigEnumEditor.cs

```csharp
#if UNITY_EDITOR
namespace _Project._2_Scripts._Editor.UGS
{
    /// <summary>Enum 스키마 관리 UI 섹션</summary>
    class RemoteConfigEnumEditor
    {
        SchemaData _schema;
        string _schemaFilePath;
        string _newSchemaName = "";
        Dictionary<string, string> _newOptionInputs = new(); // 스키마별 새 옵션 입력

        public RemoteConfigEnumEditor(SchemaData schema, string filePath);

        /// <summary>UI 그리기 (foldout 내부에서 호출)</summary>
        public bool Draw();  // true면 스키마 변경됨

        // 내부 메서드
        void DrawEnumDef(string name, List<string> options);
        void DrawNewSchema();
        void SaveSchema();
    }
}
#endif
```

### UI 동작 상세

```
▾ Enum 스키마 관리
│
│ EventType                                    [삭제]
│   [none ✕] [lunar_new_year ✕] [summer_fest ✕]
│   [halloween ✕] [christmas ✕]
│   새 옵션: [__________] [+]
│
│ Difficulty                                   [삭제]
│   [easy ✕] [normal ✕] [hard ✕]
│   새 옵션: [__________] [+]
│
│ 새 스키마: [__________] [생성]
```

- 각 옵션: `TextField` (이름 편집) + `✕` (삭제)
- 스키마 삭제 시: 해당 enum 사용 중인 키 → STRING으로 전환
- 변경 시 `.schema.json` 즉시 저장

---

## 4. RemoteConfigTab.cs (v2)

메인 탭. 나머지 3개 파일을 조합한다.

```csharp
public class RemoteConfigTab : UGSTabBase
{
    // 데이터
    List<ConfigEntry> _entries;
    SchemaData _schema;
    string _rcFilePath, _schemaFilePath;
    RemoteConfigEnumEditor _enumEditor;

    // UI 상태
    int _activeGroupIdx;
    string _searchFilter = "";
    float _colKeyWidth = 160f, _colTypeWidth = 60f;
    bool _draggingKeyCol, _draggingTypeCol;
    bool _foldAdd, _foldEnum, _foldFile, _foldDeployLog;
    List<DeployLogEntry> _deployLogs = new();

    // 키 추가
    string _newKey, _newValue;
    int _newTypeIdx;

    // ─── 메인 OnDraw ─────────────────────────────
    public override void OnDraw()
    {
        DrawToolbar();         // [Refresh] [Save All (n)] [Deploy ↑] [검색] [Dashboard ↗]
        DrawError();
        DrawLoading();

        DrawGroupTabs();       // [적 밸런스] [이벤트] [보상]
        DrawTableHeader();     // 키 | 타입 | 값 | (리사이즈 핸들 + 구분선)
        DrawEntryRows();       // 엔트리 행들 (필터 + 그룹 적용)

        DrawAddKeySection();   // ▸ 키 추가
        DrawEnumSection();     // ▸ Enum 스키마 관리
        DrawFileSection();     // ▸ 파일 경로
        DrawDeployLog();       // ▸ Deploy 결과
    }

    // ─── 툴바 ────────────────────────────────────
    void DrawToolbar()
    {
        // [Refresh]
        // [Save All (n)] — dirty 수 표시, 0이면 비활성
        // [Deploy ↑]
        // [🔍 검색 TextField]
        // [Dashboard ↗]
    }

    // ─── 엔트리 행 ──────────────────────────────
    void DrawEntryRow(ConfigEntry entry, int index)
    {
        // 키 컬럼: 클릭 시 TextField 전환 (키 이름 변경)
        // 타입 컬럼: Popup (ALL_TYPE_OPTIONS)
        // 값 컬럼: 타입별 에디터
        //   - FLOAT/INT: 검증된 TextField
        //   - BOOL: Toggle
        //   - STRING: TextField
        //   - ENUM: [스키마 선택 ▾] + [값 선택 ▾]
        //   - LIST: 아이템 타입 드롭다운 + 아이템들
        // 액션: ✓ (Save) ↺ (Revert) ✕ (Delete)
    }

    // ─── ENUM 값 에디터 ─────────────────────────
    void DrawEnumValueEditor(ConfigEntry entry)
    {
        // 1단: 스키마 선택 Popup (enumDefs 키 목록)
        //   → 변경 시 EnumOptions 갱신, 값은 첫 번째 옵션으로
        // 2단: 값 선택 Popup (선택된 스키마의 옵션들)
    }

    // ─── LIST 에디터 ────────────────────────────
    void DrawListEditor(ConfigEntry entry)
    {
        // 헤더행: 아이템타입 Popup [INT/FLOAT/STRING/BOOL/ENUM ▾]
        //   ENUM이면 + 스키마 선택 Popup
        //   아이템 타입 변경 → ConvertListItems 호출
        // 아이템 행: 타입별 에디터
        //   INT: 검증 TextField
        //   FLOAT: 검증 TextField
        //   BOOL: Toggle
        //   STRING: TextField
        //   ENUM: Popup (스키마 옵션)
        // [+] 추가, [✕] 삭제
    }

    // ─── 타입 변환 ──────────────────────────────
    void OnTypeChanged(ConfigEntry entry, string oldType, string newType)
    {
        // 1. 단일→LIST: SingleToList
        // 2. LIST→단일: ListToSingle
        // 3. 단일→단일: ConvertValue
        // 4. ENUM: 스키마 초기화
        // 5. MarkDirty
    }

    // ─── 키 이름 변경 ───────────────────────────
    void DrawKeyColumn(ConfigEntry entry)
    {
        if (entry.IsEditingKey)
        {
            // TextField로 편집
            // Enter/포커스 이탈 → 확정
            // .rc + .schema 모두 업데이트
        }
        else
        {
            // 라벨 표시
            // 더블클릭 → IsEditingKey = true
        }
    }

    // ─── Save / Revert ─────────────────────────
    void SaveEntry(ConfigEntry entry)
    {
        // 모든 Orig* 필드를 현재 값으로 갱신
        // SaveRcFile + SaveSchema (키 이름 변경 등)
    }

    void RevertEntry(ConfigEntry entry)
    {
        // 모든 필드를 Orig* 에서 복원
        // Key, DisplayType, EditValue, EnumSchemaKey,
        // ListItemType, ListEnumSchemaKey, ListItems
    }

    void SaveAll()
    {
        // dirty인 모든 엔트리에 SaveEntry 호출
    }

    void DeleteEntry(ConfigEntry entry)
    {
        // 확인 다이얼로그
        // _entries에서 제거
        // RemoteConfigSchema.RemoveKey
        // SaveRcFile
    }

    // ─── Deploy ─────────────────────────────────
    void PushToServer()
    {
        // UGSCliRunner.RunAsync → 결과를 _deployLogs에 추가
    }
}
```

---

## 구현 순서 (의존성 기반)

```
[1] RemoteConfigModels.cs     ← 의존성 없음 (모델만)
[2] RemoteConfigSchema.cs     ← [1]에 의존
[3] RemoteConfigEnumEditor.cs ← [1], [2]에 의존
[4] RemoteConfigTab.cs v2     ← [1], [2], [3] 모두 사용
[5] RemoteConfig.schema.json  ← v2 형식 예제
```

1→2→3→4→5 순서로 구현. 각 단계에서 컴파일 확인.

---

## 추정 코드량

| 파일 | 예상 줄 |
|------|---------|
| RemoteConfigModels.cs | ~150 |
| RemoteConfigSchema.cs | ~200 |
| RemoteConfigEnumEditor.cs | ~150 |
| RemoteConfigTab.cs v2 | ~600 |
| **합계** | **~1100** |

현재 단일 파일 900줄 → 4개 파일 1100줄. 줄 수는 비슷하지만 각 파일이 단일 책임을 가짐.
