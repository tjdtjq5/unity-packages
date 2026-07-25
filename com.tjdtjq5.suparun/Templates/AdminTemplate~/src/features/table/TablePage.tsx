import { useCallback, useEffect, useRef, useState } from 'react'
import { tableApi } from '../../shared/api'
import { toast } from '../../shared/toast'
import { enableColResize } from '../../shared/colResize'
import type {
  DistBucket,
  FilterOp,
  TableData,
  TableFilter,
  TableStats,
  TableType,
} from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'
import { DistChart } from './DistChart'

/** 바닐라 TABLE_PAGE_SIZE 와 같은 값. */
const PAGE_SIZE = 50
const OPS: FilterOp[] = ['=', '>', '>=', '<', '<=', 'like']
const OP_SUFFIX: Record<string, string> = {
  '>': 'gt',
  '>=': 'gte',
  '<': 'lt',
  '<=': 'lte',
  like: 'like',
}
const NUMERIC = ['int', 'long', 'number']

/** 바닐라의 필터 쿼리스트링 빌드 규칙과 동일. `=` 는 접미사 없이, 나머지는 `field.gt` 형태. */
function buildParams(filters: TableFilter[]): URLSearchParams {
  const p = new URLSearchParams()
  for (const f of filters) {
    const key = f.op === '=' ? f.field : `${f.field}.${OP_SUFFIX[f.op] ?? 'eq'}`
    p.set(key, f.value)
  }
  return p
}

/**
 * Table 조회 화면. 바닐라 selectTableType/loadTableData/renderTableView/loadStats 를 옮긴 것이다.
 * 껍데기(page-title·사이드바 active·updateSupabaseLink·hideToolbar)는 바닐라가 담당한다.
 */
