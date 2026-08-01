import { useEffect, useState } from 'react'
import { loadCsActions, runCsAction, type CsAction } from '../../shared/csapi'
import { selectBy } from '../../shared/db'
import { Modal } from '../../shared/Modal'
import type { Player } from '../../shared/players'
import { Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import type { TableRow, TableType } from '../../shared/types'
import { recordViewed } from '../audit/viewed'
import { useAdmin } from '../shell/AdminContext'

/**
 * Admin Tools (③ 트랙 #38~#42, Metaplay 동형 — 11-player-detail.png).
 *
 * 버튼 목록은 메타(cs_actions)가 진실이다 — [CsAction] 메서드 하나 = 버튼 하나.
 * 위험도로 두 그룹으로 가른다(Metaplay 의 Gentle/Dangerous 축약): dangerous 는 빨간 줄 +
 * 대상 ID 재입력 2단계 확인(#42). seniorOnly 는 cs-senior·game-admin 에게만 보인다 —
 * UI 겹일 뿐, 진짜 거부는 서버 롤 게이트가 한다.
 */

const CS_ROLES = ['game-admin', 'cs-senior', 'cs-agent']
const SENIOR_ROLES = ['game-admin', 'cs-senior']

export function AdminTools({ player, onChanged }: { player: Player; onChanged: () => void }) {
  const { roles, tableTypes } = useAdmin()
  const [actions, setActions] = useState<CsAction[]>([])
  const [active, setActive] = useState<CsAction | null>(null)

  useEffect(() => {
    loadCsActions()
      .then(setActions)
      .catch(() => setActions([]))
  }, [])

  const isCs = roles.some((r) => CS_ROLES.includes(r))
  const isSenior = roles.some((r) => SENIOR_ROLES.includes(r))
  if (!isCs) return null

  const visible = actions.filter((a) => !a.seniorOnly || isSenior)
  const gentle = visible.filter((a) => !a.dangerous)
  const danger = visible.filter((a) => a.dangerous)

  return (
    <div className="mt-3">
      <h3 className="mb-2">
        <i className="ti ti-tool me-1" /> Admin Tools
      </h3>
      <div className="btn-list mb-2">
        {gentle.map((a) => (
          <button key={a.path} className="btn btn-sm" onClick={() => setActive(a)}>
            {a.label}
          </button>
        ))}
        {/* GDPR 내보내기(#41)는 서버 액션이 아니다 — 열람 권한으로 모아서 내려받고,
            실행 사실만 감사(gdpr_export)에 남긴다. */}
        <GdprExportButton player={player} tableTypes={tableTypes} />
      </div>
      {danger.length > 0 && (
        <div className="btn-list">
          {danger.map((a) => (
            <button key={a.path} className="btn btn-sm btn-outline-danger" onClick={() => setActive(a)}>
              {a.label}
            </button>
          ))}
        </div>
      )}
      {visible.length === 0 && <p className="text-muted">사용할 수 있는 CS 액션이 없습니다 — 서버 배포가 먼저입니다.</p>}

      {active && (
        <CsActionModal
          action={active}
          player={player}
          onClose={() => setActive(null)}
          onDone={() => {
            setActive(null)
            onChanged()
          }}
        />
      )}
    </div>
  )
}

/**
 * 액션 모달 — 파라미터 폼은 메타(params)에서 자동으로 그린다. playerId 는 페이지의
 * 플레이어로 잠겨 있다(오입력 방지). dangerous 는 대상 ID 재입력이 열쇠다(#42).
 */
function CsActionModal({
  action,
  player,
  onClose,
  onDone,
}: {
  action: CsAction
  player: Player
  onClose: () => void
  onDone: () => void
}) {
  const [values, setValues] = useState<Record<string, unknown>>(() => {
    const v: Record<string, unknown> = {}
    for (const p of action.params) v[p.name] = p.type === 'bool' ? false : p.type === 'number' ? 0 : ''
    v.playerId = player.id
    return v
  })
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)

  const confirmed = !action.dangerous || typed.trim() === player.id
  const formParams = action.params.filter((p) => p.name !== 'playerId')

  async function run() {
    setBusy(true)
    try {
      await runCsAction(action, values)
      toast(`${action.label} 완료`)
      onDone()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setBusy(false)
    }
  }

  return (
    <Modal
      onClose={onClose}
      maxWidth={480}
      title={<span className="fw-bold px-2">{action.label} — {player.name || player.id.slice(0, 8)}</span>}
      footer={
        <div className="d-flex justify-content-between align-items-center p-3 border-top">
          <span className="text-muted small">{action.path}</span>
          <div className="btn-list">
            <button className="btn" onClick={onClose} disabled={busy}>취소</button>
            <button
              className={`btn ${action.dangerous ? 'btn-danger' : 'btn-primary'}`}
              disabled={!confirmed || busy}
              onClick={() => void run()}
            >
              {busy ? <><Spinner size={12} /> 실행 중…</> : '실행'}
            </button>
          </div>
        </div>
      }
    >
      <div style={{ padding: 16 }}>
        {formParams.map((p) => (
          <div key={p.name} className="mb-2">
            {p.type === 'bool' ? (
              <label className="form-check">
                <input
                  type="checkbox"
                  className="form-check-input"
                  checked={!!values[p.name]}
                  onChange={(e) => setValues((v) => ({ ...v, [p.name]: e.target.checked }))}
                />
                <span className="form-check-label">{p.name}</span>
              </label>
            ) : (
              <>
                <label className="form-label mb-1">{p.name}</label>
                <input
                  type={p.type === 'number' ? 'number' : 'text'}
                  className="form-control form-control-sm"
                  value={String(values[p.name] ?? '')}
                  onChange={(e) =>
                    setValues((v) => ({ ...v, [p.name]: p.type === 'number' ? Number(e.target.value) : e.target.value }))
                  }
                />
              </>
            )}
          </div>
        ))}

        {action.dangerous && (
          <div className="mt-3">
            <label className="form-label">
              위험한 조작입니다 — 확인을 위해 대상 ID <code>{player.id}</code> 를 입력하세요
            </label>
            <input
              className="form-control"
              value={typed}
              spellCheck={false}
              onChange={(e) => setTyped(e.target.value)}
            />
          </div>
        )}

        <p className="text-muted small mt-3 mb-0">실행은 감사 로그(cs:{action.method})에 남습니다.</p>
      </div>
    </Modal>
  )
}

