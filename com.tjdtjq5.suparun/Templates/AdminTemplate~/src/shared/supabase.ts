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
  // 이메일/비밀번호 로그인은 쓰지 않는다 — 어드민 진입은 OAuth 전용이다.
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

// ── PostgREST 쿼리 (ADR-0004) ──────────────────────────────
// 어드민이 서버를 거치지 않고 직접 붙는다. 체이닝 빌더라 실제 타입은 훨씬 복잡한데,
// 여기서도 **쓰는 만큼만** 선언한다.

export interface PostgrestResult<T> {
  data: T | null
  error: { message: string; code?: string } | null
  /** `count: 'exact'` 를 요청했을 때만 채워진다. */
  count?: number | null
}

/** PromiseLike 라 그대로 await 할 수 있다. */
export interface PostgrestFilter<T> extends PromiseLike<PostgrestResult<T>> {
  eq(column: string, value: unknown): PostgrestFilter<T>
  neq(column: string, value: unknown): PostgrestFilter<T>
  in(column: string, values: unknown[]): PostgrestFilter<T>
  gt(column: string, value: unknown): PostgrestFilter<T>
  gte(column: string, value: unknown): PostgrestFilter<T>
  lt(column: string, value: unknown): PostgrestFilter<T>
  lte(column: string, value: unknown): PostgrestFilter<T>
  like(column: string, pattern: string): PostgrestFilter<T>
  ilike(column: string, pattern: string): PostgrestFilter<T>
  not(column: string, op: string, value: unknown): PostgrestFilter<T>
  order(column: string, opts?: { ascending?: boolean }): PostgrestFilter<T>
  limit(count: number): PostgrestFilter<T>
  range(from: number, to: number): PostgrestFilter<T>
  select(columns?: string): PostgrestFilter<T>
  single(): PostgrestFilter<T>
  maybeSingle(): PostgrestFilter<T>
}

export interface PostgrestTable {
  select<T = unknown[]>(
    columns?: string,
    opts?: { count?: 'exact' | 'planned' | 'estimated'; head?: boolean },
  ): PostgrestFilter<T>
  insert<T = unknown[]>(rows: unknown): PostgrestFilter<T>
  update<T = unknown[]>(patch: unknown): PostgrestFilter<T>
  upsert<T = unknown[]>(rows: unknown, opts?: { onConflict?: string }): PostgrestFilter<T>
  delete<T = unknown[]>(): PostgrestFilter<T>
}

export interface SupabaseClient {
  auth: SupabaseAuth
  from(table: string): PostgrestTable
  rpc<T = unknown>(fn: string, args?: Record<string, unknown>): PostgrestFilter<T>
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
