import { useMemo, useState } from 'react'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import type { AuditLog } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'
import { useActors } from './actors'
import { ACTION_BADGE, ACTION_ICON, eventLabel, fmtDateTime, timeAgo } from './format'
import { EMPTY_FILTER, useAuditLogs, type AuditFilter } from './useAuditLogs'

/**
 * 감사 이벤트 목록 (#25·#26 — Metaplay View Audit Logs 동형: 200-audit-logs.png).
 *
 * 구조도 같다: 설명 한 줄 → 검색 블록 → Latest Audit Log Events 표 → [더 불러오기].
 * 필터는 3종(대상 타입·행위자·기간)이고 서버(PostgREST)가 거른다. 행을 누르면 상세로
 * 간다 — before/after 버튼을 행마다 두던 옛 화면과 달리, 페이로드는 상세의 일이다.
 */
export function AuditPage({ presetType }: { presetType?: string }) {
  const { types, tableTypes, navigate } = useAdmin()
  const { actors, emailOf } = useActors()
  const [filter, setFilter] = useState<AuditFilter>({ ...EMPTY_FILTER, configType: presetType ?? '' })
  const { logs, error, hasMore, loading, loadMore } = useAuditLogs(filter)

  // 대상 타입 후보 — 프로젝트의 config/table + 패키지 고정 표. 로그에만 있는 타입(폐기된
  // 표 등)도 고를 수 있어야 하므로 현재 페이지에서 발견된 것을 합친다.
  const typeOptions = useMemo(() => {
    const s = new Set<string>()
    types.forEach((t) => s.add(t.tableName))
    tableTypes.forEach((t) => s.add(t.tableName))
    ;['admin_user_role', 'suparun_env', 'suparun_secret', 'suparun_snapshot', 'server_log'].forEach((t) => s.add(t))
    ;(logs ?? []).forEach((l) => l.config_type && s.add(l.config_type))
    if (filter.configType) s.add(filter.configType)
    return [...s].sort()
  }, [types, tableTypes, logs, filter.configType])

  const set = (patch: Partial<AuditFilter>) => setFilter((f) => ({ ...f, ...patch }))

  return (
    <div className="audit-page">
      {/* ── 검색 (Metaplay Search 블록 동형 — 우리 축은 타입·행위자·기간 3종).
          머리 산문은 없다 — 검색 폼과 목록이 곧 설명이다. */}
      <div className="audit-search m-3">
        <div className="row g-2">
          <div className="col-sm-3">
            <label className="form-label mb-1">대상 타입</label>
            <select
              className="form-select form-select-sm"
              value={filter.configType}
              onChange={(e) => set({ configType: e.target.value })}
            >
              <option value="">전체</option>
              {typeOptions.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </div>
          <div className="col-sm-3">
            <label className="form-label mb-1">행위자</label>
            <select
              className="form-select form-select-sm"
              value={filter.adminId}
              onChange={(e) => set({ adminId: e.target.value })}
            >
              <option value="">전체</option>
              {actors.map((a) => (
                <option key={a.user_id} value={a.user_id}>
                  {a.email ?? a.user_id}
                </option>
              ))}
              <option value="server">server (PAT·서버 경유)</option>
            </select>
          </div>
          <div className="col-sm-3">
            <label className="form-label mb-1">시작일</label>
            <input
              type="date"
              className="form-control form-control-sm"
              value={filter.from}
              onChange={(e) => set({ from: e.target.value })}
            />
          </div>
          <div className="col-sm-3">
            <label className="form-label mb-1">종료일</label>
            <input
              type="date"
              className="form-control form-control-sm"
              value={filter.to}
              onChange={(e) => set({ to: e.target.value })}
            />
          </div>
        </div>
      </div>

      {error && (
        <div className="empty-state">
          <i className="ti ti-alert-triangle" />
          <h3>감사 이력을 불러오지 못했습니다</h3>
          <p>{error}</p>
        </div>
      )}

      {!error && !logs && <LoadingBlock label="감사 이력 불러오는 중" />}

      {!error && logs && (
        <>
          <table className="table table-vcenter card-table table-striped table-hover">
            <thead>
              <tr>
                <th>Event</th>
                <th>행위</th>
                <th>행위자</th>
                <th>시각</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((log) => (
                <AuditRow key={log.id} log={log} email={emailOf(log.admin_id)} onOpen={() => navigate({ kind: 'auditDetail', id: log.id })} />
              ))}
              {logs.length === 0 && (
                <tr>
                  <td colSpan={4} className="text-center text-muted py-4">
                    <i className="ti ti-history me-1" />
                    조건에 맞는 감사 이벤트가 없습니다.
                  </td>
                </tr>
              )}
            </tbody>
          </table>

          {hasMore && (
            <div className="text-center my-3">
              <button className="btn" disabled={loading} onClick={() => void loadMore()}>
                {loading ? (
                  <>
                    <Spinner size={12} /> [LOADING...]
                  </>
                ) : (
                  '[LOAD MORE]'
                )}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  )
}

function AuditRow({ log, email, onOpen }: { log: AuditLog; email: string; onOpen: () => void }) {
  return (
    <tr style={{ cursor: 'pointer' }} onClick={onOpen}>
      <td>
        <code>{eventLabel(log)}</code>
      </td>
      <td>
        <span className={`badge ${ACTION_BADGE[log.action] ?? 'bg-secondary'}`}>
          <i className={`ti ${ACTION_ICON[log.action] ?? 'ti-activity'} me-1`} />
          {log.action}
        </span>
      </td>
      <td className="text-muted">{email}</td>
      <td className="text-muted" title={fmtDateTime(log.created_at)}>
        {timeAgo(log.created_at)}
      </td>
    </tr>
  )
}
