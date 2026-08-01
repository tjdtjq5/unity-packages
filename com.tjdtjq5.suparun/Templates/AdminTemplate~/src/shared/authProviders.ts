import { bridge } from './bridge'

/**
 * OAuth 프로바이더 설정. 로컬 브리지를 거친다(shared/bridge.ts 참조) — PAT 가 필요한 호출이다.
 */

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

export async function loadProviders(): Promise<ProviderState[]> {
  const cfg = await bridge.get<Record<string, unknown>>('/auth-config')
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
  await bridge.post('/auth-config', input)
}
