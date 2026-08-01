/**
 * 로컬 브리지(Unity) 호출.
 *
 * 왜 여기로 모이나: 브라우저는 `api.supabase.com` 을 직접 못 부른다 — 그쪽 CORS 가
 * `https://supabase.com` 오리진만 허용한다. PAT 도 계정 마스터키라 브라우저에 내려보낼 수 없다.
 * 그래서 PAT 를 쥔 Unity 가 대신 부른다.
 *
 * 한때 이 자리를 `suparun-admin` Edge Function 이 맡았다. 근거는 "Unity 가 꺼져 있어도 웹만
 * 열어 보는 사람" 이었는데, **어드민 자체를 이 브리지가 서빙하게 되면서 그 전제가 사라졌다.**
 * Unity 가 없으면 이 페이지도 없다.
 *
 * 접속 정보는 브리지가 페이지를 내보낼 때 `window.__SUPARUN_BRIDGE` 로 꽂아 준다.
 * 예전처럼 DB(`suparun_meta`)에서 읽으면 그 표가 `public_read` 라 anon key 만으로 토큰이 샌다 —
 * 게임 빌드에서 뽑히는 키다.
 */

declare global {
  interface Window {
    __SUPARUN_BRIDGE?: { port: number; token: string }
  }
}

function endpoint(): { port: number; token: string } {
  const b = window.__SUPARUN_BRIDGE
  if (!b) throw new Error('브리지 접속 정보가 없습니다. Unity 대시보드에서 어드민을 여세요.')
  return b
}

async function call<T>(path: string, method: string, body?: unknown): Promise<T> {
  const b = endpoint()
  const res = await fetch(`http://127.0.0.1:${b.port}${path}`, {
    method,
    headers: {
      'x-bridge-token': b.token,
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const text = await res.text()
  let parsed: unknown = null
  try {
    parsed = text ? JSON.parse(text) : null
  } catch {
    // 브리지가 죽으면 JSON 이 아닌 본문이 온다. 그 원문을 그대로 보여주는 편이 낫다.
    if (!res.ok) throw new Error(text || `HTTP ${res.status}`)
  }

  if (!res.ok) {
    const err = (parsed ?? {}) as { error?: string; hint?: string }
    throw new Error([err.error, err.hint].filter(Boolean).join(' — ') || `HTTP ${res.status}`)
  }
  return parsed as T
}

export const bridge = {
  get: <T>(path: string) => call<T>(path, 'GET'),
  post: <T>(path: string, body?: unknown) => call<T>(path, 'POST', body ?? {}),
  del: <T>(path: string) => call<T>(path, 'DELETE'),
}

/**
 * 브리지가 있는 어드민인가 = **로컬(Unity 가 서빙)인가.**
 * 배포된 어드민에는 이 값이 안 꽂힌다 — 전환 입장·셋업·슬롯 조작처럼
 * Unity 의 손이 필요한 UI 는 이걸로 갈린다.
 */
export function bridgeAvailable(): boolean {
  return !!window.__SUPARUN_BRIDGE
}

/** Unity 가 살아 있는가. 실패해도 던지지 않는다 — 화면이 그 상태를 그려야 한다. */
export async function pingBridge(): Promise<{ ok: boolean } | null> {
  try {
    return await bridge.get<{ ok: boolean }>('/ping')
  } catch {
    return null
  }
}

/**
 * 셋업이 어디까지 됐는가.
 *
 * **플래그가 아니라 사실이다.** "첫 실행" 이라는 표시를 두지 않는 이유: PAT 가 만료되고,
 * 새 환경을 만들면 스키마가 다시 없고, 팀원이 클론하면 토큰이 없다. 플래그를 두면
 * 그것과 사실이 어긋나는 순간이 반드시 온다.
 */
export interface SetupState {
  hasPat: boolean
  hasProject: boolean
  projectRef: string
  schemaReady: boolean
  initRunning: boolean
  initError: string | null
}

export const setup = {
  state: () => bridge.get<SetupState>('/setup/state'),
  /** 토큰을 넣는다. 저장 전에 실제로 통하는지 확인하므로, 성공하면 프로젝트 수가 돌아온다. */
  savePat: (pat: string) => bridge.post<{ projects: number }>('/setup/pat', { pat }),
  projects: () => bridge.get<{ projects: { ref: string; name: string; status: string; region: string }[] }>('/projects'),
  createProject: (name: string, region?: string) =>
    bridge.post<{ ref: string; name: string; status: string }>('/projects', { name, region }),
  /** `env` 를 주면 그 슬롯에 붙인다. 안 주면 편집 환경 — 온보딩은 그 형태다. */
  chooseProject: (ref: string, env?: string) =>
    bridge.post<{ ref: string }>('/setup/project', { ref, env }),
  init: () => bridge.post<{ started: boolean }>('/setup/init'),
}

/** 아직 넘어야 할 것이 남았는가. 하나라도 비면 온보딩이 뜬다. */
export function needsSetup(s: SetupState): boolean {
  return !s.hasPat || !s.hasProject || !s.schemaReady
}

// whoAmI 는 없다 — 신원 판정이 통째로 사라졌다. 세션은 브리지가 기계 계정으로 만들어
// 주입하고(App 참조), 그 계정의 admin_user 등록도 브리지가 스스로 한다.
