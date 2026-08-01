import { useEffect, useState } from 'react'
import { sb, type SupabaseSession } from '../../shared/supabase'

interface SessionState {
  session: SupabaseSession | null
  /** 첫 세션 확인이 끝났는가. false 동안은 화면을 그리지 않아 로그인 폼 깜빡임을 막는다. */
  ready: boolean
}

/**
 * Supabase 세션 구독. 로그인·로그아웃·토큰 갱신이 전부 여기로 모인다 —
 * App 은 이 값 하나로 로그인 화면과 어드민을 가른다.
 *
 * 토큰 갱신 실패(SIGNED_OUT)도 같은 길로 떨어지므로 만료 처리를 따로 두지 않는다.
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
