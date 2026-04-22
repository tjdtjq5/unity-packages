# EOS Transport — Netcode for Entities Integration

## 상태
wip

## 용도
Unity Netcode for Entities 통합 레이어. `INetworkStreamDriverConstructor` 구현과 진단 유틸 제공.
Netcode의 **"Server 다중 드라이버 OK / Client 단일 드라이버 강제"** 제약에 맞춰 Driver를 등록.

## 의존성
- ./Feature.md — Skeleton (`EOSP2PNetworkInterface`)
- ./Connection.Feature.md — `EOSTransportUtility.RegisterRemotePeer`
- com.unity.netcode — `INetworkStreamDriverConstructor`, `DefaultDriverBuilder`, `ClientServerBootstrap`, `NetworkStreamRequestConnect/Listen`, `NetworkStreamConnection`, `NetworkId`
- com.unity.entities — `World`, `EntityManager`
- `N4EGuard.Runtime` (`N4EGuard.Multiplayer.MppmRoleDetector`) — Client Driver의 Socket/IPC 분기 판정 위임

## 포함 기능
- **EOSAndIpcDriverConstructor**
  - **Server**: IPC + EOS Socket **병행 등록** (호스트 로컬 클라 + 리모트 클라 양쪽 수락)
  - **Client**: `MppmRoleDetector.ShouldClientUseSocket`에 분기 위임 (MPPM VP면 Socket 강제, 그 외 Netcode 공식 로직 폴백)
- **EOSNetworkDiagnostics** — 월드/드라이버/연결 상태 덤프 유틸 (`DumpState`, `Log`)
- **RegisterRemotePeer Guard rail** — Transport 미초기화 시 해결 레시피 포함 에러 메시지

## 구조

| 파일 | 설명 |
|------|------|
| `EOSAndIpcDriverConstructor.cs` | Client 단일 / Server 병행 드라이버 등록 `INetworkStreamDriverConstructor` 구현 |
| `EOSNetworkDiagnostics.cs` | World/Driver/Connection 상태 덤프 정적 유틸 |
| `EOSTransportUtility.cs` (수정) | Poller 미초기화 시 해결 레시피 포함 에러 메시지 |

## API (외부 피처가 참조 가능)

- `EOSAndIpcDriverConstructor` — `Runtime/Transport/EOSAndIpcDriverConstructor.cs`
- `EOSNetworkDiagnostics.DumpState() -> string` — `Runtime/Transport/EOSNetworkDiagnostics.cs`
- `EOSNetworkDiagnostics.Log() -> void` — `Runtime/Transport/EOSNetworkDiagnostics.cs`

사용법:
```csharp
using Tjdtjq5.EOS.Transport;
using Unity.NetCode;

public class MyBootstrap : ClientServerBootstrap
{
    public override bool Initialize(string defaultWorldName)
    {
        AutoConnectPort = 0;
        NetworkStreamReceiveSystem.DriverConstructor = new EOSAndIpcDriverConstructor();
        CreateDefaultClientServerWorlds();
        return true;
    }
}
```

연결 실패 진단:
```csharp
EOSNetworkDiagnostics.Log();
// ClientWorld NetworkStreamConnection 있는데 NetworkId=0 → 핸드셰이크 실패
// → PlayType 확인: ClientServerBootstrap.RequestedPlayType
```

## 주의사항

- **Client 드라이버는 반드시 1개만 등록.** Netcode가 `NetworkStreamDriver.Connect` 시점에 `"Too many NetworkDriver created for the client. Only one NetworkDriver instance should exist"` InvalidOperationException을 throw한다. Server는 병행 등록 가능.
- **MPPM Virtual Player 환경은 자동 처리됨.** `MppmRoleDetector`가 VP를 감지해 Client Driver를 Socket으로 강제. 사용자가 PlayMode Tools에서 설정한 `RequestedPlayType`/`SimulatorEnabled` 값은 변경하지 않는다 (읽기 전용 감지). 새 VP 추가 / 팀원 클론 / CI 환경 어디서나 자동 동작.
- Netcode for Entities 1.10 기준 검증. 내장 IPC 드라이버가 호스트 모드 루프백을 처리.
- `EOSNetworkDiagnostics.DumpState()`는 `EntityQuery`를 사용하므로 메인 스레드에서만 호출.
