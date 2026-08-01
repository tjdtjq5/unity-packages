import { isPreview } from './env'
import { sb } from './supabase'

/**
 * 플레이어 조회 (#36·#37, ③ 트랙).
 *
 * auth.users 는 PostgREST 스키마 밖이라 표처럼 읽을 수 없다 — SECURITY DEFINER RPC
 * (`suparun_player_search` / `suparun_player_get`)가 유일한 창이고, 문은 롤 보유
 * (suparun_is_operator)로 잠겨 있다. 밴·개발자 상태는 RPC 가 조인해서 함께 온다.
 */

export interface Player {
  id: string
  email: string | null
  name: string
  created_at: number
  last_sign_in_at: number | null
  banned: boolean
  ban_reason: string | null
  banned_until: number | null
  is_developer: boolean
}

function client() {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  return sb
}

/** 디자인 미리보기용 표본 — 화면이 실데이터 없이도 자기 모양을 보여줄 수 있어야 한다. */
const MOCK: Player[] = [
  { id: 'a1b2c3d4-0000-4000-8000-000000000001', email: 'guest_4d2@device.local', name: 'ErraticTurtle', created_at: 1753670000000, last_sign_in_at: Date.now() - 3600_000, banned: false, ban_reason: null, banned_until: null, is_developer: false },
  { id: 'a1b2c3d4-0000-4000-8000-000000000002', email: 'dev@example.test', name: 'DevChoi', created_at: 1750000000000, last_sign_in_at: Date.now() - 86400_000, banned: false, ban_reason: null, banned_until: null, is_developer: true },
  { id: 'a1b2c3d4-0000-4000-8000-000000000003', email: 'guest_9f1@device.local', name: 'CrudeFox', created_at: 1752000000000, last_sign_in_at: Date.now() - 7 * 86400_000, banned: true, ban_reason: '반복 어뷰징', banned_until: 0, is_developer: false },
]

/** 검색 + 최근 활동 목록. 빈 질의 = 최근 로그인 순. */
export async function searchPlayers(query: string, limit = 50): Promise<Player[]> {
  if (isPreview()) {
    const q = query.trim().toLowerCase()
    return q
      ? MOCK.filter((p) => p.id.startsWith(q) || (p.email ?? '').includes(q) || p.name.toLowerCase().includes(q))
      : MOCK
  }
  const { data, error } = await client().rpc<Player[]>('suparun_player_search', {
    p_query: query.trim() || null,
    p_limit: limit,
  })
  if (error) throw new Error(error.message)
  return data ?? []
}

/** 단건. 없는 ID 는 null — 화면이 명시적 안내를 그린다 (#37). */
export async function getPlayer(id: string): Promise<Player | null> {
  if (isPreview()) return MOCK.find((p) => p.id === id) ?? MOCK[0]
  const { data, error } = await client().rpc<Player[]>('suparun_player_get', { p_id: id })
  if (error) throw new Error(error.message)
  return (data ?? [])[0] ?? null
}
