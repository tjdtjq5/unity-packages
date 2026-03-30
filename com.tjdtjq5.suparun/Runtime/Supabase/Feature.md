# Supabase Feature

- **상태**: stable (Auth/Storage는 stub)
- **용도**: Supabase 저수준 클라이언트. Realtime WebSocket(Broadcast/Presence/PostgresChanges), Auth, Storage 접근점.

## 의존성

| 대상 | 경로 | 용도 |
|------|------|------|
| (없음) | - | 외부 의존성 없음. 독립 네임스페이스 `Tjdtjq5.SupaRun.Supabase` |

## 구조

```
Supabase/
├── SupabaseClient.cs      # Supabase 통합 클라이언트 (Auth/Realtime/Storage 프로퍼티)
├── SupabaseAuth.cs         # Supabase Auth stub (TODO: Phase 1/3 구현 예정)
├── SupabaseRealtime.cs     # Realtime WebSocket (Phoenix Channel 프로토콜 vsn=1.0.0)
├── RealtimeChannel.cs      # 채널 단위 Broadcast/Presence/PostgresChanges 처리
├── RealtimeTypes.cs        # Realtime 관련 타입 (PhoenixMessage, PresenceUser, PgChangeData 등)
└── SupabaseStorage.cs      # Storage stub (TODO: Phase 3 구현 예정)
```

## API

### SupabaseClient

| 멤버 | 설명 |
|------|------|
| `SupabaseClient(url, anonKey)` | 생성자. Auth/Realtime/Storage 자동 생성. |
| `Url` | Supabase 프로젝트 URL. |
| `AnonKey` | Supabase Anon Key. |
| `Auth` | SupabaseAuth 인스턴스. |
| `Realtime` | SupabaseRealtime 인스턴스. |
| `Storage` | SupabaseStorage 인스턴스. |

### SupabaseRealtime

| 메서드/프로퍼티 | 설명 |
|----------------|------|
| `SupabaseRealtime(supabaseUrl, anonKey)` | 독립 생성 (SupabaseClient 없이). |
| `Channel(string name)` | 채널 생성/가져오기. Subscribe() 전까지 연결 안 함. |
| `SetAccessToken(string token)` | RLS 인증용 액세스 토큰 설정. 기존 채널에도 토큰 갱신. |
| `Disconnect()` | 전체 WebSocket 연결 종료. |
| `IsConnected` | WebSocket 연결 상태. |

### RealtimeChannel (builder pattern)

**Broadcast**

| 메서드 | 설명 |
|--------|------|
| `OnBroadcast(eventName, callback)` | 이벤트 수신 콜백 등록. |
| `SendBroadcast(eventName, payload)` | 메시지 전송. |
| `SetBroadcastSelf(bool)` | 자기 메시지도 수신할지 설정. |
| `SetBroadcastAck(bool)` | 서버 확인 응답 받을지 설정. |

**Presence**

| 메서드 | 설명 |
|--------|------|
| `TrackPresence(metadata)` | Presence 추적 시작. |
| `UntrackPresence()` | Presence 추적 중지. |
| `OnPresenceSync(callback)` | 전체 접속자 목록 변경 콜백. |
| `OnPresenceJoin(callback)` | 접속자 입장 콜백. |
| `OnPresenceLeave(callback)` | 접속자 퇴장 콜백. |

**Postgres Changes**

| 메서드 | 설명 |
|--------|------|
| `OnPostgresChange(table, evt, callback, filter?)` | DB 변경 감지 (raw Dictionary). |
| `OnPostgresChange<T>(table, evt, callback, filter?)` | DB 변경 감지 (타입 안전 역직렬화). |

**구독**

| 메서드 | 설명 |
|--------|------|
| `Subscribe()` | 채널 구독 시작. 첫 호출 시 WebSocket 자동 연결. |
| `Unsubscribe()` | 채널 구독 해제. 채널 0개 시 자동 연결 종료. |
| `State` | 채널 상태 (Closed/Joining/Joined/Leaving). |

### 주요 타입

| 타입 | 설명 |
|------|------|
| `PhoenixMessage` | Phoenix 프로토콜 메시지 (topic, event, payload, ref). |
| `PresenceUser` | Presence 사용자 (key + metadata). |
| `PgChangeData` | PostgresChanges 데이터 (schema, table, eventType, newRecord, old). |
| `ChangeEvent` | 변경 이벤트 enum (Insert, Update, Delete, All). |
| `ChannelState` | 채널 상태 enum (Closed, Joining, Joined, Leaving). |

## 주의사항

- `SupabaseAuth`(이 폴더)와 `Auth/SupabaseAuth`(상위 Auth 폴더)는 별개 클래스이다. 이 폴더의 것은 `Tjdtjq5.SupaRun.Supabase` 네임스페이스의 stub이고, Auth 폴더의 것이 실제 인증 구현이다.
- `SupabaseStorage`도 stub 상태이며 Phase 3에서 Upload/Download/Delete를 구현할 예정이다.
- WebSocket 수신은 백그라운드 스레드에서 동작하며, 콜백은 `RealtimeDispatcher`(MonoBehaviour)가 메인 스레드로 디스패치한다.
- `RealtimeDispatcher`는 `[RuntimeInitializeOnLoadMethod]`로 자동 생성되며 DontDestroyOnLoad이다.
- 하트비트 간격은 25초이며, 응답이 없으면 자동 재연결한다.
- 재연결은 지수 백오프 (1s, 2s, 5s, 10s)로 시도하며, 성공 시 기존 채널을 자동 재구독한다.
- Phoenix Channel 프로토콜 vsn=1.0.0을 따른다 (Supabase Realtime 서버 호환).
