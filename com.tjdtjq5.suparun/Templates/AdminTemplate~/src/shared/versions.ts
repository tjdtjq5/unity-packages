import { sb } from './supabase'

/**
 * config 버전·게시 (ADR-0010, #30~#34).
 *
 * 버전의 실체는 이 환경 안의 **미게시 스냅샷**(suparun_snapshot.is_version)이다.
 * 활성본은 public 이고, 어느 버전이 활성인지는 suparun_meta 의 스탬프가 말한다 —
 * public_read 라 클라 세션 협상(#35)도 같은 창구를 읽는다.
 */

export interface ConfigVersion {
  schema_name: string
  label: string
  created_at: number
  created_by: string
  content_hash: string | null
  git_sha: string | null
  published_at: number | null
  published_by: string | null
}

export interface ActiveVersion {
  content_hash: string
  schema_name: string
  git_sha: string | null
  published_at: number
  published_by: string
}

function client() {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  return sb
}

export async function listVersions(): Promise<ConfigVersion[]> {
  const r = await client()
    .from('suparun_snapshot')
    .select<ConfigVersion[]>('schema_name,label,created_at,created_by,content_hash,git_sha,published_at,published_by')
    .eq('is_version', true)
    .order('created_at', { ascending: false })
  if (r.error) throw new Error(r.error.message)
  return r.data ?? []
}

export async function activeVersion(): Promise<ActiveVersion | null> {
  const r = await client()
    .from('suparun_meta')
    .select<{ value: ActiveVersion }[]>('value')
    .eq('key', 'active_config_version')
  if (r.error) throw new Error(r.error.message)
  return r.data?.[0]?.value ?? null
}

/** 게시(=롤백 포함). 자동 백업 스키마명을 돌려준다. RPC 가 game-admin 을 요구한다. */
export async function publishVersion(schema: string): Promise<string> {
  const r = await client().rpc<string>('suparun_version_publish', { p_schema: schema })
  if (r.error) throw new Error(r.error.message)
  return r.data ?? ''
}

/** diff 좌표 — 'public'(활성본) 또는 버전 스키마명. */
export const ACTIVE_COORD = 'public'

export interface TableDiff {
  tbl_name: string
  added: number
  removed: number
  modified: number
  base_missing: boolean
  new_missing: boolean
}

export async function diffTables(base: string, next: string): Promise<TableDiff[]> {
  const r = await client().rpc<TableDiff[]>('suparun_version_diff_tables', { p_base: base, p_new: next })
  if (r.error) throw new Error(r.error.message)
  return r.data ?? []
}

export interface RowDiff {
  row_id: string
  status: 'added' | 'removed' | 'modified'
  before_json: string | null
  after_json: string | null
}

export async function diffRows(base: string, next: string, table: string): Promise<RowDiff[]> {
  const r = await client().rpc<RowDiff[]>('suparun_version_diff_rows', {
    p_base: base,
    p_new: next,
    p_table: table,
  })
  if (r.error) throw new Error(r.error.message)
  return r.data ?? []
}

// ── 릴리스 매니페스트 (#51, ADR-0010 결정 5) ──────────────────

export interface ReleaseStep {
  step: string
  ok: boolean
  at: number
  detail?: string
}

export interface Release {
  id: string
  logic_version: number
  logic_min: number
  git_sha: string | null
  content_hash: string | null
  revision_tag: string | null
  memo: string | null
  status: 'running' | 'done' | 'failed'
  steps: ReleaseStep[]
  published_at: number | null
  published_by: string | null
  created_at: number
  created_by: string | null
}

export async function listReleases(): Promise<Release[]> {
  const r = await client()
    .from('suparun_release')
    .select<Release[]>('*')
    .order('created_at', { ascending: false })
  if (r.error) throw new Error(r.error.message)
  return r.data ?? []
}
