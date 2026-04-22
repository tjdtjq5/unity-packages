# N4EGuard Runtime — 런타임 헬퍼

## 상태
wip

## 용도
Netcode for Entities의 반복 패턴을 안전하게 사용하기 위한 Runtime 유틸리티.
모든 메서드는 static (상태 없음, DI 불필요).

## 의존성
- Unity.Entities
- Unity.Netcode
- Unity.Collections
- Unity.Multiplayer.PlayMode (MppmRoleDetector가 `CurrentPlayer.IsMainEditor` 참조)

## 포함 기능

### Phase 1 — Core Helpers (Pillar 1)

- **GhostLocalCheck** — `GhostOwnerIsLocal` 안전 체크 1줄 헬퍼
  - `static bool IsLocalOwner(EntityManager em, Entity entity)` → HasComponent + IsComponentEnabled 통합
  - MB에서 EntityManager 직접 사용 시 IEnableableComponent 트랩 방지
  - 근거: forbidden rule `has-component-ghost-local`, learning `ghost-owner-is-local-enableable`
  - 적용 대상: PlayerGhostConnector(77행), PlayerStateBridge, FieldOrbOwnerHideSystem(C1 버그)

- **NetcodeWorldHelper** — World 탐색/판별 단일 진입점
  - `static World GetClientWorld()` — ClientServerBootstrap.ClientWorlds 순회, IsCreated 체크 포함
  - `static World GetServerWorld()` — ServerWorlds 순회
  - `static World GetVisualWorld()` — ClientWorld 우선, DefaultWorld 폴백
  - `static bool IsHost()` — ServerWorlds에 NetworkStreamInGame 존재 여부로 판별 (MPPM 안전)
  - 근거: 10곳+ 인라인 중복, DefaultGameObjectInjectionWorld 함정 (게스트에서 ServerSimulation 시스템 못 찾음)
  - 적용 대상: SkillHitEventBridge, SpatialHashTargetFinder(구 SkillTargeting 흡수, 2026-04-19 Phase 3), ProjectileVfxBridge, CombatRequestRouter, PlayerStateBridge, PlayerGhostConnector, PlayerInputCollectorMb, FieldOrbBridge, CurrencyBridge, WaveEcsBridge

### Phase 1 — RPC Guard (Pillar 2)

- **RpcGuard** — RPC 송수신 편의 + 안전장치
  - `static Entity Send<T>(EntityManager em, T rpc) where T : unmanaged, IRpcCommand` — 브로드캐스트
  - `static Entity Send<T>(EntityManager em, T rpc, Entity targetConnection)` — 유니캐스트
  - 근거: 5곳 송신 보일러플레이트 (CreateEntity + SetComponentData + AddComponent<SendRpcCommandRequest> 3줄 반복)
  - 적용 대상: CombatRequestRouter, GoInGameClientSystem, DamageRpcSendSystem, WaveStateSyncSystem, GameEndRpc 관련

### Phase 2 — World & Entity (Pillar 3)

- **NetworkIdMapper** — Cross-world Entity 해석 + 캐싱
  - `Entity ResolveByNetworkId(World targetWorld, int networkId)` — NetworkId로 대응 Entity 검색
  - 내부 캐싱으로 매 프레임 선형탐색 방지 (현재 CombatRequestRouter.FindPlayerGhostByIndex가 매번 CreateEntityQuery)
  - `void Invalidate()` — Ghost 변경 시 캐시 무효화
  - 근거: forbidden rule `cross-world-entity`, learning `client-server-entity-mismatch`
  - 적용 대상: PlayerStateBridge.SyncStatsToECS, CombatRequestRouter.FindPlayerGhostByIndex, CombatFireReceiveSystem.ResolveOwner

- **WorldRegistrar** — World.All 순회 + LateWorld 등록 통합
  - 기존 GameDataBridge/EnemyVisualRegistry의 "모든 World에 싱글톤 등록" 패턴 추출
  - LateWorldRegisterSystem과 통합하여 단일 등록 경로 제공
  - static 참조 lifecycle race 조건 방지
  - 근거: 2곳+ 동일 패턴 (World.All 순회 + static SharedBlob/SharedInstance + LateWorld 보완)
  - 적용 대상: GameDataBridge, EnemyVisualRegistry, LateWorldRegisterSystem

### Phase 3 — Multiplayer (MPPM 지원)

- **MppmRoleDetector** — MPPM 프로세스 역할 판별 + Netcode Driver 분기 헬퍼
  - `static bool IsMainEditor` — 현재 프로세스가 Main Editor인가 (빌드에서는 true 고정)
  - `static bool IsVirtualPlayer` — MPPM VP인가
  - `static bool ShouldClientUseSocket(NetDebug)` — Netcode Client Driver가 Socket을 써야 하는지 판정 (VP 가드 + `DefaultDriverBuilder.ClientUseSocketDriver` 폴백)
  - **원칙: 읽기 전용** — MultiplayerPlayModePreferences/EditorPrefs를 수정하지 않음. 사용자가 PlayMode Tools에서 바꾼 값 존중.
  - 근거: MPPM 2.x에서 VP가 기본 PlayType=ClientAndServer로 뜨면 ServerWorld가 생성되어 공식 `ClientUseSocketDriver`가 IPC를 택함 → VP는 자체 서버를 listen하지 않으므로 loopback 실패.
  - 적용 대상: `Tjdtjq5.EOS.Transport.EOSAndIpcDriverConstructor.CreateClientDriver`

