import { useCallback, useEffect, useState } from 'react'
import { loadProviders, saveProvider, type ProviderState } from '../../shared/authProviders'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import { useSession } from '../auth/useSession'

/**
 * OAuth 프로바이더 설정.
 *
 * **최초 한 번은 Unity 에서 켜야 한다.** 이 화면은 로그인해야 쓸 수 있는데(서버가 관리자만
 * 통과시킨다), 로그인 수단이 아직 없으면 로그인할 수가 없다. 그 매듭은 PAT 를 로컬에 쥔
 * Unity 만 끊을 수 있다. 켜진 뒤부터는 여기서 관리한다.
 *
 * Client Secret 은 **읽어 오지 않는다.** 서버가 응답에서 지운다. 그래서 빈칸이 정상이고,
 * 빈칸으로 저장하면 기존 값을 그대로 둔다 — Client ID 만 고치려다 secret 을 날리지 않게.
 */
export function AuthProvidersBlock() {
  const { session, ready } = useSession()
  const [rows, setRows] = useState<ProviderState[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState<string | null>(null)
  const [clientId, setClientId] = useState('')
  const [secret, setSecret] = useState('')
  const [busy, setBusy] = useState(false)

  const reload = useCallback(async () => {
    setError(null)
    try {
      setRows(await loadProviders())
    } catch (e) {
      setRows([])
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    if (session) void reload()
  }, [session, reload])

  function edit(p: ProviderState) {
    setOpen(p.key)
    setClientId(p.clientId)
    setSecret('')
  }

  async function save(p: ProviderState, enabled: boolean) {
    setBusy(true)
    try {
      await saveProvider({ provider: p.key, enabled, clientId, secret })
      toast(`${p.label} ${enabled ? '켰습니다' : '껐습니다'}`, 'success')
      setOpen(null)
      await reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(false)
    }
  }

  if (!ready) return <LoadingBlock label="확인 중" />

  // 로그인 없이 열려 있는 구간. 여기서는 서버가 통과시켜 주지 않으므로 안내만 한다.
  if (!session) {
    return (
      <div className="appset-note">
        최초 설정은 <strong>Unity → SupaRun Dashboard → 설정 → Auth</strong> 에서 합니다.
        <br />
        이 화면은 로그인해야 쓸 수 있는데, 로그인 수단이 아직 없으니 로그인할 수가 없습니다 —
        그 매듭은 Access Token 을 쥔 Unity 만 끊을 수 있습니다.
      </div>
    )
  }

  if (!rows) return <LoadingBlock label="로그인 설정 불러오는 중" />

  return (
    <>
      {error && <div className="alert alert-danger">{error}</div>}

      {rows.map((p) => (
        <div key={p.key} className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-name">{p.label}</div>
            <div className="appset-row-key">
              {p.enabled ? (p.clientId || '(Client ID 없음)') : '꺼짐'}
            </div>
          </div>

          <div className="appset-row-fields">
            <span className={`appset-status ${p.enabled ? 'on' : 'off'}`}>
              <i className={`ti ti-${p.enabled ? 'check' : 'minus'}`} />
              {p.enabled ? '켜짐' : '꺼짐'}
            </span>
            <button className="btn btn-sm" disabled={busy} onClick={() => edit(p)}>
              설정
            </button>
            {p.enabled && (
              <button
                className="btn btn-sm btn-outline-danger"
                disabled={busy}
                onClick={() => void save(p, false)}
              >
                끄기
              </button>
            )}
          </div>

          {open === p.key && (
            <div className="appset-edit">
              <label className="appset-label">Client ID</label>
              <input
                className="form-control form-control-sm"
                value={clientId}
                onChange={(e) => setClientId(e.target.value)}
                placeholder="Google Cloud Console 에서 발급"
              />
              <label className="appset-label">Client Secret</label>
              <input
                className="form-control form-control-sm"
                type="password"
                value={secret}
                onChange={(e) => setSecret(e.target.value)}
                placeholder="비워 두면 기존 값을 유지합니다"
              />
              <div className="appset-actions">
                <button
                  className="btn btn-primary btn-sm"
                  disabled={busy}
                  onClick={() => void save(p, true)}
                >
                  {busy ? <Spinner size={12} /> : '저장하고 켜기'}
                </button>
                <button className="btn btn-sm" disabled={busy} onClick={() => setOpen(null)}>
                  취소
                </button>
              </div>
              <div className="appset-note">
                Supabase 콜백 주소를 Google Cloud Console 의 승인된 리디렉션 URI 에 넣어야 합니다 —
                <code> {redirectUri()} </code>
              </div>
            </div>
          )}
        </div>
      ))}
    </>
  )
}

/** Supabase 가 OAuth 응답을 받는 고정 주소. Google 쪽에 그대로 등록해야 한다. */
function redirectUri(): string {
  const url = window.__SUPARUN_ENV?.supabaseUrl ?? ''
  return url ? `${url.replace(/\/$/, '')}/auth/v1/callback` : '(Supabase URL 미설정)'
}
