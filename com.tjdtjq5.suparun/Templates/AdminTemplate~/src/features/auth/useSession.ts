import { useEffect, useState } from 'react'
import { sb, type SupabaseSession } from '../../shared/supabase'

interface SessionState {
  session: SupabaseSession | null
  /** 첫 세션 확인이 끝났는가. false 동안은 화면을 그리지 않아 로그인 폼 깜빡임을 막는다. */
  ready: boolean
}

/**
 * Supabase 세션 구독. 바닐라 onAuthStateChange 블록을 대체한다.
 *
 * 바닐라는 `TOKEN_REFRESHED` 를 early return 으로 걸러 어드민 재진입을 막았다.
 * 여기서는 세션 객체만 갱신되고 Shell 은 그대로 있으므로 그 가드가 필요 없다.
 */
export function useSession(): SessionState {
  const [state, setState] = useState<SessionState>({ session: null, ready: false })

  useEffect(() => {
    if (!sb) {
      setState({ session: null, ready: true })
      return
    }
    const { data } = sb.auth.onAuthStateChange((_event, session) => {
      setState({ session: session?.access_token ? session : null, ready: true })
    })
    return () => data.subscription.unsubscribe()
  }, [])

  return state
}
