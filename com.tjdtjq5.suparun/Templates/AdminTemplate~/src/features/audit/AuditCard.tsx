import { useEffect, useState } from 'react'
import { isPreview } from '../../shared/env'
import { sb } from '../../shared/supabase'
import type { AuditLog } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'
import { useActors } from './actors'
import { ACTION_BADGE, ACTION_ICON, timeAgo } from './format'

/**
 * 공용 감사 카드 (#28 — Metaplay 'Latest Audit Log Events' 카드 동형, 투어 §3-5).
 *
 * 엔티티 상세/관리 화면에 박아 "이 대상을 누가 언제 만졌는가" 를 그 자리에서 보여준다.
 * 표 화면의 세로 공간을 지키기 위해 접이식이고, 접힌 줄 자체가 최신 1건을 요약한다 —
 * 펼치지 않아도 마지막 손댄 사람이 보인다.
 */
export function AuditCard({
  configType,
  rowId,
  limit = 5,
}: {
  configType: string
  /** 주면 행 단위, 안 주면 대상 타입 전체. */
  rowId?: string
  limit?: number
}) {
  const { navigate } = useAdmin()
  const { emailOf } = useActors()
  const [logs, setLogs] = useState<AuditLog[]>([])

  useEffect(() => {
    if (isPreview() || !sb) return
    let alive = true
    let q = sb.from('admin_audit_log').select<AuditLog[]>('*').eq('config_type', configType)
    if (rowId) q = q.eq('row_id', rowId)
    void q
      .order('created_at', { ascending: false })
      .limit(limit)
      .then((r) => {
        if (alive && !r.error) setLogs(r.data ?? [])
      })
    return () => {
      alive = false
    }
  }, [configType, rowId, limit])

  const latest = logs[0]

  return (
    <details className="audit-mini m-2 mb-0">
      <summary>
        <i className="ti ti-history me-1" />
        감사
        {latest ? (
          <span className="text-muted ms-2">
            최근: {latest.action} {latest.row_id ?? ''} · {emailOf(latest.admin_id)} ·{' '}
            {timeAgo(latest.created_at)}
          </span>
        ) : (
          <span className="text-muted ms-2">기록 없음</span>
        )}
      </summary>
      <ul className="audit-mini-list">
        {logs.map((log) => (
          <li key={log.id}>
            <a
              href="#"
              onClick={(e) => {
                e.preventDefault()
                navigate({ kind: 'auditDetail', id: log.id })
              }}
            >
              <span className={`badge badge-sm ${ACTION_BADGE[log.action] ?? 'bg-secondary'}`}>
                <i className={`ti ${ACTION_ICON[log.action] ?? 'ti-activity'}`} />
              </span>{' '}
              {log.action} {log.row_id && <code>{log.row_id}</code>} ·{' '}
              <span className="text-muted">
                {emailOf(log.admin_id)} · {timeAgo(log.created_at)}
              </span>
            </a>
          </li>
        ))}
        <li>
          <a
            href="#"
            onClick={(e) => {
              e.preventDefault()
              navigate({ kind: 'audit', presetType: configType })
            }}
          >
            전체 보기 <i className="ti ti-arrow-right" />
          </a>
        </li>
      </ul>
    </details>
  )
}
