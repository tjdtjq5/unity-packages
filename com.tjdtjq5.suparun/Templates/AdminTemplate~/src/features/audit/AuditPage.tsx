import { useEffect, useRef } from 'react'
import { enableColResize } from '../../shared/colResize'
import { LoadingBlock } from '../../shared/Spinner'
import type { AuditLog } from '../../shared/types'
import { useAuditLogs } from './useAuditLogs'

// 바닐라 showAuditLog() 의 badges/icons 맵을 그대로 옮긴 것.
const BADGE: Record<string, string> = {
  create: 'bg-green',
  update: 'bg-blue',
  delete: 'bg-red',
  batch: 'bg-purple',
  import: 'bg-orange',
  admin_add: 'bg-cyan',
  admin_remove: 'bg-pink',
  role_change: 'bg-teal',
}

const ICON: Record<string, string> = {
  create: 'ti-plus',
  update: 'ti-pencil',
  delete: 'ti-trash',
  batch: 'ti-stack-2',
  import: 'ti-upload',
  admin_add: 'ti-user-plus',
  admin_remove: 'ti-user-minus',
  role_change: 'ti-shield',
}

/** 바닐라 showAuditJson() 과 동일하게 새 창에 JSON 을 띄운다. 파싱 실패 시 원문 그대로. */
function openJsonWindow(json: string, title: string): void {
  const w = window.open('', '_blank', 'width=600,height=400')
  if (!w) return
  w.document.title = title
  try {
    const el = w.document.createElement('pre')
    el.textContent = JSON.stringify(JSON.parse(json), null, 2)
    w.document.body.appendChild(el)
  } catch {
    w.document.body.textContent = json
  }
}

/**
 * 변경 이력 화면. 바닐라 showAuditLog() 의 콘텐츠 부분을 옮긴 것이다.
 * 껍데기(page-title·사이드바·hideToolbar·setViewHash)는 바닐라가 계속 담당한다.
 */
export function AuditPage() {
  const { logs, error } = useAuditLogs()
  const hostRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!logs || !hostRef.current) return
    enableColResize(hostRef.current, 'audit_log', {
      fields: [
        { name: 'created_at', type: 'string' },
        { name: 'action', type: 'string', isEnum: true },
        { name: 'config_type', type: 'string' },
        { name: 'row_id', type: 'string' },
        { name: 'admin_id', type: 'string' },
        null,
      ],
      data: logs,
    })
  }, [logs])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>변경 이력을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!logs) {
    return (
      <LoadingBlock label="변경 이력 불러오는 중" />
    )
  }

  return (
    <div ref={hostRef}>
      <table className="table table-vcenter card-table table-striped">
        <thead>
          <tr>
            <th>시간</th>
            <th>작업</th>
            <th>대상</th>
            <th>ID</th>
            <th>관리자</th>
            <th>상세</th>
          </tr>
        </thead>
        <tbody>
          {logs.map((log, i) => (
            <AuditRow key={`${log.created_at}_${i}`} log={log} />
          ))}
          {logs.length === 0 && (
            <tr>
              <td colSpan={6} className="text-center text-muted py-4">
                <i className="ti ti-history me-1" />
                변경 이력이 없습니다.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}

function AuditRow({ log }: { log: AuditLog }) {
  return (
    <tr>
      <td className="text-muted">{new Date(log.created_at).toLocaleString('ko-KR')}</td>
      <td>
        <span className={`badge ${BADGE[log.action] ?? 'bg-secondary'}`}>
          <i className={`ti ${ICON[log.action] ?? 'ti-activity'} me-1`} />
          {log.action}
        </span>
      </td>
      <td>{log.config_type ?? ''}</td>
      <td>
        <code>{log.row_id || '-'}</code>
      </td>
      <td className="text-muted" style={{ maxWidth: 120, overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {log.admin_id ?? ''}
      </td>
      <td>
        {log.before_json && (
          <button
            className="btn btn-ghost-secondary btn-sm"
            onClick={() => openJsonWindow(log.before_json!, 'before')}
          >
            <i className="ti ti-arrow-back-up me-1" />
            이전
          </button>
        )}
        {log.after_json && (
          <button
            className="btn btn-ghost-primary btn-sm"
            onClick={() => openJsonWindow(log.after_json!, 'after')}
          >
            <i className="ti ti-arrow-forward-up me-1" />
            이후
          </button>
        )}
      </td>
    </tr>
  )
}
