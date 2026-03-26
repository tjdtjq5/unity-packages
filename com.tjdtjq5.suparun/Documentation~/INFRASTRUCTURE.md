# Infrastructure 분석 — Supabase + Google Cloud Run

## 아키텍처 구조

```
Unity Client
├── SupaRun (정적 API)
│   ├── 개발: LocalGameDB (인메모리)
│   └── 프로덕션: SupaRunClient → Cloud Run (HTTP)
├── SupabaseAuth (JWT 토큰 관리)
│   └── Supabase Auth API (REST)
└── SupabaseRealtime (WebSocket)
    └── Supabase Realtime (Phoenix Protocol)

Cloud Run (ASP.NET)
├── Controllers (자동 생성)
├── DapperGameDB → Supabase PostgreSQL (Npgsql 직접 연결)
├── JWT 검증 (Supabase Auth 기반)
├── Rate Limiting (JWT sub 또는 IP 기반 유저별 제한)
└── Response Compression (Gzip)

Supabase
├── Auth (GoTrue) — 게스트, OAuth, 플랫폼 네이티브
├── PostgreSQL — 게임 데이터 + serverlogs
├── Realtime — Broadcast / Presence / PostgresChanges
├── pg_cron + pg_net — 스케줄 잡
└── Management API — Setup 자동화
```

---

## Supabase 역할

### 사용 중인 기능

| 기능 | 용도 |
|------|------|
| Auth (GoTrue) | 게스트 자동로그인, OAuth 12종, GPGS/GameCenter, 계정 연동 |
| PostgreSQL | 전체 게임 데이터 저장 (DapperGameDB가 Npgsql로 직접 연결) |
| Realtime | WebSocket 채널 (Feature 템플릿에 포함) |
| Management API | 에디터 Setup 자동화 (프로젝트 목록, Anon Key, Auth URL 동기화) |
| pg_cron + pg_net | Cloud Run 엔드포인트 주기적 호출 |
| REST API (PostgREST) | Config 테이블 직접 조회 (SupabaseRestClient) |
| Storage | 미사용 (구조만 존재) |

### 왜 Supabase인가

- Auth가 게임에 최적: 게스트 → 소셜 연동 패턴을 네이티브 지원
- PostgreSQL 직접 접근 가능: 표준 SQL, 마이그레이션 자유, 탈출 용이
- Free Tier: 500MB DB + 50,000 MAU + 2GB bandwidth

### 제한사항

- Free tier 동시 연결 60개, Pro 200개
- 단일 DB 인스턴스 (vertical scaling만)
- Realtime 동시 연결: Free 200, Pro 500
- AWS 기반이라 GCP 서비스와 크로스 클라우드 통신 발생

---

## Google Cloud Run 역할

### 사용 중인 기능

| 기능 | 용도 |
|------|------|
| 컨테이너 배포 | ASP.NET Docker 이미지 실행 |
| Auto-scaling | 요청 기반 0→N 인스턴스 (max_connections 기반 자동 계산) |
| Artifact Registry | Docker 이미지 저장 + 레이어 캐시 |
| Service Account | GitHub Actions OIDC 인증 |

### 왜 Cloud Run인가

- 서버리스: 트래픽 없으면 $0, 있으면 자동 확장
- Docker 기반: ASP.NET뿐 아니라 어떤 런타임이든 가능
- Free Tier: 월 200만 요청 + 360,000 vCPU-초
- GitHub Actions 연동 완성도 높음

### 비용 구조

| 설정 | 월 비용 | Cold Start |
|------|---------|------------|
| `minInstances=0` | 무료 | 2~5초 |
| `minInstances=1` | ~50,000원 | 없음 |

---

## 알아둘 개념 3가지

### 1. 크로스 클라우드 레이턴시

Cloud Run은 Google 데이터센터, Supabase는 AWS 데이터센터에 있다.
서로 다른 클라우드 간 통신이라 같은 클라우드 내부(~1ms)보다 느리다(~5-15ms).

```
요청 → Cloud Run (Google) ──인터넷──→ Supabase DB (AWS)
                              ↑
                        +5~15ms 추가
```

- API 1회 호출당 +10ms 수준이라 체감은 어려움
- 한 요청에서 DB를 여러 번 조회하면 누적될 수 있음
- 두 서비스의 리전을 가깝게 설정하면 완화 (서울/도쿄)

### 2. Connection Pooling

DB에 데이터를 요청하려면 먼저 연결(connection)을 맺어야 한다.
매번 새로 맺으면 느리고 연결 수도 금방 소진된다.

