import { useState, type CSSProperties, type ReactNode } from 'react'
import type { UnauthorizedInfo } from '../../shared/api'
import { env } from '../../shared/env'
import { Modal } from '../../shared/Modal'
import { Spinner } from '../../shared/Spinner'
import { sb, supabaseError } from '../../shared/supabase'

const GOOGLE_ICON = (
  <svg width="18" height="18" viewBox="0 0 24 24">
    <path
      fill="#4285F4"
      d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.27-4.74 3.27-8.1z"
    />
    <path
      fill="#34A853"
      d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
    />
    <path
      fill="#FBBC05"
      d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"
    />
    <path
      fill="#EA4335"
      d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"
    />
  </svg>
)

const OAUTH_STYLES: Record<string, { label: string; style?: CSSProperties; icon?: ReactNode }> = {
  google: { label: 'Google로 로그인', icon: GOOGLE_ICON },
  kakao: {
    label: '카카오로 로그인',
    style: { background: '#FEE500', borderColor: '#FEE500', color: '#000' },
  },
  apple: { label: 'Apple로 로그인', style: { background: '#000', borderColor: '#000', color: '#fff' } },
}

type Message = { kind: 'error' | 'success'; text: string } | null

/**
 * 로그인 화면. 바닐라 `#login-page` HTML 과 doLoginEmail / doSignUp / doLoginOAuth 를 대체한다.
 *
 * Enter 키 로그인은 document 리스너 대신 `<form onSubmit>` 으로 처리한다 —
 * 같은 동작이면서 화면 밖 키 입력까지 가로채지 않는다.
 *
 * 알림은 두 갈래다:
 *   - 입력 검증·로그인 실패·가입 완료 → 폼 안 인라인 박스 (입력하던 자리에서 바로 보인다)
 *   - 권한 없음(403)·세션 만료(401) → **모달**. 사용자가 따로 조치해야 하는 안내라
 *     한 번 읽고 닫게 한다.
 */
