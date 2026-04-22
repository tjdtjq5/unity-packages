# EOS Transport — Skeleton

## 상태
wip

## 용도
INetworkInterface 빈 구현, EOSManager 브릿지, WrapToUnmanaged 패턴 확립

## 의존성
- com.playeveryware.eos — EOSManager (초기화/로그인), P2P API
- com.unity.transport — INetworkInterface, NetworkDriver, WrapToUnmanaged

## 포함 기능
- EOSP2PNetworkInterface (struct) — INetworkInterface 6개 메서드 구현 (Send/Receive는 stub)
- EOSContext + EOSContextStore — managed EOS 상태를 인덱스 기반으로 관리하는 브릿지
- EOSManager 가드 — 초���화/로그인 안 됐으면 LogError + graceful 실패
- WrapToUnmanaged 패턴 — managed struct를 NetworkDriver에 주입 가능

## 구조
| 파일 | 설명 |
|------|------|
| EOSP2PNetworkInterface.cs | INetworkInterface 구현 struct |
| EOSContext.cs | managed 컨텍스트 클래스 + static 저장소 |
| ../Tjdtjq5.EOS.Runtime.asmdef | 어셈블리 정의 |

## API (외부 피처가 참조 가능)
- `EOSP2PNetworkInterface` — NetworkDriver에 주입하는 Transport 구현체 (`Runtime/Transport/EOSP2PNetworkInterface.cs`)

사용법:
```csharp
var eos = new EOSP2PNetworkInterface();
var driver = NetworkDriver.Create(eos.WrapToUnmanaged(), settings);
```

## 주의사항
- WebSocketNetworkInterface + IPCNetworkInterface 소스를 참고 레퍼런스로 사용
- ScheduleSend/ScheduleReceive는 stub (Pillar 2에서 구현)
- Listen의 connection notification은 stub (Pillar 3에서 구현)
- PlayEveryWare EOSManager가 초기화/로그인을 담당, 이 모듈은 Transport만
- 네임스페이스: Tjdtjq5.EOS.Transport
- #if EOS_DEBUG로 조건부 로깅 지원
