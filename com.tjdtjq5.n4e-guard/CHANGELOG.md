# Changelog

## [0.1.0] - 2026-04-22

### 초기 배포

SurvivorsDuo 프로젝트에서 Photon Quantum 전환에 따른 보존 목적으로 이관.

**Runtime**:
- `GhostLocalCheck` — Ghost 엔티티 로컬 소유 판정 (Enableable Component 이슈 회피)
- `RpcGuard` — RPC 송수신 시 World 상태 + NetworkId 검증
- `NetworkIdMapper` — Client/Server Ghost Entity 매핑
- `WorldRegistrar`, `NetcodeWorldHelper` — Client/Server World 지연 등록 + 조회
- `NetcodeSubSceneLoader` — SubScene 로딩 대기 유틸
- `MppmRoleDetector` — MPPM Virtual Player 역할 감지 (Host/Client)

**Editor**:
- `N4EGuardValidator` — 금지 패턴 정적 검증

**의존성**:
- `com.unity.entities` 1.4.2+
- `com.unity.netcode` 1.10.0+
