import { useEffect, useState } from 'react'
import { LoadingBlock } from '../../shared/Spinner'
import { isPreview } from '../../shared/env'
import { sb } from '../../shared/supabase'
import type { AuditLog } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'
import { useActors } from './actors'
import { ACTION_BADGE, ACTION_ICON, eventLabel, fmtDateTime, timeAgo } from './format'

/**
 * 감사 이벤트 상세 (#26 — Metaplay View Audit Log Event 동형: 201-audit-log-detail.png).
 *
 * 구조: 행위 타이틀 + "By 누가 언제" → Event Data 키-값 → 페이로드 → Raw 접이식.
 * 페이로드는 Metaplay 의 raw 나열 대신 **터미널 diff**(- 이전 / + 이후, 변경 키만)다 —
 * 감사를 읽는 일은 코드 리뷰와 같아서, 무엇이 바뀌었는가만 남기고 나머지는 접는다.
 * URL(#audit_log/<id>)로 직접 접근할 수 있어야 하므로 데이터는 id 로 단건 조회한다.
 */
export function AuditDetailPage({ id }: { id: string }) {
  const { types, tableTypes, navigate } = useAdmin()
  const { emailOf } = useActors()
  // 프리뷰는 조회 자체를 안 하므로 곧장 '없음' 으로 — 로딩에 갇히지 않게 한다.
  const [log, setLog] = useState<AuditLog | null | undefined>(isPreview() ? null : undefined)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isPreview() || !sb) return
    let alive = true
    // 해시로 id 만 바뀌는 재진입 — 이전 이벤트의 잔상을 지우고 로딩부터 다시.
    setLog(undefined)
    setError(null)
    void sb
      .from('admin_audit_log')
      .select<AuditLog[]>('*')
      .eq('id', id)
      .then((r) => {
        if (!alive) return
        if (r.error) setError(r.error.message)
        else setLog(r.data?.[0] ?? null)
      })
    return () => {
      alive = false
    }
  }, [id])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>이벤트를 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }
  if (log === undefined) return <LoadingBlock label="이벤트 불러오는 중" />
  if (log === null) {
    return (
      <div className="empty-state">
        <i className="ti ti-ghost" />
        <h3>없는 이벤트입니다</h3>
        <p>지워졌거나 잘못된 주소입니다. ID: {id}</p>
      </div>
    )
  }

  // 대상 화면으로 가는 길 (Metaplay 'View Target' 동형) — 지금 존재하는 표만 링크한다.
  const asConfig = types.find((t) => t.tableName === log.config_type)
  const asTable = tableTypes.find((t) => t.tableName === log.config_type)

  return (
    <div className="audit-detail p-3">
      <div className="d-flex align-items-center gap-2 mb-1">
        <span className={`badge fs-3 ${ACTION_BADGE[log.action] ?? 'bg-secondary'}`}>
          <i className={`ti ${ACTION_ICON[log.action] ?? 'ti-activity'} me-1`} />
          {log.action}
        </span>
        <code className="text-muted">{eventLabel(log)}</code>
      </div>
      <div className="text-muted mb-3">
        By <b>{emailOf(log.admin_id)}</b> · {timeAgo(log.created_at)} ({fmtDateTime(log.created_at)})
      </div>

      {/* ── Event Data ── */}
      <h4 className="mb-1">Event Data</h4>
      <table className="table table-sm mb-4" style={{ maxWidth: 560 }}>
        <tbody>
          <KV k="대상 타입">
            {asConfig || asTable ? (
              <a
                href="#"
                onClick={(e) => {
                  e.preventDefault()
                  navigate(asConfig ? { kind: 'config', tableName: log.config_type! } : { kind: 'table', tableName: log.config_type! })
                }}
              >
                {log.config_type}
              </a>
            ) : (
              (log.config_type ?? '-')
            )}
          </KV>
          <KV k="대상 ID">{log.row_id ? <code>{log.row_id}</code> : '-'}</KV>
          <KV k="행위자">
            {emailOf(log.admin_id)}
            {log.admin_id && log.admin_id !== 'server' && (
              <code className="text-muted ms-2" style={{ fontSize: '.75em' }}>
                {log.admin_id}
              </code>
            )}
          </KV>
          <KV k="시각">{fmtDateTime(log.created_at)}</KV>
          <KV k="이벤트 ID">
            <code style={{ fontSize: '.8em' }}>{log.id}</code>
          </KV>
        </tbody>
      </table>

      {/* ── Payload ── */}
      <h4 className="mb-1">Payload</h4>
      <DiffView before={log.before_json} after={log.after_json} />

      {/* ── Raw ── */}
      <details className="mt-4">
        <summary className="text-muted" style={{ cursor: 'pointer' }}>
          RAW DATA
        </summary>
        <pre className="audit-raw mt-2">{JSON.stringify(rawOf(log), null, 2)}</pre>
      </details>
    </div>
  )
}

