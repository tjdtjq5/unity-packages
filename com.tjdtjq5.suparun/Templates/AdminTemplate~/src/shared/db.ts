import { isPreview } from './env'
import { sb } from './supabase'

/**
 * Supabase 직접 접근 데이터 계층 (ADR-0004).
 *
 * 서버 `/admin/api/config/*` 를 대체한다. 인증은 supabase-js 세션이 알아서 싣고,
 * 권한은 RLS 가 판정한다 — 서버 미들웨어의 403 체크가 DB 로 내려간 셈이다.
 *
 * **프리뷰 모드는 기존 mock 을 그대로 탄다.** `?mock=1` 은 Supabase 없이 화면만 보는 용도라
 * 실제 쿼리를 보낼 수 없다.
 */

declare global {
  interface Window {
    __previewApi?: (path: string, method?: string, body?: unknown) => Promise<unknown>
  }
}

/** RLS 거부는 메시지가 불친절해서(`new row violates row-level security policy`) 풀어 준다. */
function describe(error: { message: string; code?: string }): string {
  const m = error.message || '알 수 없는 오류'
  if (/row-level security/i.test(m)) return '권한이 없습니다 (RLS 정책에 막힘). 관리자 승인 여부를 확인하세요.'
  if (error.code === '23505') return '중복된 값입니다 (이미 있는 ID).'
  if (error.code === '23502') return '필수 값이 비어 있습니다.'
  return m
}

function client() {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  return sb
}

/** 전체 조회. `sort_order` 가 있으면 그 순서로 (없는 테이블은 무시된다). */
export async function selectAll<T>(table: string): Promise<T[]> {
  if (isPreview()) return (await window.__previewApi!(`/${table}`)) as T[]
  const { data, error } = await client()
    .from(table)
    .select<T[]>('*')
    .order('sort_order', { ascending: true })
  if (error) throw new Error(describe(error))
  return data ?? []
}

/** 새 행. 서버가 채운 값(기본값·트리거)을 되돌려 받는다. */
export async function insertRow<T>(table: string, row: unknown): Promise<T> {
  if (isPreview()) return (await window.__previewApi!(`/${table}`, 'POST', row)) as T
  const { data, error } = await client().from(table).insert<T[]>(row).select()
  if (error) throw new Error(describe(error))
  const rows = data ?? []
  if (rows.length === 0) throw new Error('추가되었으나 결과를 받지 못했습니다.')
  return rows[0]
}

/**
 * 행 수정. 바닐라 서버는 행 전체를 PUT 했는데, 여기서는 **바뀐 필드만** 보낸다 —
 * 동시에 다른 필드를 고친 변경을 덮어쓰지 않는다.
 */
export async function updateRow<T>(
  table: string,
  pkColumn: string,
  pkValue: string,
  patch: Record<string, unknown>,
): Promise<T | null> {
  if (isPreview())
    return (await window.__previewApi!(`/${table}/${encodeURIComponent(pkValue)}`, 'PUT', patch)) as T
  const { data, error } = await client()
    .from(table)
    .update<T[]>(patch)
    .eq(pkColumn, pkValue)
    .select()
  if (error) throw new Error(describe(error))
  return (data ?? [])[0] ?? null
}

export async function deleteRow(table: string, pkColumn: string, pkValue: string): Promise<void> {
  if (isPreview()) {
    await window.__previewApi!(`/${table}/${encodeURIComponent(pkValue)}`, 'DELETE')
    return
  }
  const { error } = await client().from(table).delete().eq(pkColumn, pkValue)
  if (error) throw new Error(describe(error))
}

/**
 * 여러 행 일괄 갱신 (드래그 정렬용).
 *
 * 서버의 `_reorder` 는 트랜잭션이었지만 upsert 는 아니다 — 중간에 끊기면 순서가 반쯤 반영된다.
 * 다만 sort_order 는 표시 순서일 뿐이고 실패 시 다시 로드해 되돌리므로, 트랜잭션을 위해
 * RPC 를 만들 만한 사안은 아니다 (ADR-0004 결정 7).
 */
export async function upsertMany(table: string, rows: unknown[], pkColumn: string): Promise<void> {
  if (isPreview()) {
    await window.__previewApi!(`/_reorder/${table}`, 'POST', { items: rows })
    return
  }
  const { error } = await client().from(table).upsert(rows, { onConflict: pkColumn })
  if (error) throw new Error(describe(error))
}

/**
 * 총 건수만. 본문을 받지 않으므로(`head: true`) 행이 몇 만이든 가볍다.
 * PostgREST 집계 함수가 막혀 있어(`PGRST123`) count 는 이 경로로만 얻는다.
 */
export async function countRows(table: string): Promise<number> {
  if (isPreview()) return ((await window.__previewApi!(`/${table}`)) as unknown[]).length
  const { count, error } = await client().from(table).select('*', { count: 'exact', head: true })
  if (error) throw new Error(describe(error))
  return count ?? 0
}
