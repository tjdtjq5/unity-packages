import { useEffect, useState } from 'react'
import { onUnauthorized, type UnauthorizedInfo } from './shared/api'
import { isPreview } from './shared/env'
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

  // 첫 세션 확인 전에는 아무것도 그리지 않는다 — 로그인 폼이 깜빡였다 사라지는 것을 막는다.
  if (!ready && !preview) return null

  if (preview || (session && !kickedOut)) {
    return (
      <div id="admin-page" className="page active">
        <Shell
          email={preview ? 'preview@mock.local' : (session?.user?.email ?? '')}
          onLogout={() => void sb?.auth.signOut()}
        />
      </div>
    )
  }

  return <LoginPage notice={kickedOut} onDismissNotice={() => setKickedOut(null)} />
}
