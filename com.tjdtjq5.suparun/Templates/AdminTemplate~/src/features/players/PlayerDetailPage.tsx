import { useEffect, useState } from 'react'
import { AdminTools } from './AdminTools'
import { selectBy } from '../../shared/db'
import { getPlayer, type Player } from '../../shared/players'
import { LoadingBlock } from '../../shared/Spinner'
import type { TableRow, TableType } from '../../shared/types'
import { recordViewed } from '../audit/viewed'
import { fmtDateTime, timeAgo } from '../audit/format'
import { useAdmin } from '../shell/AdminContext'

/**
 * 플레이어 상세 (#37, Metaplay Manage Player 동형 — 11-player-detail.png).
 *
 * 계정(auth) 카드 + [UserData] 표들을 본인 행으로 필터해 카드로 늘어놓는다 —
 * 표 목록은 메타(table_types.hasUserId)에서 오므로 게임이 표를 늘리면 카드도 는다.
 * 진입은 열람 감사(#27 자기기록)에 남는다. Admin Tools 자리는 #38 부터 채운다.
 */
export function PlayerDetailPage({ id }: { id: string }) {
  const { tableTypes, navigate } = useAdmin()
  const [player, setPlayer] = useState<Player | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)
  const [rows, setRows] = useState<Record<string, TableRow[]>>({})
  // CS 액션 실행 후 재조회 트리거 — 잔액·밴 상태가 카드에 바로 반영돼야 한다.
  const [generation, setGeneration] = useState(0)

  const userTables = tableTypes.filter((t) => t.playerColumn)

  useEffect(() => {
    if (generation === 0) setPlayer(undefined)
    getPlayer(id)
      .then((p) => {
        setPlayer(p)
        // 존재하는 플레이어를 실제로 열람했을 때만 감사에 남긴다 — 오타 진입은 열람이 아니다.
        if (p && generation === 0) recordViewed('player', p.id)
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }, [id, generation])

  useEffect(() => {
    if (!player) return
    let alive = true
    void Promise.all(
      // 컬럼명은 메타(playerColumn)가 준다 — 코드젠이 소문자 실컬럼명을 내보낸다.
      userTables.map(async (t) => [t.tableName, await selectBy<TableRow>(t.tableName, t.playerColumn!, player.id, 20)] as const),
    )
      .then((entries) => {
        if (alive) setRows(Object.fromEntries(entries))
      })
      .catch(() => {
        /* 카드 하나가 실패해도 페이지는 산다 — 해당 카드가 비어 보일 뿐이다 */
      })
    return () => {
      alive = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [player])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>플레이어를 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (player === undefined) return <LoadingBlock label="플레이어 불러오는 중" />

  // 없는 ID 는 명시적으로 말한다 (#37 AC) — 빈 화면은 "로딩 중인가?" 라는 오해를 만든다.
  if (player === null) {
    return (
      <div className="empty-state">
        <i className="ti ti-user-question" />
        <h3>존재하지 않는 플레이어입니다</h3>
        <p><code>{id}</code> 에 해당하는 계정이 없습니다 — 삭제되었거나 ID 오타일 수 있습니다.</p>
        <button className="btn btn-sm" onClick={() => navigate({ kind: 'players' })}>
          <i className="ti ti-arrow-left me-1" /> 플레이어 목록으로
        </button>
      </div>
    )
  }

  return (
    <div className="player-detail m-3">
      {/* ── 계정 카드 — Metaplay 의 Overview 열 동형 ── */}
      <div className="d-flex align-items-center mb-2">
        <h2 className="m-0 me-2">{player.name || '(이름 없음)'}</h2>
        {player.banned && <span className="badge bg-red me-1">banned</span>}
        {player.is_developer && <span className="badge bg-azure">dev</span>}
        <span className="text-muted ms-auto">
          ID <code>{player.id}</code>
        </span>
      </div>

      <table className="table table-sm mb-3" style={{ maxWidth: 560 }}>
        <tbody>
          <tr>
            <td className="text-muted" style={{ width: 130 }}>이메일</td>
            <td>{player.email ?? '-'}</td>
          </tr>
          <tr>
            <td className="text-muted">가입</td>
            <td>{fmtDateTime(player.created_at)}</td>
          </tr>
          <tr>
            <td className="text-muted">최근 로그인</td>
            <td>{player.last_sign_in_at ? `${timeAgo(player.last_sign_in_at)} (${fmtDateTime(player.last_sign_in_at)})` : '-'}</td>
          </tr>
          {player.banned && (
            <tr>
              <td className="text-muted">밴</td>
              <td>
                {player.ban_reason || '(사유 없음)'}
                <span className="text-muted ms-2">
                  {player.banned_until === 0 ? '영구' : `${fmtDateTime(player.banned_until ?? 0)} 까지`}
                </span>
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {/* ── [UserData] 카드들 — 게임 데이터의 진실을 표별로 ── */}
      {userTables.length === 0 && (
        <p className="text-muted">이 게임에는 플레이어 귀속 [UserData] 표가 없습니다.</p>
      )}
      {userTables.map((t) => (
        <UserDataCard key={t.tableName} table={t} rows={rows[t.tableName]} />
      ))}

      {/* Admin Tools (#38~#42) — 버튼 목록은 메타(cs_actions), 실행은 서버 롤 게이트+감사. */}
      <AdminTools player={player} onChanged={() => setGeneration((g) => g + 1)} />
    </div>
  )
}

function UserDataCard({ table, rows }: { table: TableType; rows?: TableRow[] }) {
  // 플레이어 컬럼은 페이지 자체가 그 사람이다 — 카드마다 반복하면 노이즈.
  const fields = table.fields.filter((f) => !f.isHidden && f.name.toLowerCase() !== table.playerColumn)
  return (
    <details className="compare-table mb-2" open={!!rows && rows.length > 0}>
      <summary>
        <b>{table.name}</b>
        <span className="text-muted ms-2">{rows ? `${rows.length}행` : '읽는 중…'}</span>
      </summary>
      {rows && rows.length > 0 ? (
        <div className="table-responsive mt-2">
          <table className="table table-sm">
            <thead>
              <tr>
                {fields.map((f) => (
                  <th key={f.name}>{f.name}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr key={i}>
                  {fields.map((f) => (
                    <td key={f.name}>{cell(r[f.name.toLowerCase()] ?? r[f.name])}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-muted mt-2 mb-1">이 플레이어의 행이 없습니다.</p>
      )}
    </details>
  )
}

function cell(v: unknown): string {
  if (v == null) return '-'
  if (typeof v === 'object') return JSON.stringify(v)
  return String(v)
}
