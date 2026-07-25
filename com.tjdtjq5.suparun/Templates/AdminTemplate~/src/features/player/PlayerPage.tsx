import { useCallback, useEffect, useRef, useState } from 'react'
import { tableApi } from '../../shared/api'
import { toast } from '../../shared/toast'
import { enableColResize } from '../../shared/colResize'
import { useAdmin } from '../shell/AdminContext'
import { castValue } from '../../shared/castValue'
import type { PlayerData, TableField, TableRow } from '../../shared/types'

const NUMERIC = ['int', 'long', 'number']

/** user_id 컬럼은 카드 안에서 중복이라 숨긴다 (바닐라와 동일). */
function isUserIdField(f: TableField): boolean {
  return f.name === 'userId' || f.name === 'user_id'
}

/**
 * 플레이어 관리. 바닐라 showPlayerSearch/doPlayerSearch/renderPlayerCards/startPlayerEdit 을 옮긴 것이다.
 * 크로스 검색에서 user_id 를 클릭해 진입할 때는 initialUserId 로 들어온다.
 */
export function PlayerPage({ initialUserId }: { initialUserId?: string }) {
  const { setPageSubtitle } = useAdmin()
  const [uid, setUid] = useState(initialUserId ?? '')
  const [data, setData] = useState<PlayerData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const search = useCallback(async (userId: string) => {
    const trimmed = userId.trim()
    if (!trimmed) {
      toast('유저 ID를 입력하세요', 'error')
      return
    }
    setLoading(true)
    setError(null)
    try {
      // 바닐라와 동일한 경로 트릭 — /admin/api/table/../player/{id} → /admin/api/player/{id}
      const res = await tableApi<PlayerData>(`/../player/${encodeURIComponent(trimmed)}`)
      setData(res)
      setPageSubtitle(trimmed)
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      setError(msg)
      setData(null)
      toast('조회 실패: ' + msg, 'error')
    } finally {
      setLoading(false)
    }
  }, [])

  // 크로스 검색에서 넘어온 경우 자동 조회
  useEffect(() => {
    if (initialUserId) void search(initialUserId)
  }, [initialUserId, search])

  return (
    <div className="p-4">
      <div className="input-icon mb-3" style={{ maxWidth: 400 }}>
        <span className="input-icon-addon">
          <i className="ti ti-user-search" />
        </span>
        <input
          type="text"
          className="form-control"
          placeholder="유저 ID 입력"
          value={uid}
          onChange={(e) => setUid(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void search(uid)
          }}
        />
      </div>
      <button className="btn btn-primary" onClick={() => void search(uid)}>
        <i className="ti ti-search me-1" />
        검색
      </button>

      <div className="mt-3">
        {loading && (
          <div className="loading-spinner">
            <div className="spinner-border text-primary" role="status" />
          </div>
        )}
        {!loading && error && (
          <div className="empty-state">
            <i className="ti ti-user-off" />
            <h3>조회 실패</h3>
            <p>{error}</p>
          </div>
        )}
        {!loading && !error && !data && (
          <div className="empty-state">
            <i className="ti ti-user" />
            <h3>유저 ID를 입력하고 검색하세요</h3>
          </div>
        )}
        {!loading && data && <PlayerCards data={data} />}
      </div>
    </div>
  )
}

function PlayerCards({ data }: { data: PlayerData }) {
  const tableTypes = useAdmin().tableTypes
  const entries = Object.entries(data.tables ?? {})

  if (entries.length === 0) {
    return (
      <div className="empty-state">
        <i className="ti ti-inbox" />
        <h3>데이터 없음</h3>
      </div>
    )
  }

  return (
    <>
      {entries.map(([tableName, rows], i) => {
        const arr = Array.isArray(rows) ? rows : []
        const tt = tableTypes.find((t) => t.tableName === tableName)
        const visible = (tt?.fields ?? []).filter((f) => !isUserIdField(f))
        return (
          <PlayerTableCard
            key={tableName}
            index={i}
            title={tt?.name ?? tableName}
            tableName={tableName}
            fields={visible}
            allFields={tt?.fields ?? []}
            rows={arr}
          />
        )
      })}
    </>
  )
}

