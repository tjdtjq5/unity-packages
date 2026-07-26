import { useEffect, useState } from 'react'
import { onUnauthorized, type UnauthorizedInfo } from './shared/api'
import { loadAuthGate, type AuthGate } from './shared/authGate'
import { isPreview } from './shared/env'
import { FullScreenLoader } from './shared/Spinner'
import { sb } from './shared/supabase'
import { LoginPage } from './features/auth/LoginPage'
import { useSession } from './features/auth/useSession'
import { Shell } from './features/shell/Shell'

/**
 * 앱 루트 — 로그인 화면과 어드민 껍데기 중 하나를 고른다.
 * 바닐라 showAdmin / backToLogin 이 하던 화면 전환이 여기 조건 하나로 줄었다.
 */
export function App() {
  const { session, ready } = useSession()
  /** 401/403 으로 쫓겨난 상태. 세션 자체는 살아 있을 수 있다(권한 부족). */
  const [kickedOut, setKickedOut] = useState<UnauthorizedInfo | null>(null)

  useEffect(() => onUnauthorized(setKickedOut), [])

  // 거부 상태는 **새 토큰이 실제로 발급됐을 때만** 푼다.
  // "로그인 버튼을 눌렀을 때" 풀면 아직 이전 세션인 채로 어드민이 다시 떠서
  // 같은 403 을 한 번 더 맞고, 그 사이 도착한 정상 로그인까지 거부 상태로 덮인다.
  const token = session?.access_token
  useEffect(() => {
    if (token) setKickedOut(null)
  }, [token])

  const preview = isPreview()

  // 로그인을 요구할지는 DB 가 정한다. 로그인 수단을 아직 안 켠 초기 세팅 구간에는
  // 요구할 수단 자체가 없으므로 열어 둔다. 자세한 근거는 shared/authGate.ts 참조.
  const [gate, setGate] = useState<AuthGate | null>(null)
  useEffect(() => {
    let alive = true
    void loadAuthGate().then((g) => {
      if (alive) setGate(g)
    })
    return () => {
      alive = false
    }
  }, [])

  // 첫 세션 확인 전에는 로그인 폼도 어드민도 그리지 않는다 — 폼이 깜빡였다 사라지는 것을 막는다.
  // 대신 로더를 세운다. 예전엔 null 이라 이 구간이 통째로 빈 검은 화면이었다.
  if ((!ready || !gate) && !preview) return <FullScreenLoader label="세션 확인 중" />

  const signedIn = !!session && !kickedOut
  // 잠기지 않았으면 로그인 없이 들어간다. 그래도 쓰기는 RLS 가 막는다(anon 이므로).
  const open = gate ? !gate.locked : false

  if (preview || signedIn || open) {
    return (
      <div id="admin-page" className="page active">
        <Shell
          email={preview ? 'preview@mock.local' : (session?.user?.email ?? '')}
          onLogout={() => void sb?.auth.signOut()}
          /** 로그인 없이 열려 있는 상태. 껍데기가 그 사실을 계속 알린다. */
          unlocked={!preview && !signedIn && open}
        />
      </div>
    )
  }

  return (
    <LoginPage
      notice={kickedOut}
      onDismissNotice={() => setKickedOut(null)}
      providers={gate?.providers ?? []}
    />
  )
}
