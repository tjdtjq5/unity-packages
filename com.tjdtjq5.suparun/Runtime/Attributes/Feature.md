# Attributes Feature

- **상태**: stable
- **용도**: 데이터 모델 스키마 정의용 어트리뷰트. Source Generator와 배포 시스템이 이 어트리뷰트를 읽어 DB 테이블/서버 API/Cron 잡을 자동 생성한다.

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| (없음) | - | 외부 의존성 없음. 순수 System.Attribute 정의만 포함. |

## 구조

```
Attributes/
├── TableAttribute.cs         # [Table] 클래스를 DB 테이블로 생성 (클라이언트 읽기 전용)
├── ConfigAttribute.cs        # [Config] 게임 설정 데이터 (서버에서 읽기 전용)
├── ServiceAttribute.cs       # [Service] [API] 메서드를 ASP.NET Controller로 자동 생성
├── APIAttribute.cs           # [API] 메서드를 서버 API 엔드포인트로 노출
├── CronAttribute.cs          # [Cron] 메서드를 Cloud Scheduler 잡으로 등록
├── PrimaryKeyAttribute.cs    # [PrimaryKey] 기본키 필드 지정
├── ForeignKeyAttribute.cs    # [ForeignKey(Type)] 외래키 참조 타입 지정
├── JsonAttribute.cs          # [Json] string 필드가 JSON 배열/객체임을 표시
├── NotNullAttribute.cs       # [NotNull] null 불가 제약
├── MaxLengthAttribute.cs     # [MaxLength(n)] 문자열 최대 길이 제약
├── UniqueAttribute.cs        # [Unique] 유니크 제약
├── IndexAttribute.cs         # [Index] DB 인덱스 생성
├── DefaultAttribute.cs       # [Default(value)] 기본값 지정
├── CreatedAtAttribute.cs     # [CreatedAt] 레코드 생성 시간 자동 기록
├── UpdatedAtAttribute.cs     # [UpdatedAt] 레코드 수정 시간 자동 기록
├── HiddenAttribute.cs        # [Hidden] 클라이언트 응답에서 필드 제외
├── VisibleIfAttribute.cs     # [VisibleIf] 어드민에서 조건 일치 시 필드 활성화
├── HiddenIfAttribute.cs      # [HiddenIf] 어드민에서 조건 일치 시 필드 비활성화
├── PublicAttribute.cs        # [Public] 인증 없이 호출 가능한 API
├── PrivateAttribute.cs       # [Private] 관리자만 호출 가능한 API
└── RenamedFromAttribute.cs   # [RenamedFrom("old")] 필드 이름 변경 시 마이그레이션용
```

## API

### 클래스 레벨 어트리뷰트

| 어트리뷰트 | 대상 | 설명 |
|-----------|------|------|
| `[Table]` | Class | DB 테이블 생성. 선택적 `Group` 매개변수로 그룹핑. |
| `[Table("그룹명")]` | Class | 그룹 지정 테이블. |
| `[Config]` | Class | 게임 설정 데이터. PostgREST로 직접 조회. |
| `[Config("그룹명")]` | Class | 그룹 지정 설정. |
| `[Service]` | Class | 서버 서비스 클래스. [API] 메서드를 Controller로 생성. |

### 메서드 레벨 어트리뷰트

| 어트리뷰트 | 대상 | 설명 |
|-----------|------|------|
| `[API]` | Method | 서버 API 엔드포인트로 노출. |
| `[Public]` | Method | 인증 없이 호출 가능 ([API]와 함께 사용). |
| `[Private]` | Method | 관리자만 호출 가능 ([API]와 함께 사용). |
| `[Cron("expr", timeZone?, desc?)]` | Method | Cron 스케줄 실행. 별칭: @daily, @weekly, @hourly, @every 30m. |

### 필드/프로퍼티 레벨 어트리뷰트

| 어트리뷰트 | 대상 | 설명 |
|-----------|------|------|
| `[PrimaryKey]` | Field/Property | 기본키. Save 시 필수. |
| `[ForeignKey(typeof(T))]` | Field/Property | 외래키. 참조 타입 지정. |
| `[Json]` | Field/Property | JSON 배열/객체 필드 표시 (어드민에서 JSON 에디터). |
| `[NotNull]` | Field/Property | null 불가 제약 (LocalGameDB에서 검증). |
| `[MaxLength(n)]` | Field/Property | 문자열 최대 길이 (LocalGameDB에서 검증). |
| `[Unique]` | Field/Property | 유니크 제약 (LocalGameDB에서 검증). |
| `[Index]` | Field/Property | DB 인덱스 생성 힌트. |
| `[Default(value)]` | Field/Property | 기본값. 값이 기본(0/null/"")이면 자동 적용. |
| `[CreatedAt]` | Field/Property | 레코드 생성 시간 자동 기록. long/double/DateTime/string. |
| `[UpdatedAt]` | Field/Property | 레코드 수정 시간 자동 기록. 매 Save 시 갱신. |
| `[Hidden]` | Field/Property | 클라이언트 응답에서 제외. |
| `[VisibleIf("field", "val1", ...)]` | Field/Property | 어드민에서 조건 필드 값 일치 시 활성화. bool/enum 단일/복수 지원. |
| `[HiddenIf("field", "val1", ...)]` | Field/Property | 어드민에서 조건 필드 값 일치 시 비활성화. VisibleIf의 역조건. |
| `[RenamedFrom("oldName")]` | Field/Property | 필드 이름 변경 시 마이그레이션 감지용. |

## 주의사항

- 이 어트리뷰트들은 정의만 포함하며, 실제 동작은 세 곳에서 처리된다:
  1. **LocalGameDB** (Runtime): `[PrimaryKey]`, `[NotNull]`, `[MaxLength]`, `[Unique]`, `[Default]`, `[CreatedAt]`, `[UpdatedAt]` 검증/적용.
  2. **Source Generator** (Editor): `[Service]`, `[API]`, `[Table]`, `[Config]`를 읽어 프록시 코드/마이그레이션 SQL 생성.
  3. **Deploy 시스템** (Editor): `[Cron]`을 읽어 Cloud Scheduler 잡 등록, `[Table]`/`[Config]`로 DB 마이그레이션 실행.
- `[ForeignKey]`의 참조 타입은 반드시 `[Table]` 또는 `[Config]` 어트리뷰트가 붙은 클래스여야 한다.
- `[RenamedFrom]`은 `[Table]` 필드 이름을 바꿀 때 DB 마이그레이션이 DROP 대신 RENAME을 생성하도록 한다. 마이그레이션 완료 후 제거해도 된다.
- `[Cron]`의 TimeZone 기본값은 `"Etc/UTC"`이다.
