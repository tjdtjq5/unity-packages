# DB Feature

- **상태**: stable
- **용도**: 게임 DB 추상화 인터페이스 + 로컬 인메모리 구현. 서버 없이 [Service] 로직을 즉시 테스트 가능.

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| Attributes | `../Attributes/` | PrimaryKey, NotNull, MaxLength, Unique, Default, CreatedAt, UpdatedAt, Table 어트리뷰트 검증 |

## 구조

```
DB/
├── IGameDB.cs        # 게임 DB 인터페이스 (CRUD + Query + Transaction)
├── LocalGameDB.cs    # IGameDB 인메모리 구현 (Dictionary + JSON 직렬화)
└── QueryFilter.cs    # 쿼리 필터/옵션 (Eq, Gt, Lt, Like, OrderBy, Limit)
```

## API

### IGameDB (interface)

| 메서드 | 설명 |
|--------|------|
| `Get<T>(object primaryKey)` | PK로 단건 조회. |
| `GetAll<T>()` | 전체 조회. |
| `Save<T>(T entity)` | 저장 (Insert or Update). |
| `Delete<T>(object primaryKey)` | PK로 삭제. |
| `Query<T>(QueryOptions options)` | 필터 + 정렬 + 페이지네이션 조회. |
| `Count<T>(QueryOptions options)` | 조건에 맞는 레코드 수. |
| `SaveAll<T>(List<T> entities)` | 일괄 저장. |
| `DeleteAll<T>(QueryOptions options)` | 조건에 맞는 레코드 일괄 삭제. |
| `Transaction(Func<IGameDB, Task> action)` | 트랜잭션 (실패 시 롤백). |

### LocalGameDB

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `Instance` | 싱글톤 인스턴스 (static). |
| `Reset()` | 인스턴스 초기화 (테스트용). |
| IGameDB 전체 메서드 | 위 인터페이스 구현. 메모리 Dictionary 기반. |

### QueryOptions (builder pattern)

| 메서드 | 설명 |
|--------|------|
| `Eq(column, value)` | 등호 필터 (`=`). |
| `Gt(column, value)` | 초과 필터 (`>`). |
| `Lt(column, value)` | 미만 필터 (`<`). |
| `Gte(column, value)` | 이상 필터 (`>=`). |
| `Lte(column, value)` | 이하 필터 (`<=`). |
| `Like(column, value)` | 부분 매칭 (대소문자 무시). |
| `OrderByAsc(column)` | 오름차순 정렬. |
| `OrderByDesc(column)` | 내림차순 정렬. |
| `SetLimit(int)` | 결과 수 제한 (기본 1000). |
| `SetOffset(int)` | 오프셋 (페이지네이션). |

### QueryFilter

| 필드 | 설명 |
|------|------|
| `Column` | 필드 이름. |
| `Operator` | 연산자 (`=`, `>`, `<`, `>=`, `<=`, `like`). |
| `Value` | 비교 값. |

## 주의사항

- `LocalGameDB`는 어트리뷰트 검증을 런타임에 수행한다: `[NotNull]` null 체크, `[MaxLength]` 길이 체크, `[Unique]` 중복 체크.
- `[Default]` 값이 기본값(0, null, "")이면 지정된 기본값을 자동 적용한다.
- `[CreatedAt]`은 신규 레코드에만, `[UpdatedAt]`은 매 Save 시 자동 갱신된다. 지원 타입: long, double, DateTime, string.
- `[PrimaryKey]` 어트리뷰트가 없는 타입을 Save하면 `InvalidOperationException`이 발생한다.
- `GetAll<T>()`이 100건 초과 시 `[Table]` 타입에 대해 성능 경고 로그를 출력한다 (Query 사용 권장).
- `Transaction`은 스냅샷 기반 롤백으로, 실패 시 전체 상태를 복원한다.
- 프로덕션 환경에서는 서버의 `DapperGameDB`(PostgreSQL)가 동일 인터페이스를 구현한다.