```
Pooling 없이:
  요청마다 → 연결 생성(50ms) → 쿼리(5ms) → 연결 종료

Pooling 있으면:
  [미리 연결 20개 준비]
  요청마다 → 빌려쓰기(0ms) → 쿼리(5ms) → 반납
```

- Supabase Free tier 동시 연결 60개 제한
- **[해결됨]** connection string에 `Maximum Pool Size` 자동 설정 (max_connections 기반 계산)

### 3. Cold Start

Cloud Run은 `minInstances=0`이면 요청 없을 때 서버를 꺼둔다.
요청이 오면 부팅 → 이때 2~5초 대기 발생.

```
서버 꺼져있을 때:
  요청 → [부팅 2~5초] → 처리 → 응답    (느림)

이미 켜져있을 때:
  요청 → 처리 → 응답 (수십ms)          (빠름)
```

- 첫 유저만 느리고 이후는 빠름
- 일정 시간 트래픽 없으면 다시 꺼짐
- 해결: `minInstances=1` (월 ~5만원) 또는 앱 시작 시 pre-warm 요청

---

## 조합 평가

### 시너지

| 항목 | 설명 |
|------|------|
| 비용 $0 시작 | 양쪽 Free Tier로 출시 전까지 무료 운영 가능 |
| 관심사 분리 | Auth/DB/Realtime = Supabase, 비즈니스 로직 = Cloud Run |
| 탈출 용이 | 표준 PostgreSQL + 표준 Docker → vendor lock-in 최소 |
| 자동화 친화 | Management API + gcloud CLI + gh CLI → 에디터 원버튼 배포 |

### 위험 요소

| 항목 | 심각도 | 대응 |
|------|--------|------|
| 크로스 클라우드 레이턴시 | 낮음 | 리전 가깝게 설정, 쿼리 최소화 |
| ~~Connection Pooling 부재~~ | ~~중간~~ | **해결됨** — Maximum Pool Size 자동 설정 |
| Cold Start | 낮음 | pre-warm 또는 minInstances=1 |
| 모니터링 분산 | 낮음 | Status 탭에서 서버 상태/DB 자동 감지 통합 조회 |

### 대안 비교

| 구성 | 장점 | 단점 |
|------|------|------|
| **Supabase + Cloud Run (현재)** | 비용 최적, Auth 완성도, 탈출 용이 | 크로스 클라우드 레이턴시 |
| Supabase Edge Functions | DB 가까움, 단일 서비스 | Deno 제한, 커스텀 로직 한계 |
| Firebase + Cloud Run | GCP 통합 | NoSQL 제약, Auth 불편 |
| Supabase 단독 (RPC) | 인프라 단순 | 비즈니스 로직 제한 |

---

## 스케일 기준표

| 규모 | 예상 CCU | 현재 구성으로 충분? | 필요 조치 |
|------|---------|-------------------|----------|
| 프로토타입 | ~100 | O | 없음 |
| 소규모 출시 | ~1,000 | O | ~~Connection Pooling 추가~~ (적용 완료) |
| 중규모 | ~5,000 | △ | Supabase Pro + minInstances=1 |
| 대규모 | 10,000+ | X | Read Replica, 별도 DB 고려 |

---

## 현재 배포 파이프라인

```
1. 개발자가 [Table], [Service], [API] 어트리뷰트로 코드 작성
2. Unity 에디터에서 "Deploy" 클릭
3. ServerCodeGenerator가 ASP.NET 코드 + Migration SQL 생성
4. 코드 해시 비교 → 변경 없으면 스킵
5. GitHubPusher가 생성된 코드를 GitHub에 push
6. GitHub Actions 트리거 (deploy.yml)
   → Docker build (캐시 활용) → Artifact Registry push → Cloud Run deploy
7. Program.cs 시작 시 Migration SQL 자동 실행
8. ActionsTracker가 15초 간격으로 상태 폴링
9. 완료 시 Cloud Run URL을 SupaRunSettings에 저장
```

---

## 개선 로드맵

### 개선안 1: 하이브리드 라우팅 (영향도 ★★★★★)

현재 모든 읽기 요청이 Cloud Run을 경유하지만, 대부분은 불필요하다.
Supabase PostgREST로 직접 조회하면 Cloud Run 부하를 70% 줄일 수 있다.

```
현재:
  모든 요청 → Cloud Run → Supabase DB

개선 후:
  [Config] 읽기  → Supabase REST 직접 (공개 데이터)  ← 부분 구현 완료
  [Table] 읽기   → Supabase REST + RLS (본인 데이터만)
  [Service] 호출 → Cloud Run (비즈니스 로직만)
```