export function LoginPage({
  /** 401/403 으로 쫓겨났을 때의 안내. 모달로 띄운다. */
  notice,
  /** 안내 모달을 닫는다. */
  onDismissNotice,
}: {
  notice: UnauthorizedInfo | null
  onDismissNotice: () => void
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [msg, setMsg] = useState<Message>(null)
  /** 진행 중인 작업. `'login'` | `'signup'` | OAuth 프로바이더명. 버튼 하나가 도는 동안 나머지도 잠근다. */
  const [busy, setBusy] = useState<string | null>(null)

  // 설정 오류는 무엇을 해도 안 되므로 다른 메시지보다 우선한다.
  const shown: Message = supabaseError ? { kind: 'error', text: supabaseError } : msg

  function fail(text: string) {
    setMsg({ kind: 'error', text })
  }

  async function login() {
    setMsg(null)
    if (!email.trim()) return fail('이메일을 입력하세요.')
    if (!password) return fail('비밀번호를 입력하세요.')
    if (!sb) return fail('Supabase 연결 실패. SupaRun Dashboard에서 Supabase 설정을 확인하세요.')
    setBusy('login')
    try {
      const { error } = await sb.auth.signInWithPassword({ email: email.trim(), password })
      if (error) fail(error.message)
    } catch (e) {
      fail('로그인 실패: ' + (e instanceof Error ? e.message : String(e)))
    } finally {
      setBusy(null)
    }
  }

  async function signUp() {
    setMsg(null)
    if (!email.trim()) return fail('이메일을 입력하세요.')
    if (!password) return fail('비밀번호를 입력하세요.')
    if (password.length < 6) return fail('비밀번호는 6자 이상이어야 합니다.')
    if (!sb) return fail('Supabase 설정이 필요합니다.')
    setBusy('signup')
    try {
      const { data, error } = await sb.auth.signUp({ email: email.trim(), password })
      if (error) return fail(error.message)
      setMsg({
        kind: 'success',
        text: data.session
          ? '회원가입 완료!'
          : '회원가입 완료! 같은 이메일과 비밀번호로 로그인하세요.',
      })
    } catch (e) {
      fail('회원가입 실패: ' + (e instanceof Error ? e.message : String(e)))
    } finally {
      setBusy(null)
    }
  }

  async function oauth(provider: string) {
    if (!sb) return fail('Supabase 설정이 필요합니다.')
    setBusy(provider)
    // 성공하면 곧 프로바이더로 리다이렉트된다 — busy 를 풀지 않아야 그 사이 두 번 눌리지 않는다.
    try {
      const { error } = await sb.auth.signInWithOAuth({
        provider,
        options: { redirectTo: window.location.origin + '/admin/index.html' },
      })
      if (error) {
        fail(error.message)
        setBusy(null)
      }
    } catch (e) {
      fail('OAuth 로그인 실패: ' + (e instanceof Error ? e.message : String(e)))
      setBusy(null)
    }
  }

  const providers = env().authProviders

  return (
    <div id="login-page" className="page page-center">
      <div className="terminal-window">
        <div className="terminal-titlebar">
          <span className="dot" />
          <span className="title">SUPARUN.ADMIN :: AUTHENTICATE</span>
        </div>
        <div className="terminal-body">
          <div className="prompt-line">
            <span className="user">supabase://auth</span>
            <span className="sep">$</span> login --interactive
            <span className="cursor">_</span>
          </div>

          <form
            onSubmit={(e) => {
              e.preventDefault()
              void login()
            }}
          >
            <div className="form-line">
              <span className="prefix">&gt; email:</span>
              <input
                type="email"
                autoComplete="email"
                placeholder="admin@example.com"
                className="terminal-input"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
              />
            </div>
            <div className="form-line">
              <span className="prefix">&gt; password:</span>
              <input
                type="password"
                autoComplete="current-password"
                placeholder="6+ chars"
                className="terminal-input"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </div>

            {shown && (
              <div className={`alert alert-${shown.kind === 'error' ? 'danger' : 'success'}`}>
                {shown.text}
              </div>
            )}

            <div className="action-line">
              <button type="submit" className="btn-terminal" disabled={busy !== null}>
                {busy === 'login' ? (
                  <>
                    <Spinner size={12} />
                    [AUTH...]
                  </>
                ) : (
                  '[ENTER]'
                )}
              </button>
              <button
                type="button"
                className="btn-terminal"
                disabled={busy !== null}
                onClick={() => void signUp()}
              >
                {busy === 'signup' ? (
                  <>
                    <Spinner size={12} />
                    [SENDING...]
                  </>
                ) : (
                  '[REGISTER]'
                )}
              </button>
            </div>
          </form>

          {providers.length > 0 && (
            <div>
              <div className="oauth-divider">─── OR ───</div>
              <div className="oauth-list">
                {providers.map((p) => {
                  const s = OAUTH_STYLES[p] ?? { label: `${p}로 로그인` }
                  return (
                    <button
                      key={p}
                      className="btn btn-outline-secondary oauth-btn"
                      style={s.style}
                      disabled={busy !== null}
                      onClick={() => void oauth(p)}
                    >
                      {busy === p ? <Spinner size={14} /> : s.icon} {s.label}
                    </button>
                  )
                })}
              </div>
            </div>
          )}

          <div className="status-bar">SupaRun v0.7.0 / READY</div>
        </div>
      </div>

      {notice && (
        <Modal
          onClose={onDismissNotice}
          maxWidth={460}
          title={
            <span className="fw-bold px-2">
              {notice.status === 401 ? '세션 만료' : '접근 권한 없음'}
            </span>
          }
          footer={
            <div className="d-flex justify-content-end p-3 border-top">
              <button className="btn btn-primary" onClick={onDismissNotice} autoFocus>
                확인
              </button>
            </div>
          }
        >
          {/* 403 안내는 세 문장이다 — pre-line 으로 줄바꿈만 살린다 */}
          <div style={{ padding: 16, whiteSpace: 'pre-line', lineHeight: 1.7 }}>
            {notice.message}
          </div>
        </Modal>
      )}
    </div>
  )
}
