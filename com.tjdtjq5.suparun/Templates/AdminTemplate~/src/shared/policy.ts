import { sb } from './supabase'

/**
 * RLS 정책 조회/변경 (ADR-0004 결정 15~19).
 *
 * `pg_policies` 는 `public` 스키마가 아니라 PostgREST 로 못 읽고, CREATE/DROP POLICY 는 DDL 이라
 * 어느 쪽도 직접은 안 된다. **"RPC 없음" 결정의 유일한 예외**다.
 *
 * 보내는 것은 테이블명과 프리셋 이름뿐이고, SQL 은 함수 안에서 조립된다.
 */

/**
 * "본인만 읽기" 프리셋은 없다 — 게임이 [UserData] 를 Supabase 에서 직접 읽지 않기 때문이다
 * (Cloud Run 을 거친다). anon 에게 열어 줄 이유가 없고, 열면 그 문은 모든 플레이어에게 열린다.
 * ADR-0004 결정 20 참조.
 */
export type Preset = 'public' | 'admin' | 'locked' | 'custom'

export interface PolicyState {
  table_name: string
  preset: Preset
  /** 쓰기가 무조건 허용된 상태(`USING (true)` 인 ALL/INSERT/UPDATE/DELETE). 화면에서 경고한다. */
  unsafe: boolean
  /** 실제 정책 목록 — 왜 custom 인지 사람이 판단할 수 있게. */
  detail: string
}

export const PRESET_LABEL: Record<Preset, string> = {
  public: '공개 데이터',
  admin: '관리 전용',
  locked: '잠금',
  custom: '사용자 지정',
}

function client() {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')
  return sb
}

export async function loadPolicies(): Promise<PolicyState[]> {
  const { data, error } = await client().rpc<PolicyState[]>('suparun_policies')
  if (error) throw new Error(error.message)
  return data ?? []
}

export async function setPolicy(table: string, preset: Exclude<Preset, 'custom'>): Promise<void> {
  const { error } = await client().rpc('suparun_set_policy', {
    p_table: table,
    p_preset: preset,
  })
  if (error) throw new Error(error.message)
}
