import { bridgeAvailable } from './bridge'
import { ops } from './ops'
import { toast } from './toast'

/**
 * Id 상수 자동 재생성 트리거.
 *
 * Id 상수({Name}Ids)가 낡는 시점은 코드가 아니라 **PK 집합이 바뀔 때**다 — 행 추가/삭제/복사,
 * 스냅샷 복원. 그 순간을 정확히 아는 것이 어드민 자신이라, 여기서 브리지에 알린다.
 * 수동 버튼은 없다(이 경로가 유일).
 *
 * 정책(환경별 토글)은 브리지가 판정한다 — 꺼져 있으면 skipped 로 답하고 아무 일도 없다.
 * 연타(행 여러 개 추가)는 디바운스로 묶는다. 생성기 쪽에 내용 비교 가드가 있어
 * PK 집합이 실제로 안 바뀌었으면(값만 수정 등) Unity 재컴파일도 일어나지 않는다.
 */

let timer: number | undefined

export function queueIdConstants(immediate = false) {
  if (!bridgeAvailable()) return
  if (timer !== undefined) window.clearTimeout(timer)
  if (immediate) {
    // 스냅샷 복원처럼 곧 페이지가 리로드되는 자리 — 디바운스를 기다리면 타이머째 죽는다.
    // 요청만 떠나면 처리는 Unity 쪽이라 리로드에 잘리지 않는다.
    timer = undefined
    void fire()
    return
  }
  timer = window.setTimeout(() => {
    timer = undefined
    void fire()
  }, 2500)
}

async function fire() {
  try {
    const r = await ops.idConstants()
    if (r.skipped) return
    // 성공은 조용히 — 잦은 트리거라 토스트가 소음이 된다. 실제 갱신은 Unity Console 에 남는다.
    if (!r.ok) toast(`Id 상수 생성 오류 — ${r.errors[0] ?? '원인 미상'}`, 'error')
  } catch {
    /* Unity 가 바쁘거나 닫힘 — 다음 편집이 다시 시도한다 */
  }
}
