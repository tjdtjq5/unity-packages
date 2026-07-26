import { useEffect, useState } from 'react'
import { countRows, selectAll } from '../../shared/db'
import { loadPolicies, type PolicyState } from '../../shared/policy'
import { LoadingBlock } from '../../shared/Spinner'
import type { AuditLog } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'

/**
 * 환경에 들어오면 처음 보이는 화면.
 *
 * **PAT 없이 얻을 수 있는 것만 담는다** — 전부 PostgREST 로 읽는다. Unity 가 켜져 있든 말든 뜬다.
 * 예전에는 이 자리가 "Config 를 선택하세요" 빈 화면이었다.
 *
 * 고른 항목의 기준은 "들어오자마자 알아야 손해를 막는 것":
 *   정책 경고 — anon 이 쓸 수 있는 테이블이 있으면 그건 즉시 알아야 한다
 *   최근 변경 — 누가 방금 무엇을 고쳤는가
 *   스냅샷   — 되돌릴 자리가 있는가
 */
export function DashboardPage() {
  const { types, tableTypes, navigate } = useAdmin()
  const [policies, setPolicies] = useState<PolicyState[] | null>(null)
  const [audit, setAudit] = useState<AuditLog[] | null>(null)
  const [snapCount, setSnapCount] = useState<number | null>(null)
  const [lastSnap, setLastSnap] = useState<{ label: string; created_at: number } | null>(null)

  useEffect(() => {
    let alive = true
    void (async () => {
      // 각각 독립이라 하나가 실패해도 나머지는 보여준다.
      try {
        const p = await loadPolicies()
        if (alive) setPolicies(p)
      } catch {
        if (alive) setPolicies([])
      }
      try {
        const a = await selectAll<AuditLog>('admin_audit_log', {
          orderBy: 'created_at',
          ascending: false,
          limit: 5,
        })
        if (alive) setAudit(a)
      } catch {
        if (alive) setAudit([])
      }
      try {
        const n = await countRows('suparun_snapshot')
        if (alive) setSnapCount(n)
        const s = await selectAll<{ label: string; created_at: number }>('suparun_snapshot', {
          orderBy: 'created_at',
          ascending: false,
          limit: 1,
        })
        if (alive) setLastSnap(s[0] ?? null)
      } catch {
        if (alive) setSnapCount(0)
      }
    })()
    return () => {
      alive = false
    }
  }, [])

  // 쓰기가 아무에게나 열린 테이블. 실제로 이런 정책 8개가 아무도 모르게 있던 적이 있다.
  const unsafe = policies?.filter((p) => p.unsafe) ?? []

  return (
    <div className="dash">
      {unsafe.length > 0 && (
        <div className="dash-alert">
          <i className="ti ti-alert-triangle me-2" />
          <div>
            <strong>쓰기가 열린 테이블 {unsafe.length}개</strong>
            <div className="dash-alert-sub">
              {unsafe.map((p) => p.table_name).join(', ')}
              <br />
              anon key 만 있으면 누구나 수정·삭제할 수 있습니다. 표 상단의 정책 배지에서 바꾸세요.
            </div>
          </div>
        </div>
      )}

      <div className="dash-grid">
        <Tile
          label="데이터"
          value={String(types.length)}
          sub={`${tableTypes.length} 플레이어 테이블`}
        />
        <Tile
          label="스냅샷"
          value={snapCount == null ? '—' : String(snapCount)}
          sub={lastSnap ? `최근 ${lastSnap.label}` : '없음'}
          onClick={() => navigate({ kind: 'snapshots' })}
        />
        <Tile
          label="정책 경고"
          value={policies == null ? '—' : String(unsafe.length)}
          sub={policies == null ? '확인 중' : `전체 ${policies.length}개 테이블`}
          danger={unsafe.length > 0}
        />
      </div>

      <section className="dash-block">
        <div className="dash-block-head">
          <h3 className="dash-block-title">최근 변경</h3>
          <button className="btn btn-sm" onClick={() => navigate({ kind: 'audit' })}>
            전체 보기
          </button>
        </div>

        {audit == null ? (
          <LoadingBlock label="불러오는 중" size={20} />
        ) : audit.length === 0 ? (
          <div className="dash-empty">기록이 없습니다.</div>
        ) : (
          <table className="table table-sm table-vcenter">
            <tbody>
              {/* AuditLog 에는 id 가 없다 — AuditPage 와 같이 시각+인덱스로 키를 만든다 */}
              {audit.map((a, i) => (
                <tr key={`${a.created_at}_${i}`}>
                  <td style={{ width: 150 }} className="text-muted">
                    {new Date(a.created_at).toLocaleString('ko-KR')}
                  </td>
                  <td style={{ width: 90 }}>
                    <span className="badge bg-blue-lt">{a.action}</span>
                  </td>
                  <td>{a.config_type}</td>
                  <td className="text-muted">{a.row_id ?? ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  )
}

function Tile({
  label,
  value,
  sub,
  danger,
  onClick,
}: {
  label: string
  value: string
  sub?: string
  danger?: boolean
  onClick?: () => void
}) {
  return (
    <div
      className={`dash-tile${onClick ? ' clickable' : ''}${danger ? ' danger' : ''}`}
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      onKeyDown={(e) => {
        if (onClick && (e.key === 'Enter' || e.key === ' ')) {
          e.preventDefault()
          onClick()
        }
      }}
    >
      <div className="dash-tile-label">{label}</div>
      <div className="dash-tile-value">{value}</div>
      {sub && <div className="dash-tile-sub">{sub}</div>}
    </div>
  )
}
