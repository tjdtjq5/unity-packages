# EOS Transport — Connection Lifecycle

## 상태
wip

## 용도
Listen (연결 수락), 클라이언트 피어 등록, 연결 해제 감지 + 매핑 정리

## 의존성
- ./Feature.md — Skeleton (EOSP2PNetworkInterface)
- ./SendReceive.Feature.md — EOSTransportPoller, Send/Receive
- ./NetcodeIntegration.Feature.md — `EOSAndIpcDriverConstructor` (Netcode 통합 계층)
- com.playeveryware.eos — EOS P2P Connection notification API

## 포함 기능
- Listen 연결 수락 — AddNotifyPeerConnectionRequest + AcceptConnection 콜백
- 연결 종료 감지 — AddNotifyPeerConnectionClosed + 매핑 정리 콜백
- EOSTransportUtility — RegisterRemotePeer() public static API (클라이언트용)
- Notification 해제 — OnDestroy에서 Remove

## 구조
| 파일 | 설명 |
|------|------|
| EOSTransportUtility.cs | public static API — RegisterRemotePeer() |
| EOSTransportPoller.cs | 수정 — Active 프로퍼티, StartListening(), 연결 콜백 |
| EOSP2PNetworkInterface.cs | 수정 — Listen()에서 Poller.StartListening() 호출 |

## API (외부 피처가 참조 가능)
- `EOSTransportUtility.RegisterRemotePeer(ProductUserId)` → `NetworkEndpoint` (`Runtime/Transport/EOSTransportUtility.cs`)

사용법 (클라이언트):
```csharp
var serverEndpoint = EOSTransportUtility.RegisterRemotePeer(serverProductUserId);
driver.Connect(serverEndpoint);
```

## 주의사항
- INetworkInterface는 연결 상태를 관리하지 않음 — NetworkDriver가 핸드셰이크로 자체 관리
- EOSTransportPoller.Active는 Bridge 레지스트리 패턴 (singleton-static-mutable 예외)
- AddNotifyPeerConnectionRequest 옵션에 ref new struct 패턴 사용 — EOS SDK 요구사항
- Notification 해제를 반드시 OnDestroy에서 수행 (리소스 릭 방지)