## 구조

| 파일 | 설명 |
|------|------|
| `GhostLocalCheck.cs` | GhostOwnerIsLocal 안전 체크 (IsLocalOwner) |
| `NetcodeWorldHelper.cs` | World 탐색/판별 (GetClientWorld, GetServerWorld, GetVisualWorld, IsHost, HasServerWorld) |
| `RpcGuard.cs` | RPC 송신 편의 (Send — EM/ECB x Broadcast/Unicast 4 overloads) |
| `NetworkIdMapper.cs` | Cross-world Entity 해석 (Resolve by NetworkId + cache) |
| `WorldRegistrar.cs` | World.All 순회 singleton 등록 (SetSingleton, TrySetSingleton) |
| `Multiplayer/MppmRoleDetector.cs` | MPPM 프로세스 판별 + Netcode Driver 분기 헬퍼 |
| `N4EGuard.Runtime.asmdef` | Runtime 어셈블리 정의 (Unity.Multiplayer.PlayMode 참조) |

## API (외부 피처가 참조 가능)

### Phase 1 — Core Helpers + RPC Guard
- `GhostLocalCheck.IsLocalOwner(EntityManager, Entity) -> bool` — `GhostLocalCheck.cs`
- `NetcodeWorldHelper.GetClientWorld() -> World` — `NetcodeWorldHelper.cs`
- `NetcodeWorldHelper.GetServerWorld() -> World` — `NetcodeWorldHelper.cs`
- `NetcodeWorldHelper.GetVisualWorld() -> World` — `NetcodeWorldHelper.cs`
- `NetcodeWorldHelper.IsHost() -> bool` — `NetcodeWorldHelper.cs`
- `NetcodeWorldHelper.HasServerWorld() -> bool` — `NetcodeWorldHelper.cs`
- `RpcGuard.Send<T>(EntityManager, T) -> Entity` — `RpcGuard.cs`
- `RpcGuard.Send<T>(EntityManager, T, Entity) -> Entity` — `RpcGuard.cs`
- `RpcGuard.Send<T>(EntityCommandBuffer, T) -> Entity` — `RpcGuard.cs`
- `RpcGuard.Send<T>(EntityCommandBuffer, T, Entity) -> Entity` — `RpcGuard.cs`

### Phase 2 — World & Entity
- `NetworkIdMapper.Bind(World)` — `NetworkIdMapper.cs`
- `NetworkIdMapper.Resolve(int networkId) -> Entity` — `NetworkIdMapper.cs`
- `NetworkIdMapper.Invalidate()` — `NetworkIdMapper.cs`
- `NetworkIdMapper.Dispose()` — `NetworkIdMapper.cs`
- `WorldRegistrar.SetSingleton<T>(World, T) -> Entity` — `WorldRegistrar.cs`
- `WorldRegistrar.SetSingletonInAllWorlds<T>(T) -> int` — `WorldRegistrar.cs`
- `WorldRegistrar.TrySetSingleton<T>(World, T) -> bool` — `WorldRegistrar.cs`

### Phase 3 — Multiplayer
- `N4EGuard.Multiplayer.MppmRoleDetector.IsMainEditor -> bool` — `Multiplayer/MppmRoleDetector.cs`
- `N4EGuard.Multiplayer.MppmRoleDetector.IsVirtualPlayer -> bool` — `Multiplayer/MppmRoleDetector.cs`
- `N4EGuard.Multiplayer.MppmRoleDetector.ShouldClientUseSocket(NetDebug) -> bool` — `Multiplayer/MppmRoleDetector.cs`

## 주의사항
- NetworkIdMapper는 IDisposable — 사용자가 반드시 Dispose 호출 (OnDestroy 등)
- NetworkIdMapper.Bind()는 기존 World 바인딩을 교체 (이전 query 자동 정리)
- WorldRegistrar는 LateWorldRegisterSystem과 별개 — 통합은 Pillar 4(Migration)에서 진행
- static 메서드 (WorldRegistrar, GhostLocalCheck, NetcodeWorldHelper, RpcGuard)는 Burst 비호환
- **MppmRoleDetector는 읽기 전용** — EditorPrefs/MultiplayerPlayModePreferences를 수정하지 않는다. 사용자가 PlayMode Tools에서 바꾼 값은 그대로 존중됨.
- Runtime에 `N4EGuard.Runtime.asmdef` 분리됨. 다른 asmdef에서 사용하려면 references에 `N4EGuard.Runtime` 추가 필요.
