import { sb } from './supabase'

/**
 * 스냅샷 / 복원 (ADR-0004 Backlog).
 *
 * 본체는 `snap_*` 스키마고, 목록·코멘트·핀은 `suparun_snapshot` 표다.
 * **찍기·복원·삭제만 RPC**다 — 브라우저가 DDL 을 실행할 수 없기 때문이고, 그 외(목록 조회,
 * 코멘트 수정, 핀 토글)는 평범한 표 조작이라 PostgREST 로 그냥 간다. RPC 를 늘리지 않는다.
 *
 * 범위는 `[SpecData]` 뿐이다. 서버 쪽 `suparun_snapshot_tables()` 가 config_types 만 보므로
 * 이 화면에서 무엇을 눌러도 `[UserData]` 에는 닿지 못한다.
 */

export interface Snapshot {
  schema_name: string
  label: string
  comment: string | null
  created_by: string
  /** epoch millis */
  created_at: number
  /** 복원 직전 자동으로 찍힌 것인가. 불변 — 배지로만 쓴다. */
  created_by_auto: boolean
  /** 보관 중인가. 핀 토글이 바꾸며, false 인 것만 자동 정리 대상이다. */
  pinned: boolean
}

/**
 * 테이블 하나의 현재 ↔ 스냅샷 차이. 리스트 배지와 복원 확인 화면이 함께 쓴다.
 *
 * 이름이 `tbl_`/`_cols` 로 접힌 것은 서버 사정이다 — `table_name` 같은 이름을 반환 컬럼으로 쓰면
 * 함수 본문의 `information_schema.columns` 조회와 충돌해 실행이 죽는다.
 */
export interface SnapshotDiff {
  tbl_name: string
  cur_rows: number
  /** 스냅샷에 그 테이블이 없으면 null. */
  snap_rows: number | null
  /** 찍은 뒤 생긴 컬럼 — 복원해도 기본값으로 남는다. */
  added_cols: string[] | null
  /** 그 사이 사라진 컬럼 — 스냅샷 값은 버려진다. */
  removed_cols: string[] | null
  /** 스냅샷에 없는 테이블. 복원해도 손대지 않는다. */
  is_missing: boolean
}

function client() {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  return sb
}

export async function loadSnapshots(): Promise<Snapshot[]> {
  const { data, error } = await client()
    .from('suparun_snapshot')
    .select<Snapshot[]>('*')
    .order('created_at', { ascending: false })
  if (error) throw new Error(error.message)
  return data ?? []
}

/** 반환값은 만들어진 스키마명. */
export async function createSnapshot(label: string, comment?: string): Promise<string> {
  const { data, error } = await client().rpc<string>('suparun_snapshot_create', {
    p_label: label,
    p_comment: comment ?? null,
    p_auto: false,
  })
  if (error) throw new Error(error.message)
  return data ?? ''
}

/**
 * 되돌린다. 반환값은 **직전 상태가 담긴 자동 스냅샷의 이름**이다 —
 * 화면이 "돌아올 자리"를 바로 알려줄 수 있게.
 */
export async function restoreSnapshot(schemaName: string): Promise<string> {
  const { data, error } = await client().rpc<string>('suparun_snapshot_restore', {
    p_schema: schemaName,
  })
  if (error) throw new Error(error.message)
  return data ?? ''
}

export async function deleteSnapshot(schemaName: string): Promise<void> {
  const { error } = await client().rpc('suparun_snapshot_delete', { p_schema: schemaName })
  if (error) throw new Error(error.message)
}

export async function loadDiff(schemaName: string): Promise<SnapshotDiff[]> {
  const { data, error } = await client().rpc<SnapshotDiff[]>('suparun_snapshot_diff', {
    p_schema: schemaName,
  })
  if (error) throw new Error(error.message)
  return data ?? []
}

/** 핀·라벨·코멘트는 표 한 줄 고치는 일이다. RPC 를 거치지 않는다. */
export async function patchSnapshot(
  schemaName: string,
  patch: Partial<Pick<Snapshot, 'label' | 'comment' | 'pinned'>>,
): Promise<void> {
  const { error } = await client()
    .from('suparun_snapshot')
    .update(patch)
    .eq('schema_name', schemaName)
  if (error) throw new Error(error.message)
}

/** 차이 목록에서 "이 복원이 실제로 무엇을 바꾸는가"만 추린다. */
export function summarizeDiff(diff: SnapshotDiff[]): {
  changedTables: SnapshotDiff[]
  skipped: SnapshotDiff[]
  schemaDrift: number
} {
  const applies = diff.filter((d) => !d.is_missing)
  return {
    changedTables: applies.filter((d) => d.cur_rows !== d.snap_rows),
    skipped: diff.filter((d) => d.is_missing),
    schemaDrift: applies.reduce(
      (n, d) => n + (d.added_cols?.length ?? 0) + (d.removed_cols?.length ?? 0),
      0,
    ),
  }
}
