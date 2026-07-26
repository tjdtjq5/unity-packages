import { env } from './env'
import { accessToken } from './supabase'

/**
 * `suparun-admin` Edge Function 호출.
 *
 * 왜 여기로 모이나: 어드민은 `api.supabase.com` 을 직접 못 부른다. 그쪽 CORS 가
 * `https://supabase.com` 오리진만 허용하기 때문이다(실측 확인). PAT 도 계정 마스터키라
 * 브라우저에 내려보낼 수 없다. 그래서 누군가 대신 불러야 하는데, 그 자리를 Cloud Run 이
 * 맡으면 **첫 배포 전에는 존재하지 않는다** — 배포에 필요한 값을 어드민에서 받으려는데
 * 어드민을 띄울 서버가 없는 순환이 생긴다.
 *
 * Edge Function 은 Supabase 프로젝트가 생기는 순간 존재해서 그 순환을 끊는다.
 * 그래서 통로를 여기 하나로 둔다 — 기능마다 통로가 다르면 "이건 왜 Unity 가 꺼져 있으면
 * 안 되지?" 를 매번 따로 겪게 된다.
 */

function base(): string {
  const url = env().supabaseUrl?.replace(/\/$/, '') ?? ''
  return `${url}/functions/v1/suparun-admin`
}

/** 로그인 전에는 anon key 로 부른다 — /ping 처럼 인증이 필요 없는 경로가 있다. */
async function authHeader(): Promise<string> {
  const token = await accessToken()
  return `Bearer ${token || env().supabaseAnonKey}`
}

async function call<T>(path: string, method: string, body?: unknown): Promise<T> {
  const res = await fetch(`${base()}${path}`, {
    method,
    headers: {
      apikey: env().supabaseAnonKey,
      Authorization: await authHeader(),
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const text = await res.text()
  let parsed: unknown = null
  try {
    parsed = text ? JSON.parse(text) : null
  } catch {
    // 함수가 죽으면 JSON 이 아닌 본문이 온다. 그 원문을 그대로 보여주는 편이 낫다.
    if (!res.ok) throw new Error(text || `HTTP ${res.status}`)
  }

  if (!res.ok) {
    const err = (parsed ?? {}) as { error?: string; message?: string }
    throw new Error(err.error || err.message || `HTTP ${res.status}`)
  }
  return parsed as T
}

export const edgeFn = {
  get: <T>(path: string) => call<T>(path, 'GET'),
  post: <T>(path: string, body?: unknown) => call<T>(path, 'POST', body ?? {}),
  del: <T>(path: string) => call<T>(path, 'DELETE'),
}

/** 함수가 배포되어 응답하는가. 실패해도 던지지 않는다 — 화면이 "아직 없음"을 그려야 한다. */
export async function pingEdgeFn(): Promise<{ ok: boolean; version?: number } | null> {
  try {
    return await edgeFn.get<{ ok: boolean; version: number }>('/ping')
  } catch {
    return null
  }
}

export interface WhoAmI {
  userId: string | null
  email: string | null
  isAdmin: boolean
  /** 아직 아무도 관리자가 아니다. 첫 로그인이 주인이 되는 구간. */
  unclaimed: boolean
  /** 로그인 수단이 하나도 없다 = 로그인 없이 로그인 수단을 켤 수 있는 구간. */
  setupOpen: boolean
  /** 지금 켜져 있는 웹 로그인 수단. Supabase 의 실제 설정에서 온다(사본이 아니다). */
  providers: string[]
}

/** 실패해도 던지지 않는다 — 함수가 아직 없을 수도 있고, 그때도 화면은 떠야 한다. */
export async function whoAmI(): Promise<WhoAmI | null> {
  try {
    return await edgeFn.get<WhoAmI>('/whoami')
  } catch {
    return null
  }
}
