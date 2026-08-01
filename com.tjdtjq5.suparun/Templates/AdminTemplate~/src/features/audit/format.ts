import type { AuditLog } from '../../shared/types'

/**
 * 감사 화면 공통 표시 규칙 — 목록(#25)·상세(#26)·카드(#28)가 같은 언어를 쓰게 한다.
 * 행위(action)의 색과 아이콘: **변경은 유채색, 열람(viewed)은 무채색** — 색 자체가
 * "데이터가 바뀌었는가" 를 말한다 (#27).
 */

export const ACTION_BADGE: Record<string, string> = {
  insert: 'bg-green',
  update: 'bg-blue',
  delete: 'bg-red',
  policy: 'bg-purple',
  restore: 'bg-orange',
  viewed: 'bg-secondary',
}

export const ACTION_ICON: Record<string, string> = {
  insert: 'ti-plus',
  update: 'ti-pencil',
  delete: 'ti-trash',
  policy: 'ti-shield',
  restore: 'ti-restore',
  viewed: 'ti-eye',
}

/** '방금' → 'n분 전' → 'n시간 전' → 'n일 전'. 그 이상은 날짜가 정직하다. */
export function timeAgo(ms: number): string {
  const d = Date.now() - ms
  if (d < 60_000) return '방금'
  if (d < 3_600_000) return `${Math.floor(d / 60_000)}분 전`
  if (d < 86_400_000) return `${Math.floor(d / 3_600_000)}시간 전`
  if (d < 30 * 86_400_000) return `${Math.floor(d / 86_400_000)}일 전`
  return new Date(ms).toLocaleDateString('ko-KR')
}

export function fmtDateTime(ms: number): string {
  return new Date(ms).toLocaleString('ko-KR')
}

/** 목록·카드의 Event 표기 — `대상타입:행ID`. 행 단위가 아니면 타입만. */
export function eventLabel(log: AuditLog): string {
  return log.row_id ? `${log.config_type ?? '?'}:${log.row_id}` : (log.config_type ?? '?')
}
