import { isPreview } from './env'
import { accessToken } from './supabase'

/**
 * 서버 API 래퍼. 바닐라 api / tableApi / adminApi 를 옮긴 것이다.
 *
 * 토큰을 전역 변수로 들고 있지 않고 **매 호출마다 세션에서 읽는다**.
 * supabase-js 가 토큰 갱신을 알아서 하므로, 이렇게 하면 갱신을 따라 옮겨 적을 필요가 없다
 * (바닐라는 onAuthStateChange 에서 `token` 을 수동으로 갱신했다).
 */

const CONFIG_BASE = '/admin/api/config'
const TABLE_BASE = '/admin/api/table'
const ADMIN_BASE = '/admin/api/admins'

declare global {
  interface Window {
    /** 프리뷰 모드 mock (index.html 하단). 실제 서버 대신 응답한다. */
    __previewApi?: (path: string, method?: string, body?: unknown) => Promise<unknown>
    __previewTableApi?: (path: string, method?: string, body?: unknown) => Promise<unknown>
    __previewAdminApi?: (path: string, method?: string, body?: unknown) => Promise<unknown>
  }
}

// ── 권한/세션 만료 알림 ────────────────────────────────────────
// 값이 아니라 구독자 집합이다 — 어느 화면에서 API 를 부르든 App 하나가 로그인 복귀를 처리한다.

export interface UnauthorizedInfo {
  /** 401 = 세션 만료, 403 = 관리자 권한 없음. 안내 제목이 갈린다. */
  status: 401 | 403
  message: string
}

type UnauthorizedListener = (info: UnauthorizedInfo) => void
const unauthorizedListeners = new Set<UnauthorizedListener>()

/** 401/403 을 만나면 알림을 받는다. 반환값은 구독 해제 함수. */
export function onUnauthorized(f: UnauthorizedListener): () => void {
  unauthorizedListeners.add(f)
  return () => {
    unauthorizedListeners.delete(f)
  }
}

const MSG_401 = '로그인 세션이 만료되었습니다. 다시 로그인하세요.'
const MSG_403 =
  '관리자 권한이 없습니다.\n첫 번째 가입자는 자동으로 관리자가 됩니다.\n이미 관리자가 있다면, 기존 관리자에게 승인을 요청하세요.'

async function request<T>(
  base: string,
  path: string,
  method: string,
  body: unknown,
  opts: { handleAuth: boolean },
): Promise<T> {
  const token = await accessToken()
  const init: RequestInit = {
    method,
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
  }
  if (body) init.body = JSON.stringify(body)

  const res = await fetch(`${base}${path}`, init)

  if (opts.handleAuth && (res.status === 401 || res.status === 403)) {
    const status = res.status as 401 | 403
    const message = status === 401 ? MSG_401 : MSG_403
    unauthorizedListeners.forEach((f) => f({ status, message }))
    throw new Error(message)
  }
  if (!res.ok) {
    const err = (await res.json().catch(() => ({}))) as { error?: string }
    throw new Error(err.error || `HTTP ${res.status}`)
  }
  // DELETE 는 본문이 없다
  if (method === 'DELETE') return null as T
  return (await res.json()) as T
}

/** Config API. **401/403 시 로그인 화면 복귀**를 유발한다. */
export function configApi<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  if (isPreview()) return window.__previewApi!(path, method, body) as Promise<T>
  return request<T>(CONFIG_BASE, path, method, body, { handleAuth: true })
}

/** Table API. 플레이어 조회는 `/../player/{id}` 로 상위 경로를 탄다. */
export function tableApi<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  if (isPreview()) return window.__previewTableApi!(path, method, body) as Promise<T>
  return request<T>(TABLE_BASE, path, method, body, { handleAuth: false })
}

/** 관리자 API. */
export function adminApi<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  if (isPreview()) return window.__previewAdminApi!(path, method, body) as Promise<T>
  return request<T>(ADMIN_BASE, path, method, body, { handleAuth: false })
}

/** 인증 토큰이 필요한 원시 fetch (Export 의 blob 다운로드용). */
export async function authFetch(url: string): Promise<Response> {
  const token = await accessToken()
  return fetch(url, { headers: { Authorization: `Bearer ${token}` } })
}
