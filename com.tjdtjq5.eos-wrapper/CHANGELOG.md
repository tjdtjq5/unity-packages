# Changelog

## [0.1.0] - 2026-04-22

### 초기 배포

SurvivorsDuo 프로젝트에서 Photon Quantum 전환에 따른 보존 목적으로 이관.

**Runtime**:
- `EOSConnectLogin` — EOS Connect 로그인 흐름
- `EOSLobbyService` + `LobbyInfo`, `LobbyCreateRequest`, `LobbySearchCriteria` — 로비 생성/검색/참가
- `EOSP2PNetworkInterface`, `EOSTransportPoller`, `EOSAndIpcDriverConstructor` — N4E Transport 커스텀 드라이버 (P2P/IPC 겸용)
- `EOSContext`, `EOSTransportUtility`, `EOSNetworkDiagnostics` — 유틸리티

**Editor**:
- `EOSAndroidGradlePatcher` — Android 빌드 gradle 자동 패치

**의존성**:
- `com.playeveryware.eos` 6.0.2+
- `com.tjdtjq5.n4e-guard` 0.1.0+
