# EOS Wrapper

Epic Online Services를 Unity NetCode for Entities와 통합하는 래퍼 패키지.

## 기능

| 영역 | 내용 |
|------|------|
| Auth | `EOSConnectLogin` - EOS Connect 로그인 흐름 |
| Lobby | `EOSLobbyService` - 로비 생성/검색/참가 (`LobbyInfo`, `LobbyCreateRequest`, `LobbySearchCriteria`) |
| Transport | `EOSP2PNetworkInterface`, `EOSTransportPoller`, `EOSAndIpcDriverConstructor` - N4E Transport 커스텀 드라이버 (P2P/IPC 겸용) |
| Utility | `EOSContext`, `EOSTransportUtility`, `EOSNetworkDiagnostics` |
| Build | `EOSAndroidGradlePatcher` (Editor) - Android 빌드 gradle 자동 패치 |

## 설치

```json
"com.tjdtjq5.eos-wrapper": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.eos-wrapper#eos-wrapper/v0.1.0"
```

## 의존성

- `com.playeveryware.eos` 6.0.2+
- `com.tjdtjq5.n4e-guard` 0.1.0+

## 참고

이 패키지는 SurvivorsDuo 프로젝트에서 Photon Quantum으로 네트워크 스택을 전환하면서 보존 목적으로 모노레포에 이관된 코드. 향후 EOS + N4E 조합이 필요한 프로젝트에서 재사용 가능.
