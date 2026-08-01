import { useState } from 'react'
import { Spinner } from '../../shared/Spinner'
import { sb, supabaseError } from '../../shared/supabase'

type Message = { kind: 'error' | 'success'; text: string } | null

/**
 * 로그인 화면 — **이메일+비밀번호 전용** (ADR-0009, #23).
 *
 * 기계 계정 자동 로그인을 걷어내며 복원했다. 매직링크는 기본 SMTP 가 시간당 2통이라 기각,
 * OAuth 는 프로바이더 앱 등록 부담으로 기각 — 이메일+비밀번호는 메일을 쓰지 않는다.
 *
 * [REGISTER] 로 가입한 계정이 곧바로 관리자가 되는 것은 아니다 — 로컬(브리지)에서는
 * 로그인 직후 `/auth/claim-admin` 이 등록하고(App.tsx), 그 외에는 기존 관리자의 승인을
 * 기다린다.
 *
 * Enter 키 로그인은 document 리스너 대신 `<form onSubmit>` 으로 처리한다 —
 * 같은 동작이면서 화면 밖 키 입력까지 가로채지 않는다.
 */
export function LoginPage() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [msg, setMsg] = useState<Message>(null)
  /** 진행 중인 작업(`'login'` | `'signup'`). 버튼 하나가 도는 동안 나머지도 잠근다. */
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
    if (!sb) return fail('Supabase 연결 실패. Supabase 설정을 확인하세요.')
    setBusy('login')
    try {
      const { error } = await sb.auth.signInWithPassword({ email: email.trim(), password })
      if (error) fail(error.message)
      // 성공 처리는 없다 — onAuthStateChange(useSession)가 세션을 받아 App 이 화면을 바꾼다.
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
      // autoconfirm(셋업 시 켜짐)이면 세션이 바로 와서 App 이 곧장 화면을 바꾼다.
      // 세션이 없다면 이 프로젝트는 autoconfirm 이 꺼져 확인 메일 경로로 빠진 것이다.
      setMsg({
        kind: 'success',
        text: data.session
          ? '회원가입 완료!'
          : '회원가입 완료! 확인 메일을 승인한 뒤 로그인하세요.',
      })
    } catch (e) {
      fail('회원가입 실패: ' + (e instanceof Error ? e.message : String(e)))
    } finally {
      setBusy(null)
    }
  }

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
                    [SIGNUP...]
                  </>
                ) : (
                  '[REGISTER]'
                )}
              </button>
            </div>
          </form>

          <div className="status-bar">SupaRun v0.7.0 / READY</div>
        </div>
      </div>
    </div>
  )
}
