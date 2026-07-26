import { loadMeta } from './meta'
import { sb } from './supabase'

/**
 * 환경 역할 정의 — **어드민이 쓰고 Unity 가 읽는다.**
 *
 * `suparun_meta.environments`(Unity 가 기록하는 현황 캐시)와 방향이 반대다. 나눈 이유:
 *   environments — "지금 어떤 상태인가". Unity 만 알 수 있다(PAT 필요)
 *   env_roles    — "이 프로젝트를 무엇으로 쓸 것인가". 사람이 정한다
 *
 * 비밀(PAT·anon key·DB 비번)은 계속 로컬에만 있다. 여기 담기는 것은 **이름과 역할뿐**이다.
 */

export interface EnvRole {
  /** 사람이 부르는 이름. dev / prod / staging … */
  name: string
  /** 편집 환경 — 컴파일할 때 스키마가 이 프로젝트로 간다. 하나만 true. */
  editor?: boolean
  /** 빌드 환경 — 게임 빌드가 이 프로젝트를 바라본다. 하나만 true. */
  build?: boolean
}

/** project_ref → 역할 */
export type EnvRoleMap = Record<string, EnvRole>

export async function loadEnvRoles(): Promise<EnvRoleMap> {
  const meta = await loadMeta(['env_roles'])
  return (meta.env_roles as EnvRoleMap | undefined) ?? {}
}

/**
 * 통째로 덮어쓴다. 부분 갱신을 두지 않는 이유: 편집·빌드 지정이 **환경 사이의 관계**라
 * 한 행만 고치면 "편집 환경이 둘" 같은 상태가 만들어진다. 항상 전체를 검사해 저장한다.
 */
export async function saveEnvRoles(roles: EnvRoleMap): Promise<void> {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  const { error } = await sb
    .from('suparun_meta')
    .upsert({ key: 'env_roles', value: roles }, { onConflict: 'key' })
  if (error) throw new Error(error.message)
}

/** 편집·빌드는 각각 하나뿐이다. 켜는 쪽을 남기고 나머지를 끈다. */
export function setExclusive(
  roles: EnvRoleMap,
  ref: string,
  field: 'editor' | 'build',
  on: boolean,
): EnvRoleMap {
  const next: EnvRoleMap = {}
  for (const [k, v] of Object.entries(roles)) {
    next[k] = { ...v, [field]: on && k === ref }
  }
  if (!next[ref]) next[ref] = { name: '', [field]: on }
  return next
}
