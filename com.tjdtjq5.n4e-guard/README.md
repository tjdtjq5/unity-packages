# N4E Guard

NetCode for Entities 사용 시 흔한 실수와 런타임 문제를 방지하는 안전장치 + 유틸리티.

## 기능

| 기능 | 설명 |
|------|------|
| `GhostLocalCheck` | Ghost 엔티티가 로컬 소유인지 안전하게 판정 (Enableable Component 이슈 회피) |
| `RpcGuard` | RPC 송수신 시 World 상태 + NetworkId 검증 |
| `NetworkIdMapper` | Client/Server Ghost Entity 매핑 유틸 |
| `WorldRegistrar` | Client/Server World 지연 등록 + 접근 헬퍼 |
| `NetcodeWorldHelper` | Client/Server World 조회 유틸 |
| `NetcodeSubSceneLoader` | SubScene 로딩 완료 대기 유틸 |
| `MppmRoleDetector` | MPPM Virtual Player 역할 감지 (Host / Client) |
| `N4EGuardValidator` (Editor) | 금지 패턴 정적 검증 |

## 설치

```json
"com.tjdtjq5.n4e-guard": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.n4e-guard#n4e-guard/v0.1.0"
```

## 의존성

- `com.unity.entities` 1.4.2+
- `com.unity.netcode` 1.10.0+

## 참고

이 패키지는 SurvivorsDuo 프로젝트에서 Photon Quantum으로 네트워크 스택을 전환하면서 보존 목적으로 모노레포에 이관된 코드. 향후 N4E 사용 시 안전장치로 재사용 가능.
