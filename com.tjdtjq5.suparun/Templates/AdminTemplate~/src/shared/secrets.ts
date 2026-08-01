import { sb } from './supabase'

/**
 * 팀이 공유해야 하는 비밀.
 *
 * **값은 절대 읽어 오지 않는다** — `suparun_secret` 에는 SELECT 정책이 아예 없다.
 * 예전에는 `admin_all(FOR ALL)` 이라 관리자로 로그인하면 브라우저에서 PAT 까지 그대로 읽혔고,
 * 그건 관리자 계정 하나가 곧 Supabase 계정 전체라는 뜻이었다.
 *
 * 그래서 목록은 값을 뺀 RPC(`suparun_secret_list`)로만 온다. 입력칸이 비어 있는 것이 정상이고,
 * 빈칸으로 저장하면 기존 값을 그대로 둔다.
 *
 * **PAT 는 여기 없다.** 개인 계정 토큰이라 팀이 돌려쓰지 않는다 — 각자 발급해 자기 Unity 에만 둔다.
 */

export interface SecretMeta {
  key: string
  updatedAt: number | null
  updatedBy: string | null
}

export interface KnownSecret {
  key: string
  label: string
  hint: string
  /**
   * 모든 환경이 같은 값을 쓴다. Unity 는 이미 그렇게 다룬다(`SupaRunSecretPrefs` 에 env 없이 저장).
   * 표 자체는 환경별이라 편집 환경 DB 에 담기지만, **의미는 공통**이라 화면이 그렇게 말해야 한다.
   */
  shared?: boolean
  /** 사람이 정할 이유가 없는 값. 만들어 주는 편이 낫다. */
  generate?: boolean
  /** 값을 얻으러 갈 곳. 링크 하나가 설명 세 줄보다 낫다. */
  linkLabel?: string
  link?: (projectRef: string) => string
}

/** SupaRun 이 쓰는 것들. DB 에 이 목록 밖의 키가 있으면 그대로 덧붙여 보여준다. */
export const KNOWN_SECRETS: KnownSecret[] = [
  {
    key: 'supabase_db_password',
    label: 'Supabase DB 비밀번호',
    hint: '배포된 서버가 DB 에 붙을 때 씁니다. 프로젝트를 만들 때 정한 값입니다.',
    // 우리가 알아낼 방법이 없다 — Supabase 도 되돌려주지 않는다. 잊었으면 리셋뿐이다.
    linkLabel: 'Supabase 에서 리셋',
    link: (ref) => `https://supabase.com/dashboard/project/${ref}/settings/database`,
  },
  {
    key: 'github_token',
    label: 'GitHub 토큰',
    hint: '서버 코드를 push 하고 Actions 를 돌릴 때 씁니다.',
    shared: true,
    // 필요한 권한(repo·workflow)을 URL 에 미리 채운다. 체크박스를 찾아 헤매지 않도록.
    linkLabel: '토큰 발급 (권한 미리 채움)',
    link: () =>
      'https://github.com/settings/tokens/new?scopes=repo,workflow&description=SupaRun',
  },
  {
    key: 'cron_secret',
    label: 'Cron Secret',
    hint: '스케줄 호출이 진짜 우리 것인지 확인하는 값입니다.',
    // 길고 무작위면 그만이다. 사람이 고민할 값이 아니다.
    generate: true,
  },
]

/** 무작위 비밀 한 줄. 브라우저 crypto 로 만든다 — 브리지를 거칠 이유가 없다. */
export function generateSecret(bytes = 24): string {
  const buf = new Uint8Array(bytes)
  crypto.getRandomValues(buf)
  return btoa(String.fromCharCode(...buf)).replace(/[+/=]/g, '').slice(0, 32)
}

export async function loadSecretMeta(): Promise<SecretMeta[]> {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')

  const { data, error } = await sb.rpc<{ key: string; updated_at: number; updated_by: string }[]>(
    'suparun_secret_list',
  )
  if (error) throw new Error(error.message)

  return (data ?? []).map((r) => ({
    key: r.key,
    updatedAt: r.updated_at ?? null,
    updatedBy: r.updated_by ?? null,
  }))
}

/**
 * 값을 넣거나 바꾼다.
 *
 * `.select()` 를 붙이지 않는 것이 중요하다 — supabase-js 는 그때만 `return=minimal` 로 보낸다.
 * 표에 SELECT 정책이 없으므로 돌려받으려 하면 RLS 에 막힌다.
 */
export async function saveSecret(key: string, value: string, by: string): Promise<void> {
  if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')

  const { error } = await sb
    .from('suparun_secret')
    .upsert({ key, value, updated_at: Date.now(), updated_by: by }, { onConflict: 'key' })
  if (error) throw new Error(error.message)
}
