import { bridge } from './bridge'

/**
 * Supabase 프로젝트 관리. 전부 로컬 브리지를 거친다(shared/bridge.ts 참조).
 *
 * 한동안 Edge Function 을 거쳤다. 브리지를 쓰면 **Unity 가 켜져 있어야만** 되고 그것이
 * 웹만 열어 보는 사람을 막는다는 이유였다. 지금은 **어드민 자체를 브리지가 서빙하므로**
 * 그 제약이 이미 들어와 있다 — 우회로를 유지할 값이 없어졌다.
 */

export interface SupabaseProject {
  ref: string
  name: string
  status: string
  region: string
  url: string
}

export async function listProjects(): Promise<SupabaseProject[]> {
  const r = await bridge.get<{ projects: SupabaseProject[] }>('/projects')
  return r.projects ?? []
}

export async function createProject(
  name: string,
  region?: string,
  plan?: string,
): Promise<{ ref: string; name: string; status: string }> {
  return await bridge.post('/projects', { name, region, plan })
}

export async function deleteProject(projectRef: string): Promise<void> {
  await bridge.del(`/projects?ref=${encodeURIComponent(projectRef)}`)
}

export async function availableRegions(): Promise<{ code: string; label: string }[]> {
  const r = await bridge.get<{ regions: { code: string; label: string }[] }>('/regions')
  return r.regions ?? []
}
