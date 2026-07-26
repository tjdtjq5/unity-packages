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
  | { kind: 'snapshots' }
  // ── 환경을 고르기 전(앱 레벨) 화면들 ──
  // 이 둘만 "어느 환경인가" 와 무관하다. 나머지는 전부 특정 Supabase 프로젝트의 데이터다.
  | { kind: 'environments' }
  | { kind: 'appSettings' }

/**
 * 환경을 고르기 전 상태인가. 사이드바가 이걸로 갈린다 —
 * 고르기 전에는 특정 Supabase 프로젝트의 데이터를 보여줄 수 없다.
 */
export function isAppLevel(r: Route): boolean {
  return r.kind === 'environments' || r.kind === 'appSettings'
}

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
    case 'snapshots':
      return 'snapshots'
    case 'environments':
      return 'environments'
    case 'appSettings':
      return 'settings'
    // home 은 해시를 남긴다 — 안 그러면 빈 해시가 되어 다시 환경 선택으로 돌아간다.
    case 'home':
      return 'home'
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
  if (h === 'admins') return { kind: 'admins' }
  if (h === 'audit_log') return { kind: 'audit' }
  if (h === 'snapshots') return { kind: 'snapshots' }
  if (h === 'environments') return { kind: 'environments' }
  if (h === 'settings') return { kind: 'appSettings' }
  if (h === 'home') return { kind: 'home' }

  // 해시 없이 들어오면 **환경 선택**부터다. 어느 환경을 보고 있는지 모른 채
  // 데이터를 고치기 시작하는 것이 이 프로젝트에서 가장 비싼 실수다.
  return { kind: 'environments' }
}

export function writeHash(r: Route): void {
  const h = routeToHash(r)
  try {
    history.replaceState(null, '', h ? '#' + h : location.pathname + location.search)
  } catch {
    /* file:// 등에서 실패할 수 있다 — 라우팅 자체는 메모리 상태로 동작한다 */
  }
}