QueryOptions는 PostgREST URL 파라미터와 1:1 대응된다:
- `Eq("col", val)` → `?col=eq.val`
- `Gt("col", val)` → `?col=gt.val`
- `Like("col", val)` → `?col=like.*val*`
- `OrderByDesc("col")` → `&order=col.desc`
- `Limit(10)` → `&limit=10`

**현재 진행 상황:** Config 직접 조회를 위한 SupabaseRestClient 구현 완료

**전제 조건:** RLS(Row Level Security) 구현 필요 (Table 직접 조회 시)
- `[Config]`: 공개 읽기 정책 (`USING (true)`)
- `[Table]`: 본인 데이터만 (`USING (playerid = auth.uid()::text)`)

**효과:** Cold Start 영향 제거, Cloud Run 비용 -70%, 레이턴시 -50%

### 개선안 2: 직접 배포 — GitHub 중간 단계 제거 (영향도 ★★★★☆)

GitHub Push → Actions 대기가 배포 시간의 대부분을 차지한다.
`gcloud run deploy --source .`로 직접 배포하면 대폭 단축된다.

```
현재 (15~25분):
  코드 생성(1초) → GitHub Push(30초) → Actions 대기(5분) → Docker(3~8분) → Deploy(1~2분)

개선 후 (3~5분):
  코드 생성(1초) → gcloud run deploy --source .(3~5분) → 끝
```

- GCP Cloud Build가 Dockerfile 빌드 + 배포를 한 번에 처리
- 시크릿: GitHub Secrets → Google Secret Manager로 이동
- GitHub 저장소는 백업/이력용으로 유지 가능 (배포 경로에서 제거)

**효과:** 배포 시간 -70%, 외부 의존성 감소

### 개선안 3: PostgreSQL Functions (영향도 ★★★☆☆)

단순 CRUD 서비스를 DB 함수로 옮기면 Cloud Run 없이 Supabase RPC로 직접 호출 가능.

```sql
-- 예: 재화 추가
CREATE FUNCTION add_currency(p_player_id TEXT, p_currency_id TEXT, p_amount INT)
RETURNS jsonb AS $$
  INSERT INTO currencybalances (id, playerid, currencyid, amount)
  VALUES (p_player_id || '_' || p_currency_id, p_player_id, p_currency_id, p_amount)
  ON CONFLICT (id) DO UPDATE SET amount = currencybalances.amount + p_amount
  RETURNING row_to_json(currencybalances.*)::jsonb;
$$ LANGUAGE plpgsql SECURITY DEFINER;
```

Unity에서 호출: `POST /rest/v1/rpc/add_currency`

| 서비스 복잡도 | 예시 | 적합한 위치 |
|-------------|------|-----------|
| 단순 CRUD | 재화 추가/차감, 출석 체크 | PostgreSQL Function |
| 다중 서비스 연동 | 상점 구매 (재화+인벤토리+로그) | Cloud Run 유지 |
| 외부 API 연동 | GPGS 토큰 검증, 결제 검증 | Cloud Run 유지 |

**한계:** 복잡한 로직은 PL/pgSQL로 작성하기 어려움. C# 코드 공유 장점 사라짐.

### 권장 적용 순서

```
1단계: 직접 배포 (gcloud run deploy)        → 즉시 적용 가능
2단계: Config 직접 조회 (PostgREST)          → 부분 구현 완료 (SupabaseRestClient)
3단계: Table 직접 조회 + RLS                 → 보안 검증 후 적용
4단계: PG Functions (선택)                   → 필요한 서비스만 선별적으로
```

---

## 보안 분석

### Critical — 즉시 수정 필요

| ID | 문제 | 상태 | 위치 | 설명 |
|----|------|------|------|------|
| S1 | SQL Injection (OrderBy) | 수정 불필요 | ServerCodeGenerator.cs | 클라이언트 대면 쿼리 엔드포인트 없음 — Cloud Run 내부에서만 사용 |
| S2 | SQL Injection (Column) | 수정 불필요 | ServerCodeGenerator.cs | 동일 사유 |
| S3 | SQL Injection (Operator) | 수정 불필요 | ServerCodeGenerator.cs | 동일 사유 |
| S4 | Anon Key 빌드 번들 | 수정 불필요 | SupaRunBuildProcessor.cs | Supabase 설계상 공개 키, RLS가 보호 담당 |
| S5 | 토큰 평문 저장 | **해결됨** | SupabaseAuth.cs | SecureStorage 적용 (iOS Keychain + Android KeyStore) |

### High

