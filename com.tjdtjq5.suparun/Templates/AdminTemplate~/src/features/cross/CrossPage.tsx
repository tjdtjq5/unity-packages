import { useRef, useState } from 'react'
import { tableApi } from '../../shared/api'
import { toast } from '../../shared/toast'
import { enableColResize } from '../../shared/colResize'
import { useAdmin } from '../shell/AdminContext'
import type { CrossCondition, CrossResult } from '../../shared/types'

const OPS = ['=', '>', '>=', '<', '<='] as const

/** 서버 details 의 키는 snake_case 다. 바닐라와 동일한 변환. */
function toSnake(s: string): string {
  return s.replace(/[A-Z]/g, (m) => '_' + m.toLowerCase())
}

/**
 * 크로스 테이블 검색. 바닐라 showCrossSearch/renderCrossSearch/executeCross 를 옮긴 것이다.
 *
 * 바닐라는 조건 값을 DOM(`cc-table-0` 등)에서 읽어 실행 직전에 다시 수집했다.
 * React 에서는 상태가 곧 진실이므로 그 수집 단계가 사라진다.
 */
export function CrossPage() {
  const { tableTypes, navigate } = useAdmin()
  const [conditions, setConditions] = useState<CrossCondition[]>([])
  const [result, setResult] = useState<CrossResult | null>(null)
  const [loading, setLoading] = useState(false)
  const resultRef = useRef<HTMLDivElement>(null)

  function addCondition() {
    const t = tableTypes[0]
    if (!t) return
    setConditions((prev) => [
      ...prev,
      { table: t.tableName, field: t.fields[0]?.name ?? '', op: '>=', value: '' },
    ])
  }

  function patch(i: number, next: Partial<CrossCondition>) {
    setConditions((prev) =>
      prev.map((c, idx) => {
        if (idx !== i) return c
        const merged = { ...c, ...next }
        // 테이블이 바뀌면 필드를 그 테이블의 첫 필드로 되돌린다 (바닐라 updateCrossFields 동작)
        if (next.table !== undefined && next.table !== c.table) {
          const t = tableTypes.find((x) => x.tableName === next.table)
          merged.field = t?.fields[0]?.name ?? ''
        }
        return merged
      }),
    )
  }

  async function execute() {
    if (conditions.length === 0 || conditions.some((c) => !c.value)) {
      toast('모든 조건에 값을 입력하세요', 'error')
      return
    }
    setLoading(true)
    setResult(null)
    try {
      const res = await tableApi<CrossResult>('/_cross', 'POST', { conditions, limit: 100 })
      setResult(res)
      toast(`크로스 검색 완료: ${res.count}명`, 'success')

      // 바닐라와 동일하게 컬럼 너비 조정을 붙인다. 데이터는 user_id 별로 흩어져 있어
      // 평면화가 까다로우므로 fields 만 합성한다.
      const usedTables = [...new Set(conditions.map((c) => c.table))]
      const crossFields: { name: string; type: string }[] = [{ name: 'user_id', type: 'string' }]
      for (const t of usedTables) {
        const tt = tableTypes.find((x) => x.tableName === t)
        for (const c of conditions.filter((x) => x.table === t)) {
          const meta = tt?.fields?.find((f) => f.name === c.field)
          crossFields.push(meta ?? { name: `${t}.${c.field}`, type: 'string' })
        }
      }
      // 렌더 직후에 붙여야 표가 존재한다
      requestAnimationFrame(() => {
        if (resultRef.current) {
          enableColResize(resultRef.current, 'cross_search', {
            fields: crossFields,
            data: [],
          })
        }
      })
    } catch (e) {
      toast('검색 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    } finally {
      setLoading(false)
    }
  }

  const usedTables = result ? [...new Set(conditions.map((c) => c.table))] : []

  return (
    <div className="p-4">
      <div className="mb-3">
        {conditions.map((c, i) => {
          const t = tableTypes.find((x) => x.tableName === c.table)
          return (
            <div key={i} className="row g-2 mb-2 align-items-end">
              <div className="col-3">
                <select
                  className="form-select form-select-sm"
                  value={c.table}
                  onChange={(e) => patch(i, { table: e.target.value })}
                >
                  {tableTypes.map((tt) => (
                    <option key={tt.tableName} value={tt.tableName}>
                      {tt.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-3">
                <select
                  className="form-select form-select-sm"
                  value={c.field}
                  onChange={(e) => patch(i, { field: e.target.value })}
                >
                  {(t?.fields ?? []).map((f) => (
                    <option key={f.name} value={f.name}>
                      {f.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-2">
                <select
                  className="form-select form-select-sm"
                  value={c.op}
                  onChange={(e) => patch(i, { op: e.target.value })}
                >
                  {OPS.map((op) => (
                    <option key={op} value={op}>
                      {op}
                    </option>
                  ))}
                </select>
              </div>
              <div className="col-2">
                <input
                  className="form-control form-control-sm"
                  value={c.value}
                  onChange={(e) => patch(i, { value: e.target.value })}
                />
              </div>
              <div className="col-2">
                <button
                  className="btn btn-ghost-danger btn-icon btn-sm"
                  onClick={() => setConditions((prev) => prev.filter((_, idx) => idx !== i))}
                >
                  <i className="ti ti-x" />
                </button>
              </div>
            </div>
          )
        })}
      </div>

      <div className="d-flex gap-2">
        <button className="btn btn-outline-primary btn-sm" onClick={addCondition}>
          <i className="ti ti-plus me-1" />
          조건 추가
        </button>
        <button className="btn btn-primary btn-sm" onClick={() => void execute()}>
          <i className="ti ti-search me-1" />
          검색
        </button>
      </div>

      <div className="mt-3" ref={resultRef}>
        {loading && (
          <div className="loading-spinner">
            <div className="spinner-border text-primary" role="status" />
          </div>
        )}
        {result && (
          <>
            <div className="alert alert-info">
              <i className="ti ti-info-circle me-1" />
              <b>{result.count}명</b> 일치
            </div>
            {result.userIds && result.userIds.length > 0 && result.details && (
              <table className="table table-vcenter table-striped table-hover">
                <thead>
                  <tr>
                    <th>user_id</th>
                    {usedTables.flatMap((t) => {
                      const tt = tableTypes.find((x) => x.tableName === t)
                      return conditions
                        .filter((c) => c.table === t)
                        .map((c) => (
                          <th key={`${t}.${c.field}`}>
                            {tt?.name ?? t}.{c.field}
                          </th>
                        ))
                    })}
                  </tr>
                </thead>
                <tbody>
                  {result.userIds.map((uid) => (
                    <tr key={uid}>
                      <td>
                        <a
                          href="#"
                          className="text-primary"
                          onClick={(e) => {
                            e.preventDefault()
                            navigate({ kind: 'player', userId: uid })
                          }}
                        >
                          <code>{uid}</code>
                        </a>
                      </td>
                      {usedTables.flatMap((t) =>
                        conditions
                          .filter((c) => c.table === t)
                          .map((c) => {
                            const detail = result.details?.[uid]?.[t] ?? {}
                            const val = detail[toSnake(c.field)] ?? detail[c.field] ?? ''
                            return <td key={`${uid}.${t}.${c.field}`}>{String(val)}</td>
                          }),
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>
    </div>
  )
}
