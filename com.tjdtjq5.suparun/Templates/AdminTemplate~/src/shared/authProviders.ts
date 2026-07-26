import { accessToken } from './supabase'

/**
 * OAuth 프로바이더 설정 — **서버를 거쳐** Supabase Management API 를 부른다.
 *
 * 브라우저가 직접 못 부르는 이유 두 가지:
 *   1) `api.supabase.com` 의 CORS 가 `https://supabase.com` 오리진만 허용한다(실측 확인).
 *   2) PAT 는 Supabase 계정 전체의 마스터키라 브라우저에 내려보내면 안 된다.
 *
 * 그래서 같은 오리진의 `/admin/api/supabase/auth-config` 로 보낸다. 인가는 서버의
 * `/admin/api` 미들웨어가 하고(role='admin'), PAT 는 서버가 DB 에서 꺼내 쓴다.
 */

const BASE = '/admin/api/supabase/auth-config'

/** 프로바이더 하나의 현재 상태. secret 은 **절대 돌아오지 않는다.** */
export interface ProviderState {
  key: string
  label: string
  enabled: boolean
  clientId: string
}

/** 어드민에서 켤 수 있는 것들. 게임 전용(Guest/GPGS/GameCenter)은 여기 없다. */
const KNOWN: { key: string; label: string; field: string }[] = [
  { key: 'google', label: 'Google', field: 'external_google' },
  { key: 'apple', label: 'Apple', field: 'external_apple' },
  { key: 'github', label: 'GitHub', field: 'external_github' },
  { key: 'kakao', label: 'Kakao', field: 'external_kakao' },
  { key: 'discord', label: 'Discord', field: 'external_discord' },
]

async function headers(): Promise<HeadersInit> {
  return {
    Authorization: `Bearer ${await accessToken()}`,
    'Content-Type': 'application/json',
  }
}

async function fail(res: Response): Promise<never> {
  const body = (await res.json().catch(() => ({}))) as { error?: string; message?: string }
  throw new Error(body.error || body.message || `HTTP ${res.status}`)
}

export async function loadProviders(): Promise<ProviderState[]> {
  const res = await fetch(BASE, { headers: await headers() })
  if (!res.ok) await fail(res)

  const cfg = (await res.json()) as Record<string, unknown>
  return KNOWN.map((p) => ({
    key: p.key,
    label: p.label,
    enabled: cfg[`${p.field}_enabled`] === true,
    clientId: (cfg[`${p.field}_client_id`] as string) ?? '',
  }))
}

/**
 * 프로바이더를 켜거나 끈다.
 *
 * `secret` 을 비워 보내면 **서버가 건드리지 않는다** — 화면에 secret 을 안 보여주므로
 * "그대로 두기" 가 기본이어야 한다. 안 그러면 Client ID 만 고치려다 secret 을 지우게 된다.
 */
export async function saveProvider(input: {
  provider: string
  enabled: boolean
  clientId: string
  secret: string
}): Promise<void> {
  const res = await fetch(BASE, {
    method: 'POST',
    headers: await headers(),
    body: JSON.stringify(input),
  })
  if (!res.ok) await fail(res)
}
