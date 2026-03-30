# Client Feature

- **상태**: stable
- **용도**: SupaRun 정적 진입점 + HTTP 클라이언트. 서버/로컬 자동 분기 및 인증 연동.

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| Models | `../Models/` | ServerConfig, ServerResponse, AuthSession, ErrorType 등 |
| DB | `../DB/` | LocalGameDB (개발 모드 fallback) |
| Auth | `../Auth/` | SupabaseAuth (인증, 토큰 관리) |
| Supabase | `../Supabase/` | SupabaseRealtime (실시간 채널) |
| Attributes | `../Attributes/` | ConfigAttribute (Config/Table 분기 판단) |
| Config | `../Config/` | SupaRunRuntimeConfig (빌드 시 설정 로드) |

## 구조

```
Client/
├── SupaRun.cs              # 정적 API 진입점 (partial class, Get/GetAll/Auth/Realtime/Client)
├── SupaRunClient.cs         # UnityWebRequest HTTP 클라이언트 (자동 재시도 + 토큰 갱신)
└── SupabaseRestClient.cs    # Supabase PostgREST 직접 조회 ([Config] 타입 전용, internal)
```

## API

### SupaRun (static partial class)

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `Initialize(ServerConfig)` | 수동 초기화. 앱 시작 시 한 번 호출. |
| `Get<T>(object id)` | 단건 조회. [Config]->PostgREST, [Table]->Cloud Run, 미배포->LocalGameDB |
| `GetAll<T>()` | 전체 조회. 라우팅 동일. |
| `Auth` | SupabaseAuth 인스턴스 (자동 초기화) |
| `Realtime` | SupabaseRealtime 인스턴스 (자동 초기화) |
| `Client` | SupaRunClient 인스턴스 (Source Generator 프록시용) |
| `LocalDB` | LocalGameDB 인스턴스 |
| `CurrentSession` | 현재 인증 세션 |
| `PlayerId` | 현재 로그인된 플레이어 ID |
| `IsInitialized` | 초기화 여부 |
| `WaitForAuth()` | Auth 완료 대기 (SG 프록시에서 서버 호출 전 사용) |

### SupaRunClient

| 메서드 | 설명 |
|--------|------|
| `GetAsync<T>(string endpoint)` | GET 요청. 자동 재시도 (최대 3회, 지수 백오프). |
| `PostAsync<T>(string endpoint, object payload)` | POST 요청 + 응답 역직렬화. |
| `PostAsync(string endpoint, object payload)` | POST 요청 (응답 데이터 불필요 시). |
| `Session` | 인증 세션. SupabaseAuth가 자동 설정. |
| `OnTokenRefresh` | 401 시 호출되는 토큰 갱신 콜백. |

### SupabaseRestClient (internal)

| 메서드 | 설명 |
|--------|------|
| `Get<T>(object id)` | PostgREST 단건 조회 (테이블명 자동 snake_case 변환) |
| `GetAll<T>()` | PostgREST 전체 조회 |

## 주의사항

- `SupaRun`은 `partial class`이므로 Source Generator가 Service 프록시 메서드를 추가한다.
- 자동 초기화 시 에디터는 `UserSettings/SupaRunSettings.json`, 빌드는 `Resources/SupaRunConfig.json`에서 설정을 읽는다.
- `SupaRunClient`는 5xx/Timeout 에러 시 지수 백오프 재시도 (1s, 2s, 4s), 401 시 토큰 자동 갱신 후 1회 재시도한다.
- `SupabaseRestClient`는 `[Config]` 어트리뷰트가 붙은 타입만 사용하며, Supabase REST API에 `anonKey`로 직접 쿼리한다.
