import { isPreview } from './env'
import { sb } from './supabase'

/**
 * 어드민 메타데이터 로더 (ADR-0004 결정 6·7).
 *
 * 서버 `/admin/api/config/_types` 를 대체한다. Unity 가 컴파일할 때
 * `suparun_meta` 테이블에 밀어 넣은 것을 그대로 읽는다 — 그래서 서버 재배포 없이 반영된다.
 *
 * `suparun_meta` 는 `public_read` 라 로그인 전에도 읽힌다. 스키마 정보일 뿐 비밀이 아니고,
 * 로그인 화면에서 이미 화면 골격이 필요하기 때문이다.
 */

interface MetaRow {
  key: string
  value: unknown
}

/**
 * 필요한 key 들을 한 번에 읽는다. 테이블이 아직 없거나(첫 배포 전) 행이 비어 있으면
 * 빈 값으로 돌려준다 — 어드민이 "설정이 없다"는 화면이라도 뜨는 편이 낫다.
 */
export async function loadMeta(keys: string[]): Promise<Record<string, unknown>> {
  if (isPreview()) {
    // 프리뷰는 Supabase 없이 도는 모드다. mock 이 서버 경로를 흉내내므로 그쪽에서 가져온다.
    const out: Record<string, unknown> = {}
    if (keys.includes('config_types')) out.config_types = await window.__previewApi!('/_types')
    if (keys.includes('table_types')) out.table_types = await window.__previewTableApi!('/_types')
    if (keys.includes('icons')) out.icons = await window.__previewApi!('/_icons')
    if (keys.includes('components')) out.components = await window.__previewApi!('/_components')
    return out
  }

  if (!sb) return {}

  const { data, error } = await sb
    .from('suparun_meta')
    .select<MetaRow[]>('key, value')
    .in('key', keys)

  if (error) {
    // 첫 스키마 반영 전이면 테이블 자체가 없다. 화면은 뜨되 비어 보이게 둔다.
    console.warn('[suparun-admin] suparun_meta 읽기 실패 —', error.message)
    return {}
  }

  const out: Record<string, unknown> = {}
  for (const row of data ?? []) out[row.key] = row.value
  return out
}
