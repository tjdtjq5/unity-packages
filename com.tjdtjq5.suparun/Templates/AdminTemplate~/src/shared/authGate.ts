import { env } from './env'
import { loadMeta } from './meta'

/**
 * 로그인을 요구할 것인가 — **단계적 잠금**.
 *
 * 초기 세팅 중에는 로그인 수단 자체가 없다(Google 을 아직 안 켰으므로). 그 구간에는 열어 두고,
 * 로그인 프로바이더가 켜지는 순간부터 잠근다.
 *
 * 열려 있는 동안에도 **쓰기는 막혀 있다** — anon key 로 동작하고 RLS 의 쓰기 정책이
 * `is_admin()` 을 요구하기 때문이다. 즉 이 구간은 "읽기만 되는 상태"지 무제한이 아니다.
 * 그래도 공개 URL 이 열려 있는 것은 사실이므로 화면이 그 사실을 계속 알린다.
 *
 * 판정 근거를 DB(`suparun_meta.auth_config`)에서 읽는 이유: 예전처럼 빌드에 구워 두면
 * 프로바이더를 켠 뒤 **재배포해야** 잠금이 걸린다. 그 사이가 그대로 구멍이 된다.
 */

export interface AuthGate {
  /** 로그인해야 들어올 수 있는가. */
  locked: boolean
  /** 쓸 수 있는 로그인 수단. 비어 있으면 아직 아무것도 안 켠 것이다. */
  providers: string[]
}

export async function loadAuthGate(): Promise<AuthGate> {
  let providers: string[] = []
  try {
    const meta = await loadMeta(['auth_config'])
    const cfg = meta.auth_config as { providers?: string[] } | undefined
    providers = cfg?.providers ?? []
  } catch {
    // meta 를 못 읽으면(정책·네트워크) 빌드에 구워진 값으로 물러난다.
    providers = []
  }

  // DB 에 아직 없으면 빌드 치환값을 쓴다 — 옛 배포가 갑자기 열리지 않게.
  if (providers.length === 0) providers = env().authProviders ?? []

  // Guest/GPGS/GameCenter 는 게임 클라이언트용이라 웹에서 누를 수 없다.
  // 이걸 세면 로그인 화면에 버튼이 하나도 없는 채로 잠겨 아무도 못 들어온다.
  const NOT_WEB = new Set(['guest', 'gpgs', 'gamecenter'])
  const usable = providers.filter((p) => p && !NOT_WEB.has(p.toLowerCase()))

  return { locked: usable.length > 0, providers: usable }
}
