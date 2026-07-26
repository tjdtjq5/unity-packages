import { edgeFn } from './edgeFn'

/**
 * Supabase 프로젝트 관리. 전부 `suparun-admin` Edge Function 을 거친다(shared/edgeFn.ts 참조).
 *
 * 예전에는 로컬 브리지(127.0.0.1)를 거쳤다. 그러면 **Unity 가 켜져 있어야만** 프로젝트를
 * 다룰 수 있고, 기획자가 웹만 열어 보는 경우가 막힌다. Edge Function 은 Supabase 프로젝트가
 * 있으면 항상 존재하므로 그 제약이 사라진다.
 */

export interface SupabaseProject {
  ref: string
  name: string
  status: string
  region: string
  url: string
}

export async function listProjects(): Promise<SupabaseProject[]> {
  const r = await edgeFn.get<{ projects: SupabaseProject[] }>('/projects')
  return r.projects ?? []
}

export async function createProject(
  name: string,
  region?: string,
  plan?: string,
): Promise<{ ref: string; name: string; status: string }> {
  return await edgeFn.post('/projects', { name, region, plan })
}

export async function deleteProject(projectRef: string): Promise<void> {
  await edgeFn.del(`/projects?ref=${encodeURIComponent(projectRef)}`)
}

export async function availableRegions(): Promise<{ code: string; label: string }[]> {
  const r = await edgeFn.get<{ regions: { code: string; label: string }[] }>('/regions')
  return r.regions ?? []
}
