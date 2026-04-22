# EOS Transport — Send/Receive

## 상태
wip

## 용도
ScheduleSend/ScheduleReceive에서 EOS P2P API 실제 호출, 하이브리드 모델 (MonoBehaviour + NativeQueue + Job)

## 의존성
- ./Feature.md — Skeleton (EOSP2PNetworkInterface 구조체, EOSContext)
- com.playeveryware.eos — EOS P2P SendPacket/ReceivePacket, EOSManager

## 포함 기능
- EOSTransportPoller (MonoBehaviour) — Update()에서 수신 polling, LateUpdate()에서 송신 처리
- TransportPacket (struct) — NativeQueue에 담을 unmanaged 패킷 구조체
- DrainReceiveJob — Poller 수신 NativeQueue → ReceiveQueue 이동
- FillSendJob — SendQueue → Poller 송신 NativeQueue 이동
- Endpoint ↔ ProductUserId 매핑 — Poller에서 가상 loopback endpoint 자동 할당
- Debug 로깅 — #if EOS_DEBUG 조건부 패킷 로그

## 구조
| 파일 | 설명 |
|------|------|
| EOSTransportPoller.cs | 메인 스레드 EOS polling MonoBehaviour |
| TransportPacket.cs | NativeQueue용 unmanaged 패킷 구조체 |
| EOSP2PNetworkInterface.cs | DrainReceiveJob + FillSendJob 추가 (수정) |
| EOSContext.cs | Poller 참조 필드 추가 (수정) |

## API (외부 피처가 참조 가능)
- 새 public API 없음 — Skeleton의 EOSP2PNetworkInterface에 기능 채움

## 주의사항
- EOS P2P는 메인 스레드에서만 호출 가능 → MonoBehaviour에서 처리
- Reliability: UnreliableUnordered 고정 (Unity Transport가 reliability 담당)
- 수신 상한: 프레임당 최대 256개
- Poller는 Initialize에서 자동 생성, Dispose에서 파괴
- EOS P2P MTU: 1170 bytes
- **Play 모드 종료 시 notification 제거 타이밍**: `OnDestroy`에서 `P2PInterface.RemoveNotifyPeerConnectionRequest`를 호출하면 EOSManager가 SDK를 먼저 shutdown한 뒤라 네이티브 크래시. `EditorApplication.playModeStateChanged(ExitingPlayMode)` + `OnApplicationQuit`에서 먼저 제거하고, `OnDestroy`는 `EOSManager.GetEOSPlatformInterface() != null` 가드된 fallback으로만 사용.
- **NativeQueue Dispose 전 Job 완료 보장**: `LastSendJobHandle.Complete()`를 `OnDestroy` 맨 앞에서 호출해 `FillSendJob`이 NativeQueue를 참조 중인 상태에서 Dispose되지 않도록 함.
