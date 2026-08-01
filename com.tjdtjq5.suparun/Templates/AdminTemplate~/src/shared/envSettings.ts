import { sb } from './supabase'

/**
 * 이 환경의 설정. **어드민이 쓰고 Unity 가 읽는다.**
 *
 * `suparun_meta` 가 아니라 `suparun_env` 인 이유: 그쪽은 `public_read` 라 anon key 만 있으면
 * 누구나 읽는다. anon key 는 게임 빌드에서 뽑히므로 GCP 프로젝트·GitHub 레포가 사실상 공개된다.
 *
 * **'환경 공통' 설정은 두지 않는다.** 각 Supabase 프로젝트가 자기 값을 갖는다 — dev 어드민은
 * dev 것만 보고, prod 는 prod 어드민에서 고친다. 공통을 두면 "그 값을 어느 환경 DB 에 둘 것인가"
 * 가 풀리지 않는다(어드민은 자기 환경 DB 하나만 본다).
 */

export interface EnvSettings {
  name: string
  gcpProjectId: string
  gcpRegion: string
  gcpServiceName: string
  gcpMinInstances: string
  githubRepoName: string
  serverCaches: string
  /**
   * 켤 플랫폼 로그인. 쉼표로 잇는다(`Guest,GPGS`).
   *
   * Unity 가 **서버 코드를 생성할 때** 읽는다 — 여기서 고른 것만 인증 컨트롤러가 만들어진다
   * (DeployManager). 그래서 이 값을 바꾸면 다음 배포부터 반영된다.
   */
  platformAuth: string
}

/** 고를 수 있는 플랫폼. Unity 의 `SupaRunSettings.PlatformAuthKinds` 와 같은 문자열이어야 한다. */
export const PLATFORM_AUTH = [
  { id: 'Guest', label: '게스트', hint: '설치하자마자 바로 시작' },
  { id: 'GPGS', label: 'Google Play 게임즈', hint: 'Android' },
  { id: 'GameCenter', label: 'Game Center', hint: 'iOS' },
]

/** 화면 필드 ↔ DB 키. **Unity 가 같은 키로 읽으므로 함부로 바꾸지 말 것.** */
const KEYS: Record<keyof EnvSettings, string> = {
  name: 'name',
  gcpProjectId: 'gcp_project_id',
  gcpRegion: 'gcp_region',
  gcpServiceName: 'gcp_service_name',
  gcpMinInstances: 'gcp_min_instances',
  githubRepoName: 'github_repo_name',
  serverCaches: 'server_caches',
  platformAuth: 'platform_auth',
}

export const EMPTY_ENV: EnvSettings = {
  name: '',
  gcpProjectId: '',
  gcpRegion: '',
  gcpServiceName: '',
  gcpMinInstances: '',
  githubRepoName: '',
  serverCaches: '',
  platformAuth: '',
}

export async function loadEnvSettings(): Promise<EnvSettings> {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')

  const { data, error } = await sb
    .from('suparun_env')
    .select<{ key: string; value: string }[]>('key,value')
  if (error) throw new Error(error.message)

  const byKey = new Map((data ?? []).map((r) => [r.key, r.value]))
  const out = { ...EMPTY_ENV }
  for (const field of Object.keys(KEYS) as (keyof EnvSettings)[])
    out[field] = byKey.get(KEYS[field]) ?? ''
  return out
}

/**
 * **바뀐 것만** 쓴다. 전체를 덮어쓰지 않는 이유: 이 표는 Unity 도 읽고, 화면에 없는 키가
 * 나중에 늘어날 수 있다. 모르는 키를 지우지 않는 편이 안전하다.
 */
export async function saveEnvSettings(
  next: EnvSettings,
  prev: EnvSettings,
  by: string,
): Promise<number> {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')

  const now = Date.now()
  const rows = (Object.keys(KEYS) as (keyof EnvSettings)[])
    .filter((f) => next[f] !== prev[f])
    .map((f) => ({ key: KEYS[f], value: next[f], updated_at: now, updated_by: by }))

  if (rows.length === 0) return 0

  const { error } = await sb.from('suparun_env').upsert(rows, { onConflict: 'key' })
  if (error) throw new Error(error.message)
  return rows.length
}
