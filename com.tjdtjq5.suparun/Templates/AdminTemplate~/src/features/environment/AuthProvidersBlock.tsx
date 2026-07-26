import { useCallback, useEffect, useState } from 'react'
import { loadProviders, saveProvider, type ProviderState } from '../../shared/authProviders'
import { whoAmI, type WhoAmI } from '../../shared/edgeFn'
import { fieldLabels, formatWarning, ProviderSetupGuide } from './ProviderSetupGuide'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { sb } from '../../shared/supabase'
import { toast } from '../../shared/toast'

/**
 * OAuth 프로바이더 설정.
 *
 * **로그인 수단이 하나도 없는 동안에는 로그인 없이 쓸 수 있다.** 그 상태에서는 아무도
 * 로그인할 수 없으므로, 관리자만 허용하면 로그인 수단을 켜는 일 자체를 아무도 못 하게 된다.
 * 하나라도 켜지면 그 순간 이 화면은 관리자 전용으로 잠긴다.
 *
 * Client Secret 은 **읽어 오지 않는다.** 함수가 응답에서 지운다. 그래서 빈칸이 정상이고,
 * 빈칸으로 저장하면 기존 값을 그대로 둔다 — Client ID 만 고치려다 secret 을 날리지 않게.
 */
export function AuthProvidersBlock() {
  const [me, setMe] = useState<WhoAmI | null | undefined>(undefined)
  const [rows, setRows] = useState<ProviderState[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [open, setOpen] = useState<string | null>(null)
  const [clientId, setClientId] = useState('')
  const [secret, setSecret] = useState('')
  const [busy, setBusy] = useState(false)

  const reload = useCallback(async () => {
    setError(null)
    const who = await whoAmI()
    setMe(who)
    // 관리자이거나, 로그인할 방법 자체가 없는 구간이면 목록을 읽을 수 있다.
    if (!who?.isAdmin && !who?.setupOpen) {
      setRows(null)
      return
    }
    try {
      setRows(await loadProviders())
    } catch (e) {
      setRows([])
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

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

  /**
   * 실제로 로그인해 본다. 성공하면 이 페이지로 돌아오고, 설정이 틀렸으면
   * Google 이 `redirect_uri_mismatch` 를 띄운다 — 그 화면이 곧 진단이다.
   */
  async function tryLogin(p: ProviderState) {
    if (!sb) return toast('Supabase 연결이 설정되지 않았습니다.', 'error')
    setBusy(true)
    try {
      const { error } = await sb.auth.signInWithOAuth({
        provider: p.key,
        options: { redirectTo: window.location.href },
      })
      if (error) {
        toast(error.message, 'error')
        setBusy(false)
      }
      // 성공하면 곧 Google 로 넘어간다 — busy 를 풀지 않아야 그 사이 두 번 눌리지 않는다.
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setBusy(false)
    }
  }

  if (me === undefined) return <LoadingBlock label="확인 중" />

  // 함수가 아직 배포되지 않았거나 응답하지 않는다.
  if (me === null) {
    return (
      <div className="appset-note">
        설정 대행 함수가 응답하지 않습니다.
        <br />
        Unity → SupaRun Dashboard → 설정 → Supabase → 어드민 대행 함수에서 배포하세요.
      </div>
    )
  }

  // 로그인은 가능한데 관리자가 아니다. 이 구간은 로그인으로만 풀린다.
  if (!me.isAdmin && !me.setupOpen) {
    return (
      <div className="appset-note">
        {me.userId ? '관리자 권한이 없습니다.' : '로그인이 필요합니다.'}
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
              <>
                {/* 리디렉션 URI 가 실제로 등록됐는지는 **로그인해봐야만** 안다.
                    형식 검증으로도 안 잡히는 부분이라 이게 유일한 진짜 검증이다. */}
                <button className="btn btn-sm" disabled={busy} onClick={() => void tryLogin(p)}>
                  로그인 테스트
                </button>
                <button
                  className="btn btn-sm btn-outline-danger"
                  disabled={busy}
                  onClick={() => void save(p, false)}
                >
                  끄기
                </button>
              </>
            )}
          </div>

          {open === p.key && (
            <div className="appset-edit">
              <ProviderSetupGuide provider={p.key} />

              {/* autoComplete 를 지정하지 않으면 브라우저가 Client ID 를 이메일 칸으로,
                  Secret 을 비밀번호 칸으로 보고 저장된 값을 채운다. 실제로 이메일이
                  Client ID 로 저장되는 사고가 났다. `new-password` 는 저장된 비밀번호
                  채우기를 막는 표준 신호이고, name 을 비표준으로 두면 휴리스틱도 빗나간다. */}
              <label className="appset-label">{fieldLabels(p.key).id}</label>
              <input
                className="form-control form-control-sm"
                name="suparun-oauth-client"
                autoComplete="off"
                spellCheck={false}
                value={clientId}
                onChange={(e) => setClientId(e.target.value)}
                placeholder={`콘솔에서 발급한 ${fieldLabels(p.key).id}`}
              />
              <label className="appset-label">{fieldLabels(p.key).secret}</label>
              <input
                className="form-control form-control-sm"
                type="password"
                name="suparun-oauth-secret"
                autoComplete="new-password"
                value={secret}
                onChange={(e) => setSecret(e.target.value)}
                placeholder="비워 두면 기존 값을 유지합니다"
              />

              {/* 막지 않고 알리기만 한다 — 자세한 이유는 formatWarning 참조 */}
              {formatWarning(p.key, clientId, secret) && (
                <div className="gsetup-warn">{formatWarning(p.key, clientId, secret)}</div>
              )}

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
            </div>
          )}
        </div>
      ))}
    </>
  )
}
