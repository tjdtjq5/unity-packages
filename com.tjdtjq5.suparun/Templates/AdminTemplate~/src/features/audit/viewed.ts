import { isPreview } from '../../shared/env'
import { sb } from '../../shared/supabase'

/**
 * 열람 자기기록 (#27, ADR-0008: 변경=트리거, 열람=자기기록).
 *
 * SELECT 에는 트리거가 없어 민감 화면은 진입 시 스스로 기록한다 — RPC
 * `suparun_audit_viewed`(action='viewed' 만 허용하는 좁은 문)를 부른다.
 *
 * **중복 정책: 페이지 로드당 화면(대상)별 1회.** 탭을 오갈 때마다 쌓이면 노이즈가
 * 신호(진짜 열람 사실)를 덮고, 새로고침(새 세션)은 다시 기록되므로 "언제 열람했는가"
 * 는 충분히 남는다. 실패는 조용히 넘긴다 — 열람 기록이 화면 사용을 막으면 안 된다.
 */

const recorded = new Set<string>()

export function recordViewed(configType: string, rowId?: string): void {
  const key = `${configType}|${rowId ?? ''}`
  if (recorded.has(key) || isPreview() || !sb) return
  recorded.add(key)
  void sb
    .rpc('suparun_audit_viewed', { p_config_type: configType, p_row_id: rowId ?? null })
    .then((r) => {
      if (r.error) console.warn('열람 기록 실패:', r.error.message)
    })
}
