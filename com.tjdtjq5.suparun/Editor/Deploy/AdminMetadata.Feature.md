# Admin Metadata Unification

## 상태
stable

## 용도
Table/Config/JSON 메타데이터 생성을 통합 리팩토링하고, JSON 모달에서도 enum + VisibleIf/HiddenIf를 지원한다.

## 의존성
- `../../Runtime/Attributes/` — VisibleIf/HiddenIf/EnumType 어트리뷰트 정의
- `../../Templates/AdminTemplate~/` — 어드민 SPA (index.html)
- `./ServerCodeGenerator.cs` — 메타데이터 JSON 생성
- `./DeployManager.cs` — StripForServer (서버 빌드 시 어트리뷰트 제거)

## 포함 기능

### 1. BuildFieldMetadata 공통화
- `BuildConfigMetadataJson()`과 `BuildTableMetadataJson()` → 공통 `BuildFieldMetadataList()` 메서드로 통합
- 필드 메타데이터 생성 로직 (PrimaryKey, NotNull, Json, EnumType, ForeignKey, VisibleIf, HiddenIf)을 한 곳에서 관리
- Config/Table 각각은 공통 메서드 호출 + 타입별 추가 정보(group, hasUserId 등)만 래핑

### 2. Table 메타데이터 완성
- 공통화 결과로 Table에도 자동으로 EnumType, Json, VisibleIf, HiddenIf 지원
- 별도 코드 추가 불필요 — 1번 완료 시 자동 해결

### 3. JSON 타입 메타데이터 생성
- `[Json(typeof(T))]`의 T 클래스를 reflection하여 필드별 메타데이터 생성
- T의 필드에 `[EnumType]`, `[VisibleIf]`, `[HiddenIf]` 가 있으면 해당 메타데이터 포함
- 생성 위치: Config/Table 메타데이터의 해당 필드에 `"jsonSchema":[...]` 추가
- 예시:
  ```json
  {
    "name": "stat_bonuses",
    "type": "string",
    "isJson": true,
    "jsonSchema": [
      {"name": "StatId", "type": "string"},
      {"name": "Value", "type": "number"},
      {"name": "ModType", "type": "string", "isEnum": true, "enumValues": ["flat","percent"]}
    ]
  }
  ```

### 4. JSON 모달 고도화 (index.html)
- `openJsonEditor()`에서 `jsonSchema` 메타데이터가 있으면 `detectSchema()` 대신 사용
- `renderJsonEditorRows()`에서 enum 필드 → `<select>` 드롭다운 렌더링
- `renderJsonEditorRows()`에서 VisibleIf/HiddenIf 조건 체크 → 비활성 셀 표시
- enum/bool 변경 시 해당 JSON 행의 조건부 셀 즉시 갱신

## 구조

| 파일 | 변경 | 설명 |
|------|------|------|
| `ServerCodeGenerator.cs` | 수정 | `BuildMemberJson()`, `BuildFieldsJson()`, `BuildJsonSchemaJson()` 공통 메서드 추가. Config/Table 함수 리팩토링 |
| `DeployManager.cs` | 수정 | `StripForServer()`에 `[VisibleIf]`/`[HiddenIf]` 제거 추가 |
| `../../Templates/AdminTemplate~/index.html` | 수정 | JSON 모달: `jsonSchema` 메타데이터 활용, enum 드롭다운, VisibleIf, `collectJsonEditorRows` 버그 수정 |
| `../../Runtime/Attributes/VisibleIfAttribute.cs` | 신규 | `[VisibleIf("field", "val1", ...)]` 조건부 표시 |
| `../../Runtime/Attributes/HiddenIfAttribute.cs` | 신규 | `[HiddenIf("field", "val1", ...)]` 조건부 숨김 |

## API (외부 피처가 참조 가능)

| 메서드 | 파일 | 설명 |
|--------|------|------|
| `BuildMemberJson(MemberInfo, Type)` | `ServerCodeGenerator.cs` | 단일 멤버(필드/프로퍼티)의 어드민 메타데이터 JSON 생성 |
| `BuildFieldsJson(Type)` | `ServerCodeGenerator.cs` | 타입의 public 필드 목록을 메타데이터 JSON 배열로 변환 |
| `BuildJsonSchemaJson(Type)` | `ServerCodeGenerator.cs` | `[Json(typeof(T))]`의 T를 반영하여 jsonSchema 배열 생성 |

## 주의사항
- `BuildFieldMetadataList()` 리팩토링 시 기존 Config/Table 메타데이터 출력이 변하지 않도록 주의 (호환성)
- JSON 타입의 프로퍼티는 `GetProperties()`로 읽어야 함 (DTO 클래스는 프로퍼티 사용 패턴)
- `jsonSchema`가 없는 JSON 필드는 기존 `detectSchema()` 폴백 유지
- StripForServer에서 JSON DTO 클래스의 `[VisibleIf]`/`[HiddenIf]`도 제거되어야 함
- JSON 모달의 VisibleIf는 같은 JSON 행(객체) 내부에서만 조건 평가