| ID | 문제 | 상태 | 위치 | 설명 |
|----|------|------|------|------|
| S6 | OAuth redirect 와일드카드 | 낮은 위험 | OAuthHandler.cs | localhost 전용이라 실질 위험 낮음 |
| S7 | Cron Secret 미설정 시 우회 | 수정 불필요 | ServerCodeGenerator.cs | 배포 시 자동 생성됨 |
| S8 | GitHub Secret CLI 인자 노출 | 낮은 위험 | GitHubPusher.cs | 로컬 개발 환경에서만 사용 |
| S9 | Rate Limiting 글로벌 | **해결됨** | Program.cs.template | JWT sub 또는 IP 기반 PartitionedRateLimiter 적용 |
| S10 | 스택트레이스 응답 노출 | **해결됨** | ServerCodeGenerator.cs | `{ error }` 만 반환, 스택트레이스 제거 |
| S11 | JWT Audience 미검증 | 수정 불필요 | Program.cs.template | JWT 서명 검증만으로 충분 |

### Medium / Low

- HTTPS 리다이렉션 미적용 (Program.cs)
- CORS 설정 없음 (WebGL 지원 시 필요)
- 요청 본문이 serverlogs에 평문 로깅 (민감 데이터 포함 가능)
- 클라이언트 JWT 서명 검증 없음 (서버에서 검증하므로 실질 위험 낮음)

---

## 성능 분석

### Critical / High

| ID | 문제 | 상태 | 위치 | 영향 | 수정 난이도 |
|----|------|------|------|------|-----------|
| P1 | Connection Pooling 미적용 | **해결됨** | DapperGameDB (생성 코드) | ~~요청마다 DB 연결 생성~~ → Maximum Pool Size 자동 계산 | 매우 낮음 |
| P2 | Reflection 매 호출 반복 | **해결됨** | LocalGameDB / DapperGameDB | ~~GetFields() 매번 호출~~ → static Dictionary<Type, FieldInfo[]> 캐시 | 중간 |
| P3 | Query 전체 테이블 역직렬화 | 미수정 | LocalGameDB.cs | 1만 행 → 전부 FromJson 후 필터 → 50~100ms | 중간 |
| P4 | Newtonsoft.Json 사용 | 미수정 | SupaRunClient.cs | System.Text.Json 대비 느리고 GC 압박 | 낮음 — 교체 |
| P5 | 응답 캐싱 없음 | 미수정 | SupaRunClient.cs | 동일 요청 반복 시 매번 네트워크 호출 | 중간 |

### Medium

- Assembly 전체 스캔 — 배포 시 100~500ms (DeployManager)
- WebSocket 메시지마다 StringBuilder 할당 (SupabaseRealtime)
- Transaction 스냅샷 전체 딕셔너리 복사 (LocalGameDB)
- ~~서버 응답 Gzip 압축 없음~~ → **해결됨** (Response Compression 적용)
- 벌크 작업 엔드포인트 없음

---

## 안정성 / 코드 품질 분석

### Critical

| ID | 문제 | 상태 | 위치 | 설명 |
|----|------|------|------|------|
| R1 | 토큰 갱신 미구현 | **해결됨** | SupaRunClient.cs | SupaRunClient.OnTokenRefresh → SupabaseAuth.TryRefreshToken 연결 완료 |
| R2 | `.Result` 블로킹 호출 | **해결됨** | OAuthHandler.cs | 2곳 `.Result` → `await` 변환 완료 |
| R3 | Auth 초기화 fire-and-forget | 미수정 | SupaRun.cs:174 | 예외 무시됨 — 인증 실패 감지 불가 |

### High

| ID | 문제 | 상태 | 위치 | 설명 |
|----|------|------|------|------|
| R4 | Bare `catch { }` | **해결됨** | ServerCodeGenerator, OAuthHandler 등 | 6곳 수정, Debug.LogWarning 추가 |
| R5 | LocalGameDB 스레드 안전성 | 미수정 | LocalGameDB.cs | Dictionary가 ConcurrentDictionary 아님 |
| R6 | Realtime 타임아웃 상태 불일치 | 미수정 | RealtimeChannel.cs | 타임아웃 후 join 완료 시 상태 충돌 |
| R7 | CancellationToken 미지원 | 미수정 | 전체 async 메서드 | 앱 종료 시 graceful shutdown 불가 |
| R8 | WebGL 호환성 없음 | 미수정 | OAuthHandler, SupabaseRealtime | HttpListener, ClientWebSocket 미지원 |

### Medium

- JWT 파싱 실패 시 `return null` (SupabaseAuth)
- UnityWebRequest `using` 누락 (SupabaseAuth 일부)
- Android Manifest 생성 실패 시 경고만 (빌드 계속 진행)
- SupabaseStorage 미구현 (스텁만 존재)
- `OnKicked` 이벤트 선언만 있고 미구현

