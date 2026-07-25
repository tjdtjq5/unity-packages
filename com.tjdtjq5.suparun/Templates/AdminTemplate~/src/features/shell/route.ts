/**
 * 화면 라우팅. 바닐라 setViewHash / restoreFromHash 를 대체한다.
 *
 * react-router 를 쓰지 않는 이유: 화면이 6개뿐이고 해시 하나로 표현되며,
 * 중첩 라우트도 로더도 필요 없다. 아래 30줄이 전부다.
 *
 * 바닐라와 동일하게 **replaceState** 를 쓴다(pushState 아님) — 뒤로가기로 화면을
 * 되짚지 않는 것이 기존 동작이다.
 */

export type Route =
  | { kind: 'home' }
  | { kind: 'config'; tableName: string }
  | { kind: 'table'; tableName: string }
  | { kind: 'admins' }
  | { kind: 'audit' }
  | { kind: 'cross' }
  | { kind: 'player'; userId?: string }

export function routeToHash(r: Route): string | null {
  switch (r.kind) {
    case 'config':
      return 'config/' + r.tableName
    case 'table':
      return 'table/' + r.tableName
    case 'admins':
      return 'admins'
    case 'audit':
      return 'audit_log'
    case 'cross':
      return 'cross_search'
    case 'player':
      return 'player_search'
    case 'home':
      return null
  }
}

/**
 * 해시 → Route. 존재하지 않는 테이블을 가리키면 home 으로 떨어뜨린다
 * (Config 를 삭제/리네임한 뒤 옛 해시로 들어오는 경우 방어 — 바닐라와 동일).
 */
export function hashToRoute(
  hash: string,
  isKnownConfig: (tableName: string) => boolean,
  isKnownTable: (tableName: string) => boolean,
): Route {
  const h = decodeURIComponent((hash || '').replace(/^#/, ''))
  if (h.startsWith('config/')) {
    const tn = h.slice(7)
    return isKnownConfig(tn) ? { kind: 'config', tableName: tn } : { kind: 'home' }
  }
  if (h.startsWith('table/')) {
    const tn = h.slice(6)
    return isKnownTable(tn) ? { kind: 'table', tableName: tn } : { kind: 'home' }
  }
  if (h === 'player_search') return { kind: 'player' }
  if (h === 'cross_search') return { kind: 'cross' }
  if (h === 'admins') return { kind: 'admins' }
  if (h === 'audit_log') return { kind: 'audit' }
  return { kind: 'home' }
}

export function writeHash(r: Route): void {
  const h = routeToHash(r)
  try {
    history.replaceState(null, '', h ? '#' + h : location.pathname + location.search)
  } catch {
    /* file:// 등에서 실패할 수 있다 — 라우팅 자체는 메모리 상태로 동작한다 */
  }
}
