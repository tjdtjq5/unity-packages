import { bridge } from './bridge'

/**
 * 배포 체크리스트가 쓰는 브리지 API.
 *
 * `gcloud`·`gh` 는 로컬 명령이라 브라우저가 직접 못 돌린다. 브리지(Unity)가 손 역할을 한다.
 *
 * **로그인은 완료를 알려주지 않는다** — 브라우저가 열리고 사람이 거기서 끝낸다. 그래서 화면은
 * `status` 를 주기적으로 물어보고, 값이 바뀌는 순간이 곧 "연결됨"이다. 다시 누를 필요가 없다.
 */

export interface ToolState {
  installed: boolean
  loggedIn: boolean
  account: string | null
  version: string | null
  /** 안 깔렸을 때만 온다. 복사해 붙이는 한 줄. */
  installCommand: string | null
}

export interface DeployStatus {
  tools: { dotnet: ToolState; gcloud: ToolState; gh: ToolState }
  billing: { enabled: boolean; blocked: string | null }
  permission: { ok: boolean; serviceAccount: string | null; blocked: string | null }
  /** Unity 가 **지금 보고 있는** 값. 편집은 `envSettings.ts` 로 한다(어드민이 소유). */
  target: {
    name: string
    gcpProjectId: string
    gcpRegion: string
    gcpServiceName: string
    gcpMinInstances: number
    githubRepoName: string
    serverCaches: string
  }
  autoSetup: { running: boolean; step: string; error: string | null }
  ready: boolean
}

export const deploy = {
  status: () => bridge.get<DeployStatus>('/deploy/status'),

  gcloudLogin: () => bridge.post<{ started: boolean }>('/deploy/gcloud-login'),
  ghLogin: () => bridge.post<{ started: boolean }>('/deploy/gh-login'),

  /** 저장한 뒤 부른다 — Unity 가 `suparun_env` 를 다시 읽어야 ready 판정이 맞는다. */
  refresh: () => bridge.post<{ ok: boolean }>('/deploy/refresh'),

  gcpProjects: () => bridge.get<{ projects: { id: string; name: string }[] }>('/deploy/gcp-projects'),
  createGcpProject: (id: string, name: string) =>
    bridge.post<{ id: string }>('/deploy/gcp-projects', { id, name }),

  billingAccounts: () => bridge.get<{ accounts: { id: string; name: string }[] }>('/deploy/billing-accounts'),
  linkBilling: (account: string) => bridge.post<{ ok: boolean }>('/deploy/billing-link', { account }),

  ghRepos: () => bridge.get<{ repos: string[] }>('/deploy/gh-repos'),
  createGhRepo: (name: string) =>
    bridge.post<{ name: string; alreadyExisted: boolean }>('/deploy/gh-repos', { name }),

  autoSetup: () => bridge.post<{ started: boolean }>('/deploy/auto-setup'),
}

/** Cloud Run 서비스명 규칙: 소문자·숫자·하이픈만, 63자 이내. Unity 쪽 SanitizeServiceName 과 같다. */
export function sanitizeServiceName(input: string): string {
  const s = (input || '')
    .toLowerCase()
    .replace(/[ _]/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/^-+|-+$/g, '')
  return s.length > 63 ? s.slice(0, 63).replace(/-+$/, '') : s
}

/** 사람이 고르는 리전. 가까울수록 빠르다. */
export const REGIONS: { code: string; label: string }[] = [
  { code: 'asia-northeast3', label: '서울' },
  { code: 'asia-northeast1', label: '도쿄' },
  { code: 'asia-east1', label: '대만' },
  { code: 'asia-southeast1', label: '싱가포르' },
  { code: 'us-central1', label: '아이오와' },
  { code: 'us-east1', label: '버지니아' },
  { code: 'europe-west1', label: '벨기에' },
]

/** min instances 를 **비용**으로 부른다. 숫자만 보여주면 무슨 뜻인지 알 수 없다. */
export const MIN_INSTANCES: { value: string; label: string }[] = [
  { value: '0', label: '무료 — 첫 요청이 2~5초 느림' },
  { value: '1', label: '항상 켜짐 — 월 ~5만원' },
]
