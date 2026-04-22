# EOS Lobby Wrapper

## 상태
wip

## 용도
EOS Lobby SDK의 얇은 비동기 래퍼. Create / Search / Join / Leave / UpdateAttribute + 멤버 변경 콜백을 Awaitable/event 스타일로 제공한다. 게임 규약(멤버 수, attribute 의미)은 모르며, 호출자가 정책을 주입한다.

## 의존성
- com.playeveryware.eos — `EOSManager.Instance.GetEOSLobbyInterface()`, Lobby/Search Handle
- ../Auth/Feature.md — `EOSConnectLogin.LocalUserId` (ProductUserId)
- ../Transport/Connection.Feature.md — Lobby attribute로 받은 host ProductUserId를 `EOSTransportUtility.RegisterRemotePeer`에 주입 (호출은 상위 피처)

## 포함 기능
- **EOSLobbyService** (plain class) — Lobby SDK 호출 래퍼
  - `CreateAsync(LobbyCreateOptions)` → `Awaitable<LobbyInfo>` — BucketId + MaxMembers + PermissionLevel + 초기 Attribute 세팅
  - `SearchAsync(LobbySearchCriteria)` → `Awaitable<LobbyInfo[]>` — BucketId 1차 + Attribute 2차 필터
  - `JoinAsync(string lobbyId)` → `Awaitable<LobbyInfo>`
  - `LeaveAsync()` → `Awaitable<bool>`
  - `UpdateAttributeAsync(string key, string value)` / `UpdateAttributeAsync(string key, long value)`
  - `CurrentLobby` — 현재 가입된 로비 스냅샷 (없으면 null)
  - `OnMemberChanged` event — 멤버 입장/퇴장 콜백
  - `OnAttributeChanged` event — 로비 attribute 변경 콜백 (status 전이 감지용)
- **LobbySearchCriteria** — 필터 빌더
  - `BucketId` (필수)
  - `RequireAttribute(key, value)` — 문자열 일치
  - `MaxResults` — 기본 10
- **LobbyInfo** — 조회 결과 스냅샷 (LobbyId, OwnerId, Members, Attributes dictionary)
- **EOS 콜백 해제** — `Dispose()` / 오너 파괴 시 Notification ID 해제 (리소스 릭 방지)

## 구조
| 파일 | 설명 |
|------|------|
| EOSLobbyService.cs | LobbyInterface 래퍼. Create/Search/Join/Leave/UpdateAttribute + 3종 notification 관리 |
| LobbyInfo.cs | 로비 스냅샷 POCO (LobbyId/Owner/Members/Attributes) |
| LobbyCreateRequest.cs | CreateAsync 입력 POCO (BucketId + 초기 attributes) |
| LobbySearchCriteria.cs | SearchAsync 필터 빌더 (BucketId + attribute equals) |
| Feature.md | 이 문서 |

## API (외부 피처가 참조 가능)
- `EOSLobbyService.CreateAsync(LobbyCreateRequest) -> Awaitable<LobbyInfo>` — `Runtime/Lobby/EOSLobbyService.cs`
- `EOSLobbyService.SearchAsync(LobbySearchCriteria) -> Awaitable<IReadOnlyList<LobbyInfo>>` — 동 파일
- `EOSLobbyService.JoinAsync(string lobbyId) -> Awaitable<LobbyInfo>` — 동 파일
- `EOSLobbyService.LeaveAsync() -> Awaitable<bool>` — 동 파일
- `EOSLobbyService.UpdateStringAttributeAsync(string key, string value) -> Awaitable<bool>` — 동 파일
- `EOSLobbyService.UpdateInt64AttributeAsync(string key, long value) -> Awaitable<bool>` — 동 파일
- `EOSLobbyService.CurrentLobby -> LobbyInfo` — 현재 가입 로비 스냅샷 (없으면 null)
- `EOSLobbyService.OnLobbyUpdated : Action<LobbyInfo>` — 전체 attribute/멤버 업데이트 알림
- `EOSLobbyService.OnMemberStatusChanged : Action<ProductUserId, LobbyMemberStatus>` — 멤버 입/퇴장 알림
- `LobbyInfo.TryGetString(key, out value)` / `.TryGetInt64(key, out value)` — attribute 조회

## 주의사항
- EOS Lobby API는 callback 기반 — `TaskCompletionSource` 또는 Unity `AwaitableCompletionSource` 패턴으로 Awaitable 래핑
- 콜백 해제 누락 = 리소스 릭 (Connection.Feature.md의 Notification 해제 패턴 그대로)
- Attribute 값은 String/Int64만 허용 (EOS SDK 제약). Bool은 문자열 "true"/"false".
- PermissionLevel `PublicAdvertised` 권장 — `JoinViaPresence`는 친구 UI 필요
- Search 결과가 인덱스 지연으로 Create 직후 누락될 수 있음 (호출자가 지연/재시도 책임)
- 네임스페이스: `Tjdtjq5.EOS.Lobby`
- 패키지 asmdef(`Tjdtjq5.EOS.Runtime`) 하위라 별도 asmdef 추가 불필요
