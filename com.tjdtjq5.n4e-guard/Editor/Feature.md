# N4EGuard Editor — Validation Rules

## 상태
wip

## 용도
Netcode for Entities 코딩 규칙 4개를 컴파일 후 자동 검증.
[InitializeOnLoad]로 Domain Reload 시 자동 실행 — 사람 개입 없이 Console에 Warning 출력.

## 의존성
- Unity.Entities
- Unity.NetCode

## 포함 기능

### 구현 방식
- **[InitializeOnLoad] Editor 스크립트** — 컴파일 완료 시 자동 실행
- 프로젝트 .cs 파일을 텍스트 스캔 (Editor/ 폴더 제외)
- 위반 시 `Debug.LogWarning("[N4EGuard] B4: ...")` — Console 클릭으로 해당 파일 점프
- 억제: `// N4EGuard:ignore B4` 주석으로 개별 라인 억제

### B4 — [GhostField] int/byte에 Quantization=0 누락
- `[GhostField]` 어트리뷰트가 붙은 필드가 int/byte/short/enum 타입인데 `Quantization=0` (또는 `Quantization = 0`)이 없으면 경고
- 스캔 방법: `[GhostField` 포함 라인 찾기 → 같은 라인 또는 다음 라인의 필드 타입이 정수 계열인지 확인 → `Quantization` 키워드 유무
- 위험: 정수에 기본 양자화(100) 적용 시 float 변환 과정에서 정밀도 손실. 에러 없이 조용히 값이 틀어짐.
- 근거: ProjectileStats.PierceCount, FieldOrbOwner.PlayerIndex 등에서 발견

### B5 — PredictedSimulationSystemGroup 내 사이드이펙트
- `[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]` 어트리뷰트가 있는 파일에서 아래 패턴 검출 시 경고:
  - `ecb.CreateEntity` / `ECB.CreateEntity`
  - `PoolSystem.Pick` / `PoolSystem.Current.Pick`
  - `AudioSource.Play`
  - `Instantiate(`
- 위험: Prediction rollback 시 해당 코드가 중복 실행 — Entity 누적, VFX/사운드 중복
- 근거: 현재 PlayerMoveSystem만 Prediction 그룹이라 안전하지만, 향후 추가 시 필연적 발생

### B6 — RPC 수신 후 DestroyEntity 누락
- 파일에 `ReceiveRpcCommandRequest` 참조가 있는데 같은 파일에 `DestroyEntity` 호출이 없으면 경고
- 스캔 방법: `ReceiveRpcCommandRequest` grep → 같은 파일에서 `DestroyEntity` 존재 확인
- 위험: RPC Entity가 파괴되지 않으면 다음 프레임에 재처리 → 무한 데미지, 무한 스폰
- 근거: 현재 모든 수신 시스템이 올바르게 Destroy하고 있지만, 새 RPC 추가 시 빠트리기 쉬움

### B7 — WorldSystemFilter 없는 ISystem
- `ISystem`을 구현하는 struct인데 `[WorldSystemFilter]` 어트리뷰트가 없으면 경고
- allowlist: 의도적 전체 World 실행 시스템 목록 (예: `LateWorldRegisterSystem`)을 파일 상단에 관리
- 위험: 필터 없으면 Server+Client 양쪽에서 실행 → 서버 전용 로직이 클라에서 돌거나, 비주얼이 서버에서 낭비
- 근거: EnemyHitFlashSystem, EnemyHpBarSystem에서 실제 발견

## 구조

| 파일 | 설명 |
|------|------|
| `N4EGuard.Editor.asmdef` | Editor-only 어셈블리 (Runtime 의존 없음, 순수 텍스트 스캔) |
| `N4EGuardValidator.cs` | [InitializeOnLoad] 자동 실행 스캐너 — B4/B5/B6/B7 4개 규칙 |

## API (외부 피처가 참조 가능)

외부 API 없음 — 컴파일 후 자동 실행. Console에 Warning 출력만.

억제 방법:
- `// N4EGuard:ignore B4` — 해당 라인의 특정 규칙 억제
- `// N4EGuard:ignore` — 해당 라인의 모든 규칙 억제
- B7 allowlist: `N4EGuardValidator.WorldFilterAllowlist`에 시스템 이름 추가

## 주의사항
- `N4EGuard.Editor.asmdef`로 별도 어셈블리 분리 (빌드에 포함 안 됨)
- 텍스트 스캔이므로 block comment 내부, string literal 내부의 패턴은 false positive 가능
- `//` 주석 라인은 스킵하지만 `/* */` 블록 주석은 미처리
- 매 Domain Reload마다 전체 스캔 (200파일 기준 <1초)
- BatchMode (CI)에서는 자동 비활성화
