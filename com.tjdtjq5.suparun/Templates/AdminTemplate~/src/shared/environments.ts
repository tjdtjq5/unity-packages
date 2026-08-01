import { loadMeta } from './meta'

/**
 * 환경 현황 (dev / prod …).
 *
 * **Unity 가 넣어 둔 것을 읽기만 한다.** 이 값들은 전부 Management API + PAT 로만 얻을 수 있는데
 * PAT 는 로컬 에디터에만 두기로 했다(어드민 계정이 털려도 Supabase 계정 전체는 안 넘어가게).
 * 그래서 아이콘 맵과 같은 방식이다 — Unity 가 어드민을 열 때 구워서 `suparun_meta` 에 넣는다.
 *
 * 따라서 이 화면은 **마지막으로 Unity 가 본 상태**다. `collected_at` 이 그걸 드러낸다.
 */

export interface EnvironmentInfo {
  name: string
  supabase_url?: string
  cloud_run_url?: string
  service_name?: string
  project_ref?: string
  /** 에디터가 지금 보고 있는 환경인가. 컴파일 시 스키마가 여기로 간다. */
  is_editor?: boolean
  /** 빌드에 구워지는 환경인가. */
  /** epoch millis — 언제 수집했는가. 값의 신선도를 판단하는 유일한 근거다. */
  collected_at?: number

  status?: string
  region?: string
  created_at?: string

  /** 서비스별 헬스. `{ db: true, rest: true, auth: true }` */
  services?: Record<string, boolean>

  disk_total?: number
  disk_used?: number

  /** load average / 코어 수 근사. 스냅샷 하나로는 정확한 CPU% 를 못 구한다. */
  cpu_percent?: number
  cpu_cores?: number

  mem_total?: number
  mem_used?: number
  mem_percent?: number

  connections?: number
  max_connections?: number

  /** 수집이 실패한 환경. 이름·URL 만 있고 나머지가 비어 있다. */
  error?: string
}

export async function loadEnvironments(): Promise<EnvironmentInfo[]> {
  const meta = await loadMeta(['environments'])
  return (meta.environments as EnvironmentInfo[] | undefined) ?? []
}

/** 모든 서비스가 정상인가. 하나라도 죽었으면 false. */
export function isHealthy(e: EnvironmentInfo): boolean {
  if (e.error) return false
  if (e.status && e.status !== 'ACTIVE_HEALTHY') return false
  if (!e.services) return true
  return Object.values(e.services).every(Boolean)
}

export function isPaused(e: EnvironmentInfo): boolean {
  return e.status === 'INACTIVE'
}

/** 바이트를 사람이 읽는 크기로. */
export function formatBytes(n?: number): string {
  if (n == null) return '—'
  if (n < 1024) return `${n} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let v = n / 1024
  let i = 0
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024
    i++
  }
  return `${v.toFixed(v < 10 ? 1 : 0)} ${units[i]}`
}

/** 사용률에 따른 색 등급. 0~100 을 받는다. */
export function levelOf(percent?: number): 'ok' | 'warn' | 'danger' | 'none' {
  if (percent == null) return 'none'
  if (percent >= 90) return 'danger'
  if (percent >= 70) return 'warn'
  return 'ok'
}
