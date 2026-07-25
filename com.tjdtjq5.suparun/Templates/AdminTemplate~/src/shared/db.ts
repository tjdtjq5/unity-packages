import { isPreview } from './env'
import { sb, type PostgrestFilter } from './supabase'

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

export interface SelectOptions {
  /** 정렬 컬럼. 기본은 `sort_order`(Config 용). **없는 컬럼을 주면 PostgREST 가 에러를 낸다.** */
  orderBy?: string | null
  ascending?: boolean
  limit?: number
}

/**
 * 전체 조회.
 *
 * 기본 정렬이 `sort_order` 인 것은 [SpecData] 가 그 컬럼을 자동으로 갖기 때문이다.
 * `admin_user` · `admin_audit_log` 처럼 없는 테이블은 `orderBy` 를 명시하거나 null 로 꺼야 한다.
 */
export async function selectAll<T>(table: string, opts: SelectOptions = {}): Promise<T[]> {
  if (isPreview()) return (await window.__previewApi!(`/${table}`)) as T[]
  const { orderBy = 'sort_order', ascending = true, limit } = opts
  let q = client().from(table).select<T[]>('*')
  if (orderBy) q = q.order(orderBy, { ascending })
  if (limit != null) q = q.limit(limit)
  const { data, error } = await q
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

// ── 조회 필터 (Table 화면) ─────────────────────────────────

export type FilterOp = '=' | '>' | '>=' | '<' | '<=' | 'like'

export interface QueryFilter {
  field: string
  op: FilterOp
  value: string
}

function applyFilters<T>(q: PostgrestFilter<T>, filters: QueryFilter[]): PostgrestFilter<T> {
  for (const f of filters) {
    if (!f.value) continue
    switch (f.op) {
      case '=': q = q.eq(f.field, f.value); break
      case '>': q = q.gt(f.field, f.value); break
      case '>=': q = q.gte(f.field, f.value); break
      case '<': q = q.lt(f.field, f.value); break
      case '<=': q = q.lte(f.field, f.value); break
      // 바닐라 서버는 부분 일치였다. PostgREST 는 % 를 직접 써야 한다.
      case 'like': q = q.ilike(f.field, `%${f.value}%`); break
    }
  }
  return q
}

/** 필터 + 페이지네이션 조회. 총 건수는 `count: 'exact'` 로 함께 받는다. */
export async function selectPage<T>(
  table: string,
  filters: QueryFilter[],
  offset: number,
  limit: number,
): Promise<{ rows: T[]; total: number }> {
  let q = client().from(table).select<T[]>('*', { count: 'exact' })
  q = applyFilters(q, filters)
  const { data, count, error } = await q.range(offset, offset + limit - 1)
  if (error) throw new Error(describe(error))
  return { rows: data ?? [], total: count ?? 0 }
}

/**
 * 건수·최소·최대. **집계 함수 없이** 구한다 —
 * count 는 본문 0행(`head`), min/max 는 정렬 후 1행. 데이터가 몇 만이든 전송량이 거의 없다.
 * 합계·평균은 전체 스캔이 필요해 제공하지 않는다 (ADR-0004 결정 8).
 */
export async function selectStats(
  table: string,
  column: string,
  filters: QueryFilter[],
): Promise<{ count: number; min: number | null; max: number | null }> {
  const countQ = applyFilters(client().from(table).select('*', { count: 'exact', head: true }), filters)
  const minQ = applyFilters(client().from(table).select<Record<string, unknown>[]>(column), filters)
    .order(column, { ascending: true })
    .limit(1)
  const maxQ = applyFilters(client().from(table).select<Record<string, unknown>[]>(column), filters)
    .order(column, { ascending: false })
    .limit(1)

  const [c, lo, hi] = await Promise.all([countQ, minQ, maxQ])
  for (const r of [c, lo, hi]) if (r.error) throw new Error(describe(r.error))

  const pick = (rows: Record<string, unknown>[] | null) => {
    const v = rows?.[0]?.[column]
    return typeof v === 'number' ? v : null
  }
  return { count: c.count ?? 0, min: pick(lo.data), max: pick(hi.data) }
}

/**
 * 분포 히스토그램. min/max 로 구간을 나눈 뒤 **구간별 count 만** 받는다.
 * 본문을 하나도 받지 않으므로 행이 몇 만이든 같은 비용이다.
 */
export async function selectDistribution(
  table: string,
  column: string,
  filters: QueryFilter[],
  bucketCount = 10,
): Promise<{ min: number; max: number; count: number }[]> {
  const { min, max } = await selectStats(table, column, filters)
  if (min == null || max == null) return []
  if (min === max) return [{ min, max, count: await bucketSize(table, column, filters, min, max, true) }]

  const width = (max - min) / bucketCount
  const ranges = Array.from({ length: bucketCount }, (_, i) => ({
    min: min + width * i,
    max: i === bucketCount - 1 ? max : min + width * (i + 1),
    last: i === bucketCount - 1,
  }))

  const counts = await Promise.all(
    ranges.map((r) => bucketSize(table, column, filters, r.min, r.max, r.last)),
  )
  return ranges.map((r, i) => ({ min: r.min, max: r.max, count: counts[i] }))
}

/** 마지막 구간만 상한을 포함한다 — 안 그러면 최대값이 어디에도 안 들어간다. */
async function bucketSize(
  table: string,
  column: string,
  filters: QueryFilter[],
  lo: number,
  hi: number,
  inclusive: boolean,
): Promise<number> {
  let q = applyFilters(client().from(table).select('*', { count: 'exact', head: true }), filters)
  q = q.gte(column, lo)
  q = inclusive ? q.lte(column, hi) : q.lt(column, hi)
  const { count, error } = await q
  if (error) throw new Error(describe(error))
  return count ?? 0
}
