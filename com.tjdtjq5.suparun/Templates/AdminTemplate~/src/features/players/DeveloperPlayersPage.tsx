import { useCallback, useEffect, useState } from 'react'
import { selectAll } from '../../shared/db'
import { getPlayer, type Player } from '../../shared/players'
import { LoadingBlock } from '../../shared/Spinner'
import { fmtDateTime } from '../audit/format'
import { useAdmin } from '../shell/AdminContext'

/**
 * Developer Players (#40, Metaplay Technical>Developer Players 동형 — 150-developer-players.png).
 *
 * 목록의 진실은 `suparun_developer` 표(쓰기는 서버 CS 액션뿐). 지정/해제는 플레이어 상세의
 * Admin Tools 에서 한다 — 이 화면은 "지금 누가 개발자인가"의 열람이다.
 */

interface DevRow {
  user_id: string
  note: string | null
  created_at: number
  created_by: string | null
}

export function DeveloperPlayersPage() {
  const { navigate, setPageSubtitle } = useAdmin()
  const [rows, setRows] = useState<(DevRow & { player?: Player | null })[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const devs = await selectAll<DevRow>('suparun_developer', { orderBy: 'created_at', ascending: false })
      setPageSubtitle(`${devs.length}명`)
      // 개발자는 소수다 — 프로필을 개별 RPC 로 붙여도 왕복이 몇 번 안 된다.
      const withPlayers = await Promise.all(
        devs.map(async (d) => ({ ...d, player: await getPlayer(d.user_id).catch(() => null) })),
      )
      setRows(withPlayers)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [setPageSubtitle])

  useEffect(() => {
    void load()
  }, [load])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>개발자 목록을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!rows) return <LoadingBlock label="개발자 목록 불러오는 중" />

  if (rows.length === 0) {
    return (
      <div className="empty-state">
        <i className="ti ti-code" />
        <h3>개발자 플레이어가 없습니다</h3>
        <p>플레이어 상세의 Admin Tools 에서 [개발자 지정]으로 추가합니다.</p>
      </div>
    )
  }

  return (
    <div className="m-3">
      <div className="table-responsive">
        <table className="table table-sm table-hover">
          <thead>
            <tr>
              <th style={{ width: 130 }}>ID</th>
              <th>이름 / 이메일</th>
              <th>메모</th>
              <th style={{ width: 170 }}>지정</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((d) => (
              <tr
                key={d.user_id}
                style={{ cursor: 'pointer' }}
                onClick={() => navigate({ kind: 'player', id: d.user_id })}
              >
                <td><code>{d.user_id.slice(0, 8)}…</code></td>
                <td>
                  {d.player ? (
                    <>
                      {d.player.name || <span className="text-muted">(이름 없음)</span>}
                      {d.player.email && <span className="text-muted ms-2">{d.player.email}</span>}
                    </>
                  ) : (
                    <span className="text-muted">(계정 조회 실패 — 삭제됐을 수 있음)</span>
                  )}
                </td>
                <td className="text-muted">{d.note || '-'}</td>
                <td className="text-muted">{fmtDateTime(d.created_at)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
