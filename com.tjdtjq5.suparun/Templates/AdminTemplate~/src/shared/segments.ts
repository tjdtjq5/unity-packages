import { deleteRow, insertRow, selectAll, updateRow } from './db'
import { isPreview } from './env'
import { sb } from './supabase'

/**
 * 세그먼트 (③ 트랙 #43~#45, ADR-0011) — 조건으로 정의되는 플레이어 부분집합.
 *
 * 정의(suparun_segment)는 PostgREST CRUD(RLS: 열람=롤, 쓰기=game-admin, 감사 트리거).
 * 평가는 **DB 함수가 유일한 구현**이다 — 어드민과 게임 서버가 같은 것을 부른다.
 */

export interface SegmentCondition {
  source: 'account' | 'system' | 'table'
  column?: string
  op: string
  value?: unknown
  table?: string
  agg?: string
  table_filter?: Record<string, string>
}

export interface Segment {
  id: string
  name: string
  description: string | null
  match: 'all' | 'any'
  conditions: SegmentCondition[]
  created_at: number
  created_by: string | null
  updated_at: number | null
  updated_by: string | null
}

const MOCK: Segment[] = [
  {
    id: 'seg_mock1', name: '고래 후보', description: '골드 1000 이상 + 7일 내 접속',
    match: 'all',
    conditions: [
      { source: 'table', table: 'currency', table_filter: { currencyid: 'gold' }, column: 'amount', agg: 'max', op: '>=', value: 1000 },
      { source: 'account', column: 'last_sign_in_at', op: 'since_days', value: 7 },
    ],
    created_at: Date.now() - 86400_000, created_by: null, updated_at: null, updated_by: null,
  },
]

export async function listSegments(): Promise<Segment[]> {
  if (isPreview()) return MOCK
  return selectAll<Segment>('suparun_segment', { orderBy: 'created_at', ascending: false })
}

export async function createSegment(row: Omit<Segment, 'created_at' | 'created_by' | 'updated_at' | 'updated_by'>): Promise<Segment> {
  return insertRow<Segment>('suparun_segment', { ...row, created_at: Date.now() })
}

export async function updateSegment(id: string, patch: Partial<Segment>): Promise<Segment | null> {
  return updateRow<Segment>('suparun_segment', 'id', id, { ...patch, updated_at: Date.now() })
}

export async function removeSegment(id: string): Promise<void> {
  return deleteRow('suparun_segment', 'id', id)
}

function client() {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  return sb
}

/** 대상 수 미리보기 — 전수 평가라 목록에서 남발하지 않는다(상세에서만). */
export async function segmentCount(id: string): Promise<number> {
  if (isPreview()) return 42
  const { data, error } = await client().rpc<number>('suparun_segment_count', { p_segment_id: id })
  if (error) throw new Error(error.message)
  return typeof data === 'number' ? data : Number(data)
}

/** 플레이어의 소속 세그먼트 (#45 — 플레이어 상세 칩). */
export async function segmentsOf(playerId: string): Promise<{ segment_id: string; name: string }[]> {
  if (isPreview()) return [{ segment_id: 'seg_mock1', name: '고래 후보' }]
  const { data, error } = await client().rpc<{ segment_id: string; name: string }[]>(
    'suparun_segments_of', { p_player_id: playerId })
  if (error) throw new Error(error.message)
  return data ?? []
}
