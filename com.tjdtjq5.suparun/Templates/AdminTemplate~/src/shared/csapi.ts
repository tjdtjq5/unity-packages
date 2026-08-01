import { bridgeAvailable } from './bridge'
import { isPreview } from './env'
import { loadMeta } from './meta'
import { ops } from './ops'
import { accessToken } from './supabase'

/**
 * CS 액션 호출 (③ 트랙 #38) — 서버의 롤 게이트·감사 엔드포인트를 부른다.
 *
 * 버튼 목록의 진실은 메타(`suparun_meta.cs_actions`)다 — 서버 코드젠이 [CsAction] 메서드와
 * 시스템 액션(밴·이름·개발자·리셋·GDPR)을 배포 시점에 밀어 넣는다. 화면은 그걸 그릴 뿐이라
 * 게임이 액션을 늘려도 어드민 코드는 그대로다.
 *
 * 대상 서버: 호스팅본은 **자기를 서빙하는 그 서버**(같은 오리진), 로컬은 편집 환경의
 * Cloud Run URL(ops.state). 인증은 어드민 로그인 세션의 Supabase JWT — 서버가 검증하고
 * 롤은 admin_user_role 표에서 매 호출 조회한다.
 */

export interface CsParam {
  name: string
  type: string
}

export interface CsAction {
  service: string
  method: string
  path: string
  label: string
  seniorOnly: boolean
  dangerous: boolean
  params: CsParam[]
}

/** 디자인 미리보기용 표본 — 실제 목록은 메타에서 온다. */
const PREVIEW_ACTIONS: CsAction[] = [
  { service: 'cs_tools_service', method: 'GrantCurrency', path: 'api/cs_tools_service/GrantCurrency', label: '재화 지급', seniorOnly: false, dangerous: false, params: [{ name: 'playerId', type: 'string' }, { name: 'currencyId', type: 'string' }, { name: 'amount', type: 'number' }, { name: 'reason', type: 'string' }] },
  { service: 'system', method: 'SetBan', path: 'api/cs/system/SetBan', label: '밴/해제', seniorOnly: false, dangerous: true, params: [{ name: 'playerId', type: 'string' }, { name: 'banned', type: 'bool' }, { name: 'reason', type: 'string' }, { name: 'bannedUntil', type: 'number' }] },
  { service: 'system', method: 'GdprDelete', path: 'api/cs/system/GdprDelete', label: 'GDPR 계정 삭제', seniorOnly: true, dangerous: true, params: [{ name: 'playerId', type: 'string' }] },
]

export async function loadCsActions(): Promise<CsAction[]> {
  if (isPreview()) return PREVIEW_ACTIONS
  const m = await loadMeta(['cs_actions'])
  return (m.cs_actions as CsAction[] | undefined) ?? []
}

let cachedBase: string | null = null

/** CS 서버 기준 URL. 호스팅본 = 같은 오리진(빈 문자열), 로컬 = 편집 환경의 Cloud Run URL. */
async function csBase(): Promise<string> {
  if (!bridgeAvailable()) return ''
  if (cachedBase !== null) return cachedBase
  const s = await ops.state()
  const env = s.environments.find((e) => e.name === s.editorEnv)
  cachedBase = (env?.cloudRunUrl ?? '').replace(/\/+$/, '')
  return cachedBase
}

export async function runCsAction(action: CsAction, params: Record<string, unknown>): Promise<unknown> {
  if (isPreview()) return { ok: true }
  const base = await csBase()
  if (bridgeAvailable() && !base)
    throw new Error('이 환경의 Cloud Run URL 이 없습니다 — 서버 배포가 먼저입니다.')
  const token = await accessToken()
  if (!token) throw new Error('로그인 세션이 없습니다.')

  const res = await fetch(`${base}/${action.path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify(params),
  })
  const text = await res.text()
  let parsed: unknown = null
  try {
    parsed = text ? JSON.parse(text) : null
  } catch {
    /* 프록시가 JSON 아닌 본문을 줄 수 있다 — 아래에서 상태코드로 던진다 */
  }
  if (!res.ok) {
    const err = (parsed ?? {}) as { error?: string }
    throw new Error(err.error || `HTTP ${res.status}`)
  }
  return parsed
}