/**
 * GDPR 데이터 내보내기 (#41) — [UserData] 표 전체 + 계정 요약을 JSON 으로 내려받는다.
 * 내부 전용 필드(isHidden)와 다른 플레이어를 가리킬 수 있는 것 없는 **본인 행**만 담는다.
 * 내려받기 전에 미리보기를 보여주고, 실행은 감사(gdpr_export)에 남는다.
 */
function GdprExportButton({ player, tableTypes }: { player: Player; tableTypes: TableType[] }) {
  const [preview, setPreview] = useState<Record<string, unknown> | null>(null)
  const [busy, setBusy] = useState(false)

  async function build() {
    setBusy(true)
    try {
      const userTables = tableTypes.filter((t) => t.playerColumn)
      const data: Record<string, unknown> = {
        account: {
          id: player.id,
          email: player.email,
          name: player.name,
          created_at: player.created_at,
          last_sign_in_at: player.last_sign_in_at,
        },
      }
      for (const t of userTables) {
        const rows = await selectBy<TableRow>(t.tableName, t.playerColumn!, player.id, 1000)
        // 내부 전용(isHidden) 컬럼 제외 — 미리보기에서 빠졌음이 눈으로 확인된다.
        const hidden = new Set(t.fields.filter((f) => f.isHidden).map((f) => f.name.toLowerCase()))
        data[t.tableName] = rows.map((r) =>
          Object.fromEntries(Object.entries(r).filter(([k]) => !hidden.has(k.toLowerCase()))),
        )
      }
      recordViewed('player', player.id, 'gdpr_export')
      setPreview(data)
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(false)
    }
  }

  function download() {
    if (!preview) return
    const blob = new Blob([JSON.stringify(preview, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `gdpr-${player.id}.json`
    a.click()
    URL.revokeObjectURL(url)
  }

  return (
    <>
      <button className="btn btn-sm" disabled={busy} onClick={() => void build()}>
        {busy ? <Spinner size={12} /> : <i className="ti ti-download me-1" />}
        GDPR 내보내기
      </button>
      {preview && (
        <Modal
          onClose={() => setPreview(null)}
          maxWidth={640}
          title={<span className="fw-bold px-2">GDPR 내보내기 — 미리보기</span>}
          footer={
            <div className="d-flex justify-content-end p-3 border-top">
              <div className="btn-list">
                <button className="btn" onClick={() => setPreview(null)}>닫기</button>
                <button className="btn btn-primary" onClick={download}>
                  <i className="ti ti-download me-1" /> JSON 다운로드
                </button>
              </div>
            </div>
          }
        >
          <pre style={{ maxHeight: 380, overflow: 'auto', margin: 0, padding: 16, fontSize: 12 }}>
            {JSON.stringify(preview, null, 2)}
          </pre>
        </Modal>
      )}
    </>
  )
}
