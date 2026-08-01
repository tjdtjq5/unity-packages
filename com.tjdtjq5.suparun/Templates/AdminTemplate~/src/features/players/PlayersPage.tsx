import { useEffect, useRef, useState } from 'react'
import { searchPlayers, type Player } from '../../shared/players'
import { LoadingBlock } from '../../shared/Spinner'
import { fmtDateTime, timeAgo } from '../audit/format'
import { useAdmin } from '../shell/AdminContext'

/**
 * 플레이어 검색 + 목록 (#36, Metaplay Manage Players 동형 — 10-players.png).
 *
 * 화면의 순서가 곧 운영의 순서다: 위에 검색(특정 플레이어를 찾아온 사람),
 * 아래에 최근 활동(무슨 일이 있는지 둘러보는 사람). 빈 질의 = 최근 로그인 순.
 */
export function PlayersPage() {
  const { navigate, setPageSubtitle } = useAdmin()
  const [query, setQuery] = useState('')
  const [players, setPlayers] = useState<Player[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const timer = useRef<number>(0)

  // 타자마다 RPC 를 쏘지 않는다 — 300ms 디바운스. 언마운트 시 타이머 정리.
  useEffect(() => {
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      searchPlayers(query)
        .then((r) => {
          setError(null)
          setPlayers(r)
          setPageSubtitle(`${r.length}명`)
        })
        .catch((e) => setError(e instanceof Error ? e.message : String(e)))
    }, query ? 300 : 0)
    return () => window.clearTimeout(timer.current)
  }, [query, setPageSubtitle])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>플레이어 목록을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  return (
    <div className="players-page">
      <div className="audit-search m-3">
        <div className="input-icon">
          <span className="input-icon-addon">
            <i className="ti ti-search" />
          </span>
          <input
            type="text"
            className="form-control"
            placeholder="플레이어 검색 — ID·이메일·이름"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
          />
        </div>
      </div>

      {!players ? (
        <LoadingBlock label="플레이어 불러오는 중" />
      ) : players.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-user-off" />
          <h3>{query ? '검색 결과가 없습니다' : '플레이어가 없습니다'}</h3>
          <p>{query ? 'ID 전체 또는 이메일·이름 일부로 검색합니다.' : '게임에 로그인한 계정이 여기에 나타납니다.'}</p>
        </div>
      ) : (
        <div className="m-3 mt-0">
          <div className="text-muted mb-2">
            {query ? '검색 결과' : '최근 활동 플레이어'}
          </div>
          <div className="table-responsive">
            <table className="table table-sm table-hover">
              <thead>
                <tr>
                  <th style={{ width: 130 }}>ID</th>
                  <th>이름 / 이메일</th>
                  <th style={{ width: 110 }}>상태</th>
                  <th style={{ width: 150 }}>가입</th>
                  <th style={{ width: 120 }}>최근 로그인</th>
                </tr>
              </thead>
              <tbody>
                {players.map((p) => (
                  <tr
                    key={p.id}
                    style={{ cursor: 'pointer' }}
                    onClick={() => navigate({ kind: 'player', id: p.id })}
                  >
                    <td><code>{p.id.slice(0, 8)}…</code></td>
                    <td>
                      {p.name || <span className="text-muted">(이름 없음)</span>}
                      {p.email && <span className="text-muted ms-2">{p.email}</span>}
                    </td>
                    <td>
                      {p.banned && <span className="badge bg-red me-1">banned</span>}
                      {p.is_developer && <span className="badge bg-azure">dev</span>}
                      {!p.banned && !p.is_developer && <span className="text-muted">-</span>}
                    </td>
                    <td className="text-muted">{fmtDateTime(p.created_at)}</td>
                    <td className="text-muted">{p.last_sign_in_at ? timeAgo(p.last_sign_in_at) : '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