function PlayerTableCard({
  index,
  title,
  tableName,
  fields,
  allFields,
  rows,
}: {
  index: number
  title: string
  tableName: string
  fields: TableField[]
  allFields: TableField[]
  rows: TableRow[]
}) {
  const hostRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (rows.length === 0 || !hostRef.current) return
    enableColResize(hostRef.current, 'player_' + index, { fields, data: rows })
  }, [index, fields, rows])

  const pkName = allFields.find((f) => f.isPrimaryKey)?.name

  return (
    <div className="card card-sm shadow-sm mb-3 page-fade">
      <div className="card-header">
        <h4 className="card-title">
          <i className="ti ti-table me-2" />
          {title}
        </h4>
        <span className="badge bg-blue-lt ms-auto">{rows.length}건</span>
      </div>
      {rows.length === 0 ? (
        <div className="card-body text-muted text-center py-3">데이터 없음</div>
      ) : (
        <div className="table-responsive" ref={hostRef}>
          <table className="table table-vcenter card-table table-striped">
            <thead>
              <tr>
                {fields.map((f) => (
                  <th key={f.name}>
                    {f.isPrimaryKey && <i className="ti ti-key text-yellow me-1" />}
                    {f.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((row, ri) => {
                const rowId = String(row.id ?? (pkName ? row[pkName] : '') ?? '')
                return (
                  <tr key={rowId || ri}>
                    {fields.map((f) => (
                      <PlayerCell
                        key={f.name}
                        tableName={tableName}
                        row={row}
                        field={f}
                        rowId={rowId}
                      />
                    ))}
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** 인라인 편집 셀. 바닐라 startPlayerEdit() 을 옮긴 것이다. */
function PlayerCell({
  tableName,
  row,
  field,
  rowId,
}: {
  tableName: string
  row: TableRow
  field: TableField
  rowId: string
}) {
  const [editing, setEditing] = useState(false)
  const [saved, setSaved] = useState(false)
  const [draft, setDraft] = useState('')
  const shown = String(row[field.name] ?? '')

  if (field.isPrimaryKey) {
    return (
      <td>
        <code className="text-muted">{shown}</code>
      </td>
    )
  }

  if (field.type === 'bool') {
    return (
      <td>
        <span className={`badge ${row[field.name] ? 'bg-green' : 'bg-red'}`}>
          {row[field.name] ? 'true' : 'false'}
        </span>
      </td>
    )
  }

  async function commit() {
    setEditing(false)
    const next = castValue(draft, field.type)
    if (next === row[field.name]) return
    // 바닐라와 동일 — 행 전체를 PUT 한다
    row[field.name] = next
    try {
      await tableApi(`/${tableName}/${encodeURIComponent(rowId)}`, 'PUT', row)
      setSaved(true)
      setTimeout(() => setSaved(false), 800)
      toast('저장됨', 'success')
    } catch (e) {
      toast('저장 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    }
  }

  return (
    <td>
      {editing ? (
        <input
          className="cell-input"
          type={NUMERIC.includes(field.type) ? 'number' : 'text'}
          value={draft}
          autoFocus
          onChange={(e) => setDraft(e.target.value)}
          onBlur={() => void commit()}
          onKeyDown={(e) => {
            if (e.key === 'Enter') e.currentTarget.blur()
            if (e.key === 'Escape') setEditing(false)
          }}
        />
      ) : (
        <span
          className={`cell-edit${saved ? ' cell-saved' : ''}`}
          onClick={() => {
            setDraft(shown)
            setEditing(true)
          }}
        >
          {shown}
        </span>
      )}
    </td>
  )
}
