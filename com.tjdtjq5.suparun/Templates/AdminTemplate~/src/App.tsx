import { useEffect, useState } from 'react'
import { onUnauthorized, type UnauthorizedInfo } from './shared/api'
import { needsSetup, setup, type SetupState } from './shared/bridge'
import { isPreview } from './shared/env'
import { FullScreenLoader } from './shared/Spinner'
import { sb, supabaseError } from './shared/supabase'
import { OnboardingPage } from './features/setup/OnboardingPage'
import { Shell } from './features/shell/Shell'

/**
 * 앱 루트 — **사람 로그인이 없다.**
 *
 * 이 페이지는 로컬 브리지 전용이고, 여기까지 온 사람은 이미 브리지 토큰(=PAT 대행 전권)을
 * 쥐고 있다. 로그인 화면은 전권자에게 또 세운 문이었다 — 접근 통제는 Supabase 조직
 * 멤버십(각자 PAT)이 맡고, RLS 가 요구하는 세션은 브리지가 **기계 계정**으로 만들어
 * `window.__SUPARUN_SESSION` 에 꽂아 준다(SupaRunMachineAccount).
 *
 * 화면은 셋으로 갈린다:
 *   셋업 구간(PAT·프로젝트·스키마 빈칸) → 온보딩
 *   세션 주입 성공                      → 어드민
 *   세션 없음/실패                      → 원인 안내 (원인은 Unity Console 에 있다)
 */

declare global {
  interface Window {
    /** 브리지가 서빙할 때 꽂는 기계 계정 세션. 없으면 로그인 실패다. */
    __SUPARUN_SESSION?: { access_token: string; refresh_token: string }
  }
}

export function App() {
  const preview = isPreview()

  /** 401/403 으로 쫓겨난 상태. 기계 세션은 자동 갱신되므로 드문 일 — 새로고침을 안내한다. */
  const [kickedOut, setKickedOut] = useState<UnauthorizedInfo | null>(null)
  useEffect(() => onUnauthorized(setKickedOut), [])

  // 셋업이 어디까지 됐는가. 세션보다 먼저 본다 — 프로젝트가 없으면 세션도 없다.
  const [setupState, setSetupState] = useState<SetupState | null | undefined>(undefined)
  useEffect(() => {
    if (preview) return
    void setup.state().then(setSetupState).catch(() => setSetupState(null))
  }, [preview])

  // 주입된 세션을 supabase-js 에 싣는다. 이후 갱신(refresh)은 클라이언트가 알아서 한다.
  const [auth, setAuth] = useState<'boot' | 'ready' | 'failed'>('boot')
  const [email, setEmail] = useState('')
  useEffect(() => {
    if (preview) return
    const injected = window.__SUPARUN_SESSION
    const client = sb
    if (!client || !injected) {
      setAuth('failed')
      return
    }
    let alive = true
    void client.auth
      .setSession(injected)
      .then(async ({ error }) => {
        if (!alive) return
        if (error) {
          setAuth('failed')
          return
        }
        const { data } = await client.auth.getSession()
        if (!alive) return
        setEmail(data.session?.user?.email ?? '')
        setAuth('ready')
      })
      .catch(() => alive && setAuth('failed'))
    return () => {
      alive = false
    }
  }, [preview])

  if (preview) {
    return (
      <div id="admin-page" className="page active">
        <Shell email="preview@mock.local" />
      </div>
    )
  }

  if (setupState === undefined) return <FullScreenLoader label="설정 확인 중" />

  // 브리지가 안 잡히면 Unity 가 꺼진 것이다. 이 페이지 자체를 브리지가 내보내므로
  // 열려 있다면 살아 있었다는 뜻이고, 지금 안 잡히면 그 사이에 닫힌 것이다.
  if (setupState === null) {
    return (
      <Notice tone="warning">
        <b>Unity 가 응답하지 않습니다.</b>
        <br />
        어드민은 Unity 안의 로컬 서버가 내보냅니다. Unity 를 켠 뒤 새로고침하세요.
      </Notice>
    )
  }

  // 빈칸이 하나라도 있으면 온보딩. 플래그가 아니라 사실로 판정하므로,
  // 중단했다 돌아와도 그 지점에서 이어진다.
  if (needsSetup(setupState)) return <OnboardingPage />

  if (auth === 'boot') return <FullScreenLoader label="세션 준비 중" />

  if (auth === 'failed' || kickedOut) {
    return (
      <Notice tone="danger">
        <b>{kickedOut ? '세션이 거부되었습니다.' : '기계 계정 세션을 받지 못했습니다.'}</b>
        <br />
        {kickedOut?.message ?? supabaseError ?? 'Unity Console 의 [SupaRun:Auth] 로그를 확인하세요.'}
        <div className="action-line" style={{ marginTop: 12 }}>
          <button className="btn-terminal" onClick={() => window.location.reload()}>
            [새로고침]
          </button>
        </div>
      </Notice>
    )
  }

  return (
    <div id="admin-page" className="page active">
      <Shell email={email} />
    </div>
  )
}

function Notice({ tone, children }: { tone: 'warning' | 'danger'; children: React.ReactNode }) {
  return (
    <div className="page page-center">
      <div className="terminal-window">
        <div className="terminal-body">
          <div className={`alert alert-${tone}`}>{children}</div>
        </div>
      </div>
    </div>
  )
}