function KV({ k, children }: { k: string; children: React.ReactNode }) {
  return (
    <tr>
      <td className="text-muted" style={{ width: 120 }}>
        {k}
      </td>
      <td>{children}</td>
    </tr>
  )
}

/** Raw 표시용 — before/after 는 파싱해 중첩으로 보여준다(이중 이스케이프 문자열은 못 읽는다). */
function rawOf(log: AuditLog): Record<string, unknown> {
  return {
    ...log,
    before_json: tryParse(log.before_json),
    after_json: tryParse(log.after_json),
  }
}

function tryParse(s: string | null | undefined): unknown {
  if (!s) return null
  try {
    return JSON.parse(s)
  } catch {
    return s
  }
}

/**
 * before→after 터미널 diff. 키 단위로 비교해 바뀐 키만 `- / +` 로 보여주고,
 * 안 바뀐 키는 접는다. viewed 처럼 페이로드가 없는 이벤트는 그 사실을 말한다.
 */
function DiffView({ before, after }: { before?: string | null; after?: string | null }) {
  const b = tryParse(before)
  const a = tryParse(after)

  if (b === null && a === null) {
    return <p className="text-muted">이 이벤트에는 페이로드가 없습니다 (열람 등 데이터 무변경 이벤트).</p>
  }

  // 객체가 아니면(스냅샷 이름 문자열 등) diff 가 아니라 값 그대로가 정직하다.
  if (typeof b !== 'object' || typeof a !== 'object' || Array.isArray(b) || Array.isArray(a)) {
    return (
      <pre className="audit-diff">
        {b !== null && <div className="d-del">- {JSON.stringify(b)}</div>}
        {a !== null && <div className="d-add">+ {JSON.stringify(a)}</div>}
      </pre>
    )
  }

  const bo = (b ?? {}) as Record<string, unknown>
  const ao = (a ?? {}) as Record<string, unknown>
  const keys = [...new Set([...Object.keys(bo), ...Object.keys(ao)])].sort()
  const changed = keys.filter((k) => JSON.stringify(bo[k]) !== JSON.stringify(ao[k]))
  const same = keys.filter((k) => !changed.includes(k))

  return (
    <>
      <pre className="audit-diff">
        {changed.length === 0 && <div className="text-muted">값 변화가 없는 기록입니다 (동일값 저장).</div>}
        {changed.map((k) => (
          <div key={k}>
            {k in bo && (
              <div className="d-del">
                - {k}: {JSON.stringify(bo[k])}
              </div>
            )}
            {k in ao && (
              <div className="d-add">
                + {k}: {JSON.stringify(ao[k])}
              </div>
            )}
          </div>
        ))}
      </pre>
      {same.length > 0 && (
        <details>
          <summary className="text-muted" style={{ cursor: 'pointer' }}>
            변경 없는 필드 {same.length}개
          </summary>
          <pre className="audit-diff mt-2">
            {same.map((k) => (
              <div key={k} className="text-muted">
                {'  '}
                {k}: {JSON.stringify(ao[k] ?? bo[k])}
              </div>
            ))}
          </pre>
        </details>
      )}
    </>
  )
}
