import { useState, type CSSProperties, type ReactNode } from 'react'
import type { UnauthorizedInfo } from '../../shared/api'
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

/**
 * 로그인 화면 — **OAuth 전용**.
 *
 * 이메일/비밀번호는 없앴다. 프로젝트마다 관리자 계정을 따로 만들고 비밀번호를 나눠 갖는 방식이었는데,
 * 계정이 프로젝트 수만큼 늘고 퇴사자 정리가 프로젝트마다 반복된다. 구글 계정 하나로 모든 환경에
 * 들어오고, 들어올 수 있는 사람은 `admin_user` 한 줄로 정해진다.
 *
 * 알림은 두 갈래다:
 *   - 로그인 실패 → 폼 안 인라인 박스
 *   - 권한 없음(403)·세션 만료(401) → **모달**. 사용자가 따로 조치해야 하는 안내라 한 번 읽고 닫게 한다.
 */
export function LoginPage({
  /** 401/403 으로 쫓겨났을 때의 안내. 모달로 띄운다. */
  notice,
  /** 안내 모달을 닫는다. */
  onDismissNotice,
  /** 쓸 수 있는 로그인 수단. DB(`auth_config`)에서 온다 — 자세한 건 shared/authGate.ts. */
  providers,
}: {
  notice: UnauthorizedInfo | null
  onDismissNotice: () => void
  providers: string[]
}) {
  const [error, setError] = useState<string | null>(null)
  /** 진행 중인 프로바이더명. 하나가 도는 동안 나머지도 잠근다. */
  const [busy, setBusy] = useState<string | null>(null)

  // 설정 오류는 무엇을 해도 안 되므로 다른 메시지보다 우선한다.
  const shown = supabaseError ?? error

  async function oauth(provider: string) {
    if (!sb) return setError('Supabase 설정이 필요합니다.')
    setError(null)
    setBusy(provider)
    // 성공하면 곧 프로바이더로 리다이렉트된다 — busy 를 풀지 않아야 그 사이 두 번 눌리지 않는다.
    try {
      const { error } = await sb.auth.signInWithOAuth({
        provider,
        options: { redirectTo: window.location.origin + '/admin/index.html' },
      })
      if (error) {
        setError(error.message)
        setBusy(null)
      }
    } catch (e) {
      setError('OAuth 로그인 실패: ' + (e instanceof Error ? e.message : String(e)))
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
            <span className="sep">$</span> login --provider
            <span className="cursor">_</span>
          </div>

          {shown && <div className="alert alert-danger">{shown}</div>}

          <div className="oauth-list" style={{ marginTop: 18 }}>
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

          {/* App 이 이 경우를 설정 화면으로 보내므로 평소에는 안 보인다. 마지막 방어선이다. */}
          {providers.length === 0 && (
            <div className="alert alert-warning" style={{ marginTop: 18 }}>
              로그인 수단이 설정되지 않았습니다.
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
