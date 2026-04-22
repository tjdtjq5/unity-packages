# N4EGuard — Netcode for Entities 가드레일

## 상태
wip

## 용도
Unity Netcode for Entities 사용 시 반복되는 실수를 방지하고 보일러플레이트를 줄이는 가드레일 모듈.
Netcode API를 래핑하지 않는다 — 개발자는 여전히 `SystemAPI`, `EntityManager`를 직접 사용하고, 이 모듈은 유틸리티 + 검증만 제공한다.

향후 `com.tjdtjq5.n4e-guard` UPM 패키지로 추출 예정.

## 네임스페이스
`N4EGuard`

## 의존성
- Unity.Entities (com.unity.entities)
- Unity.Netcode (com.unity.netcode)
- Unity.Collections (com.unity.collections)
- Unity.Multiplayer.PlayMode (com.unity.multiplayer.playmode, MppmRoleDetector용)
- 프로젝트 코드 의존 없음 (순수 유틸리티)

## 로드맵

| # | 경로 | 피처명 | 상태 | Phase | 의존성 |
|---|------|--------|------|-------|--------|
| 1 | N4EGuard/Runtime | Core Helpers (GhostLocalCheck + NetcodeWorldHelper) | wip | 1 | 없음 |
| 2 | N4EGuard/Runtime | RPC Guard (RpcGuard) | wip | 1 | 없음 |
| 3 | N4EGuard/Runtime | World & Entity (NetworkIdMapper + WorldRegistrar) | wip | 2 | Pillar 1 |
| 4 | (프로젝트 코드) | Migration & Bug Fix | wip | 2 | Pillar 1~3 |
| 5 | N4EGuard/Editor | Validation Rules (B1~B9) | wip | 3 | Pillar 1~3 |
| 6 | N4EGuard/Runtime/Multiplayer | MPPM 지원 (MppmRoleDetector) | **stable** | 3 | 없음 |

## Pillar 4 — Migration & Bug Fix 상세

Pillar 1~3 헬퍼를 기존 프로젝트 코드에 적용하면서 발견된 버그도 수정.

### 버그 수정 (C1~C3)
- **C1** `FieldOrbOwnerHideSystem.cs` — `SystemAPI.Query<GhostOwnerIsLocal>`가 disabled Entity 포함 → localPlayerIndex 항상 1
- **C2** `ProjectileVfxBridge.cs` OnDestroy — `_linkQuery` 등 3개 EntityQuery Dispose 누락
- **C3** ✅ **해결** (2026-04-19 VP 근접무기 Phase 3). `SkillTargeting.cs` 삭제 → `Combat/Weapon/Runtime/SpatialHashTargetFinder.cs`로 흡수 (instance 필드 + `IDisposable` 구현, VContainer Singleton이라 LifetimeScope Dispose 시 `_clientEnemyQuery` 자동 정리).

### 인라인 패턴 교체 대상 (10곳+)
- `GhostOwnerIsLocal` 체크 → `GhostLocalCheck.IsLocalOwner()` (3곳)
- `ClientServerBootstrap.ServerWorlds` 순회 → `NetcodeWorldHelper.GetServerWorld()` (6곳+)
- `ClientServerBootstrap.ClientWorlds` 순회 → `NetcodeWorldHelper.GetClientWorld()` (4곳+)
- `ServerWorlds.Count > 0` 호스트 판별 → `NetcodeWorldHelper.IsHost()` (3곳)
- `DefaultGameObjectInjectionWorld` 직접 사용 → `NetcodeWorldHelper.GetServerWorld()` (4곳)
- RPC 송신 3줄 패턴 → `RpcGuard.Send()` (5곳)

## 구조
(아직 없음 — /ft:build 후 업데이트)

## 주의사항
- **Runtime asmdef 분리 완료** (`N4EGuard.Runtime.asmdef`). 패키지 UPM 추출 준비.
- 다른 asmdef에서 N4EGuard Runtime 사용하려면 references에 `N4EGuard.Runtime` 명시.
- Editor asmdef(`N4EGuard.Editor.asmdef`)는 별도 유지.
- Burst 호환 불필요 — MB/managed 코드에서 사용하는 가드레일
