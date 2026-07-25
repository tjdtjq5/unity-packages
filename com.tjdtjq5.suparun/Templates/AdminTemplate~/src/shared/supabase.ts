import { env } from './env'

/**
 * Supabase 클라이언트. **CDN UMD 전역**(`window.supabase`)을 쓴다 —
 * npm 의존성으로 넣지 않는 것은 번들 크기 때문이 아니라, 어드민 페이지가
 * CDN 스크립트 4종(bootstrap/supabase/chart/sortable)에 이미 의존하고 있어서 계약을 맞추는 쪽이다.
 *
 * 타입은 실제로 쓰는 만큼만 선언한다. `@supabase/supabase-js` 를 devDependency 로 끌어오면
 * 런타임(UMD)과 타입(npm) 버전이 어긋날 때 조용히 틀리는 쪽이 더 위험하다.
 */

export interface SupabaseUser {
  email?: string
}

export interface SupabaseSession {
  access_token: string
  user?: SupabaseUser
}

interface AuthError {
  message: string
}

export type AuthEvent =
  | 'INITIAL_SESSION'
  | 'SIGNED_IN'
  | 'SIGNED_OUT'
  | 'TOKEN_REFRESHED'
  | 'USER_UPDATED'
  | 'PASSWORD_RECOVERY'

interface SupabaseAuth {
  signInWithPassword(c: { email: string; password: string }): Promise<{ error: AuthError | null }>
  signUp(c: {
    email: string
    password: string
  }): Promise<{ data: { session: SupabaseSession | null }; error: AuthError | null }>
  signInWithOAuth(o: {
    provider: string
    options?: { redirectTo?: string }
  }): Promise<{ error: AuthError | null }>
  signOut(): Promise<unknown>
  getSession(): Promise<{ data: { session: SupabaseSession | null } }>
  onAuthStateChange(cb: (event: AuthEvent, session: SupabaseSession | null) => void): {
    data: { subscription: { unsubscribe(): void } }
  }
}

export interface SupabaseClient {
  auth: SupabaseAuth
}

declare global {
  interface Window {
    supabase?: { createClient(url: string, key: string): SupabaseClient }
  }
}

function create(): { client: SupabaseClient | null; error: string | null } {
  const e = env()
  try {
    if (!window.supabase) throw new Error('supabase-js CDN 로드 실패')
    if (!e.supabaseUrl || e.supabaseUrl.includes('{{'))
      throw new Error(
        'SUPABASE_URL이 설정되지 않았습니다. SupaRun Dashboard에서 Supabase를 연결하세요.',
      )
    if (!e.supabaseAnonKey || e.supabaseAnonKey.includes('{{'))
      throw new Error('SUPABASE_ANON_KEY가 설정되지 않았습니다.')
    return { client: window.supabase.createClient(e.supabaseUrl, e.supabaseAnonKey), error: null }
  } catch (err) {
    return { client: null, error: err instanceof Error ? err.message : String(err) }
  }
}

const created = create()

/** 설정이 잘못되면 null 이다 — 로그인 화면이 `supabaseError` 를 대신 보여준다. */
export const sb: SupabaseClient | null = created.client
export const supabaseError: string | null = created.error

/** 현재 access token. 세션이 없으면 빈 문자열. */
export async function accessToken(): Promise<string> {
  if (!sb) return ''
  const { data } = await sb.auth.getSession()
  return data.session?.access_token ?? ''
}