---

## 수정 우선순위

### 해결 완료

| 항목 | 분류 | 수정 내용 |
|------|------|----------|
| 토큰 보안 저장 (S5) | 보안 | SecureStorage 적용 (iOS Keychain + Android KeyStore) |
| Rate Limiting 유저별 (S9) | 보안 | JWT sub 또는 IP 기반 PartitionedRateLimiter |
| 스택트레이스 제거 (S10) | 보안 | 프로덕션에서 `{ error }` 만 반환 |
| 토큰 갱신 연결 (R1) | 안정성 | SupaRunClient.OnTokenRefresh → SupabaseAuth.TryRefreshToken |
| `.Result` → `await` (R2) | 안정성 | OAuthHandler.cs 2곳 변환 |
| Bare catch 수정 (R4) | 안정성 | 6곳 수정, Debug.LogWarning 추가 |
| Connection Pooling (P1) | 성능 | Maximum Pool Size 자동 계산 (max_connections 기반) |
| Reflection 캐싱 (P2) | 성능 | static Dictionary<Type, FieldInfo[]> 캐시 (LocalGameDB + DapperGameDB) |
| 응답 압축 (Gzip) | 성능 | Response Compression 미들웨어 추가 |

### 수정 불필요로 판단

| 항목 | 분류 | 사유 |
|------|------|------|
| SQL Injection (S1~S3) | 보안 | 클라이언트 대면 쿼리 엔드포인트 없음 |
| Anon Key 빌드 번들 (S4) | 보안 | Supabase 설계상 공개 키, RLS가 보호 |
| OAuth redirect 와일드카드 (S6) | 보안 | localhost 전용, 낮은 위험 |
| Cron Secret 미설정 (S7) | 보안 | 배포 시 자동 생성됨 |
| GitHub Secret CLI (S8) | 보안 | 로컬 개발 환경, 낮은 위험 |
| JWT Audience 미검증 (S11) | 보안 | JWT 서명 검증만으로 충분 |

### 새로운 기능 추가

| 항목 | 설명 |
|------|------|
| Config 직접 조회 | SupabaseRestClient를 통한 Supabase REST 직접 쿼리 (부분 구현) |
| 응답 압축 | Gzip Response Compression 적용 |
| Status 탭 | 대시보드에 서버 상태, DB 자동 감지 표시 |
| 스케일링 자동 설정 | max_connections → Pool Size / Max Instances 자동 계산 |

### 남은 항목 — 필요 시 수정

| 항목 | 분류 | 수정 내용 | 우선도 |
|------|------|----------|--------|
| Auth fire-and-forget (R3) | 안정성 | 인증 실패 감지 로직 추가 | 중간 |
| LocalGameDB 스레드 안전성 (R5) | 안정성 | ConcurrentDictionary 교체 | 낮음 (개발 전용) |
| Realtime 타임아웃 (R6) | 안정성 | 상태 불일치 해소 | 낮음 |
| CancellationToken (R7) | 안정성 | graceful shutdown 지원 | 낮음 |
| WebGL 호환 (R8) | 호환성 | `#if !UNITY_WEBGL` 가드 | 낮음 (필요 시) |
| Query 전체 역직렬화 (P3) | 성능 | 필터 중 역직렬화 | 낮음 (개발 전용) |
| Newtonsoft.Json (P4) | 성능 | System.Text.Json 교체 | 낮음 |
| 응답 캐싱 (P5) | 성능 | ETag 또는 클라이언트 캐시 | 중간 |

---

## 긍정적 평가

| 영역 | 잘된 점 |
|------|--------|
| 아키텍처 | 어트리뷰트 → 코드 생성 → 배포 자동화 파이프라인이 매우 독창적 |
| 에러 처리 | SupaRunClient의 retry + 에러 분류가 프로덕션 수준 |
| 시크릿 관리 | 민감 정보를 EditorPrefs로 분리, .gitignore 적용 |
| 캐시 전략 | Docker 레이어 분리 + 코드 해시 비교로 불필요 배포 스킵 |
| Source Generator | Incremental Generator 사용으로 컴파일 영향 최소화 |
| DX | Setup 자동화, 원버튼 배포, Status 탭으로 서버 상태 통합 조회 |
| 보안 강화 | SecureStorage, 유저별 Rate Limiting, 스택트레이스 제거 적용 |
| 성능 최적화 | Connection Pooling 자동 계산, Reflection 캐싱, Gzip 압축 적용 |

---

*마지막 업데이트: 2026-03-27*
