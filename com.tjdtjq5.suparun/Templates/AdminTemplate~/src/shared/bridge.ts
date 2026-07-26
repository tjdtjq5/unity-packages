import { loadMeta } from './meta'

/**
 * 로컬 Unity 브리지.
 *
 * PAT 가 필요한 일(프로젝트 생성·삭제·복구)은 브라우저가 직접 못 한다 — PAT 를 로컬에만 두기로 했기 때문이다.
 * 대신 **Unity 가 띄운 로컬 서버에 명령을 보낸다.** PAT 를 쥔 쪽은 끝까지 Unity 다.
 *
 * 부수 효과가 곧 안전장치다: **Unity 가 꺼져 있으면 이 기능들이 자동으로 잠긴다.**
 *
 * HTTPS 페이지에서 `http://127.0.0.1` 을 부르는 것은 실측으로 확인했다 —
 * 브라우저가 localhost 를 potentially trustworthy 로 취급해 Mixed Content 에서 예외로 둔다.
 * 서버 쪽이 CORS + `Access-Control-Allow-Private-Network` 를 함께 줘야 통과한다.
 */

export interface BridgeEndpoint {
  port: number
  token: string
  unity?: string
}

export interface BridgeProject {
  ref: string
  name: string
  status: string
  region: string
  url: string
}

/** 브리지가 응답하지 않을 때. 화면은 이걸 "Unity 꺼짐"으로 읽는다. */
export class BridgeOfflineError extends Error {
  constructor() {
    super('Unity 에디터가 실행 중이 아닙니다')
    this.name = 'BridgeOfflineError'
  }
}

let cached: BridgeEndpoint | null = null

/** 접속 정보는 Unity 가 `suparun_meta.bridge` 에 적어 둔다. 관리자만 읽는 자리다. */
async function endpoint(): Promise<BridgeEndpoint | null> {
  if (cached) return cached
  const meta = await loadMeta(['bridge'])
  const b = meta.bridge as BridgeEndpoint | undefined
  if (!b?.port || !b?.token) return null
  cached = b
  return b
}

/** 접속 정보를 다시 읽는다. 포트가 바뀌었을 때(다른 포트로 잡힘) 쓴다. */
export function invalidateBridge(): void {
  cached = null
}

async function call<T>(
  path: string,
  init: RequestInit = {},
  timeoutMs = 8000,
): Promise<T> {
  const ep = await endpoint()
  if (!ep) throw new BridgeOfflineError()

  // 타임아웃을 짧게 잡는다 — 꺼져 있을 때 사용자를 오래 기다리게 하지 않는다.
  const ctrl = new AbortController()
  const timer = setTimeout(() => ctrl.abort(), timeoutMs)
  try {
    const res = await fetch(`http://127.0.0.1:${ep.port}${path}`, {
      ...init,
      signal: ctrl.signal,
      headers: {
        'content-type': 'application/json',
        'x-bridge-token': ep.token,
        ...(init.headers ?? {}),
      },
    })
    const body = (await res.json().catch(() => ({}))) as Record<string, unknown>
    if (!res.ok) {
      const msg = (body.error as string) ?? `HTTP ${res.status}`
      const hint = body.hint as string | undefined
      throw new Error(hint ? `${msg}\n${hint}` : msg)
    }
    return body as T
  } catch (e) {
    // 연결 자체가 안 되면 Unity 가 꺼진 것으로 본다. 포트가 바뀌었을 수도 있어 캐시를 버린다.
    if (e instanceof DOMException && e.name === 'AbortError') {
      invalidateBridge()
      throw new BridgeOfflineError()
    }
    if (e instanceof TypeError) {
      invalidateBridge()
      throw new BridgeOfflineError()
    }
    throw e
  } finally {
    clearTimeout(timer)
  }
}

/** Unity 가 살아있는가. 토큰이 필요 없는 유일한 호출이다. */
export async function pingBridge(): Promise<{ unity: string; editor_env: string } | null> {
  try {
    return await call<{ unity: string; editor_env: string }>('/ping', { method: 'GET' }, 2500)
  } catch {
    return null
  }
}

export async function listProjects(): Promise<BridgeProject[]> {
  const r = await call<{ projects: BridgeProject[] }>('/projects', { method: 'GET' })
  return r.projects ?? []
}

export async function createProject(
  name: string,
  region?: string,
  plan?: string,
): Promise<BridgeProject> {
  return await call<BridgeProject>('/projects', {
    method: 'POST',
    body: JSON.stringify({ name, region, plan }),
  }, 30000)   // 생성 요청 자체는 빠르지만 여유를 준다
}

export async function deleteProject(projectRef: string): Promise<void> {
  await call(`/projects?ref=${encodeURIComponent(projectRef)}`, { method: 'DELETE' })
}

export async function restoreProject(projectRef: string): Promise<void> {
  await call(`/projects/restore?ref=${encodeURIComponent(projectRef)}`, { method: 'POST' }, 30000)
}

export async function availableRegions(): Promise<{ code: string; label: string }[]> {
  const r = await call<{ regions: { code: string; label: string }[] }>('/regions', { method: 'GET' })
  return r.regions ?? []
}