export function TablePage({ tableType }: { tableType: TableType }) {
  const { setPageSubtitle } = useAdmin()
  const [filters, setFilters] = useState<TableFilter[]>([])
  const [page, setPage] = useState(0)
  const [data, setData] = useState<TableData | null>(null)
  const hostRef = useRef<HTMLDivElement>(null)

  // 필터 입력 폼 (바닐라는 DOM에서 값을 읽었지만 여기서는 상태로 든다)
  const numericFields = tableType.fields.filter((f) => NUMERIC.includes(f.type))
  const [draftField, setDraftField] = useState(tableType.fields[0]?.name ?? '')
  const [draftOp, setDraftOp] = useState<FilterOp>('=')
  const [draftValue, setDraftValue] = useState('')

  const load = useCallback(async () => {
    setData(null)
    const params = buildParams(filters)
    params.set('limit', String(PAGE_SIZE))
    params.set('offset', String(page * PAGE_SIZE))
    try {
      const res = await tableApi<TableData>(`/${tableType.tableName}?${params}`)
      setData(res)
      setPageSubtitle(`${res.total}건`)
    } catch (e) {
      toast('조회 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
      setData({ rows: [], total: 0 })
    }
  }, [tableType.tableName, filters, page])

  useEffect(() => {
    void load()
  }, [load])

  const rows = data?.rows ?? []

  useEffect(() => {
    if (!data || !hostRef.current) return
    enableColResize(hostRef.current, 'table_' + tableType.tableName, {
      fields: tableType.fields,
      data: rows,
    })
  }, [data, rows, tableType])

  function addFilter() {
    if (!draftValue) {
      toast('값을 입력하세요', 'error')
      return
    }
    setFilters((prev) => [...prev, { field: draftField, op: draftOp, value: draftValue }])
    setDraftValue('')
    setPage(0)
  }

  function removeFilter(i: number) {
    setFilters((prev) => prev.filter((_, idx) => idx !== i))
    setPage(0)
  }

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 0

  return (
    <div ref={hostRef}>
      {/* ── 필터 ── */}
      <div className="p-3 border-bottom bg-light">
        <div className="d-flex flex-wrap gap-2 mb-2">
          {filters.map((f, i) => (
            <span key={`${f.field}${f.op}${f.value}${i}`} className="badge bg-blue-lt p-2">
              {f.field} {f.op} {f.value}{' '}
              <a
                href="#"
                className="ms-1 text-danger"
                onClick={(e) => {
                  e.preventDefault()
                  removeFilter(i)
                }}
              >
                <i className="ti ti-x" />
              </a>
            </span>
          ))}
        </div>
        <div className="row g-2 align-items-end">
          <div className="col-auto">
            <select
              className="form-select form-select-sm"
              value={draftField}
              onChange={(e) => setDraftField(e.target.value)}
            >
              {tableType.fields.map((f) => (
                <option key={f.name} value={f.name}>
                  {f.name}
                </option>
              ))}
            </select>
          </div>
          <div className="col-auto">
            <select
              className="form-select form-select-sm"
              value={draftOp}
              onChange={(e) => setDraftOp(e.target.value as FilterOp)}
            >
              {OPS.map((op) => (
                <option key={op} value={op}>
                  {op}
                </option>
              ))}
            </select>
          </div>
          <div className="col-auto">
            <input
              type="text"
              className="form-control form-control-sm"
              placeholder="값"
              style={{ width: 150 }}
              value={draftValue}
              onChange={(e) => setDraftValue(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') addFilter()
              }}
            />
          </div>
          <div className="col-auto">
            <button className="btn btn-primary btn-sm" onClick={addFilter}>
              <i className="ti ti-plus me-1" />
              필터
            </button>
          </div>
        </div>
      </div>

      {/* ── 테이블 ── */}
      {!data ? (
        <div className="loading-spinner">
          <div className="spinner-border text-primary" role="status" />
        </div>
      ) : rows.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-filter-off" />
          <h3>결과 없음</h3>
        </div>
      ) : (
        <table className="table table-vcenter card-table table-hover table-striped">
          <thead>
            <tr>
              {tableType.fields.map((f) => (
                <th key={f.name}>{f.name}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, ri) => (
              <tr key={ri} style={{ animationDelay: `${Math.min(ri * 15, 300)}ms` }}>
                {tableType.fields.map((f) => {
                  const val = row[f.name]
                  return (
                    <td key={f.name}>
                      {f.type === 'bool' ? (
                        <span className={`badge ${val ? 'bg-green' : 'bg-red'}`}>
                          {val ? 'true' : 'false'}
                        </span>
                      ) : (
                        String(val ?? '')
                      )}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {/* ── 페이지네이션 ── */}
      {data && totalPages > 1 && (
        <div className="card-footer d-flex align-items-center">
          <p className="m-0 text-muted">
            전체 <b>{data.total}</b>건
          </p>
          <ul className="pagination m-0 ms-auto">
            <PageLink disabled={page === 0} onGo={() => setPage(page - 1)}>
              <i className="ti ti-chevron-left" />
            </PageLink>
            {pageRange(page, totalPages).map((p) => (
              <li key={p} className={`page-item ${p === page ? 'active' : ''}`}>
                <a
                  className="page-link"
                  href="#"
                  onClick={(e) => {
                    e.preventDefault()
                    setPage(p)
                  }}
                >
                  {p + 1}
                </a>
              </li>
            ))}
            <PageLink disabled={page >= totalPages - 1} onGo={() => setPage(page + 1)}>
              <i className="ti ti-chevron-right" />
            </PageLink>
          </ul>
        </div>
      )}

      {/* ── 통계 + 분포 (숫자 필드가 있을 때만) ── */}
      {numericFields.length > 0 && rows.length > 0 && (
        <StatsPanel
          tableName={tableType.tableName}
          numericFields={numericFields.map((f) => f.name)}
          filters={filters}
        />
      )}
    </div>
  )
}

function pageRange(page: number, totalPages: number): number[] {
  const start = Math.max(0, page - 2)
  const end = Math.min(totalPages, start + 5)
  const out: number[] = []
  for (let p = start; p < end; p++) out.push(p)
  return out
}

function PageLink({
  disabled,
  onGo,
  children,
}: {
  disabled: boolean
  onGo: () => void
  children: React.ReactNode
}) {
  return (
    <li className={`page-item ${disabled ? 'disabled' : ''}`}>
      <a
        className="page-link"
        href="#"
        onClick={(e) => {
          e.preventDefault()
          if (!disabled) onGo()
        }}
      >
        {children}
      </a>
    </li>
  )
}

/** 바닐라 loadStats() — 통계와 분포를 같은 필터로 함께 조회한다. */
function StatsPanel({
  tableName,
  numericFields,
  filters,
}: {
  tableName: string
  numericFields: string[]
  filters: TableFilter[]
}) {
  const [field, setField] = useState(numericFields[0] ?? '')
  const [stats, setStats] = useState<TableStats | null>(null)
  const [buckets, setBuckets] = useState<DistBucket[]>([])

  useEffect(() => {
    if (!field) return
    let cancelled = false
    void (async () => {
      const params = buildParams(filters)
      params.set('field', field)
      try {
        const s = await tableApi<TableStats>(`/${tableName}/_stats?${params}`)
        const d = await tableApi<{ buckets?: DistBucket[] }>(
          `/${tableName}/_distribution?${params}&buckets=10`,
        )
        if (cancelled) return
        setStats(s)
        setBuckets(d.buckets ?? [])
      } catch (e) {
        // 바닐라와 동일 — 통계 실패는 토스트를 띄우지 않고 콘솔에만 남긴다
        console.error('Stats error:', e)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [tableName, field, filters])

  const n = (v: number, digits = 0) =>
    Number(v).toLocaleString(undefined, { maximumFractionDigits: digits })

  return (
    <div className="p-3 border-top">
      <div className="row g-3">
        <div className="col-md-6">
          <div className="card card-sm">
            <div className="card-header">
              <h4 className="card-title">통계</h4>
              <div className="ms-auto">
                <select
                  className="form-select form-select-sm"
                  style={{ width: 'auto' }}
                  value={field}
                  onChange={(e) => setField(e.target.value)}
                >
                  {numericFields.map((f) => (
                    <option key={f} value={f}>
                      {f}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            <div className="card-body">
              {stats ? (
                <div className="row text-center">
                  <Stat label="합계" value={n(stats.sum)} />
                  <Stat label="평균" value={n(stats.avg, 1)} />
                  <Stat label="최대" value={n(stats.max)} />
                  <Stat label="최소" value={n(stats.min)} />
                  <Stat label="건수" value={n(stats.count)} />
                </div>
              ) : (
                <div className="text-muted">필드를 선택하세요</div>
              )}
            </div>
          </div>
        </div>
        <div className="col-md-6">
          <div className="card card-sm">
            <div className="card-header">
              <h4 className="card-title">분포</h4>
            </div>
            <div className="card-body">
              <DistChart buckets={buckets} field={field} />
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="col">
      <div className="text-muted small">{label}</div>
      <div className="fw-bold">{value}</div>
    </div>
  )
}
