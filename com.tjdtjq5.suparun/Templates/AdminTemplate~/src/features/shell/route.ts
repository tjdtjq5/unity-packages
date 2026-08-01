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
  // presetType: 감사 카드(#28)의 "전체 보기" 가 대상 타입 필터를 미리 걸어 보낸다.
  // 해시에는 싣지 않는다 — 새로고침하면 전체 목록이며 그걸로 충분하다.
  | { kind: 'audit'; presetType?: string }
  // 감사 이벤트 상세 (#26). URL 로 직접 접근 가능해야 해서 id 가 해시에 실린다.
  | { kind: 'auditDetail'; id: string }
  | { kind: 'snapshots' }
  // 비밀은 **이 환경의 데이터**다(`suparun_secret` 은 각 프로젝트 안의 표).
  // 설정과 화면을 나누는 이유는 따로다 — 드나드는 빈도가 다르고, 나중에 권한을 쪼갤 자리이기도 하다.
  | { kind: 'secrets' }
  // 서버 로그. 이 환경의 서버가 남긴 것이라 환경 안이다.
  | { kind: 'logs' }
  // Unity 를 시키는 화면(스키마 반영·배포·승격). 대상이 이 환경이므로 역시 환경 안이다.
  | { kind: 'ops' }
  // 롤 부여/회수 (#24). 명단(admin_user)이 환경(프로젝트)별 표라 환경 안이다.
  | { kind: 'roles' }
  // config 버전 목록·게시 (#30·#31·#34). 버전이 이 환경 안의 스냅샷이라 환경 안이다.
  | { kind: 'versions' }
  // 버전 비교 (#32·#33). base/next 는 메모리로만 넘긴다 — 해시 좌표는 새로고침에 안 남아도 된다.
  | { kind: 'compare'; base?: string; next?: string }
  // 릴리스 매니페스트 (#51). 릴리스가 환경 안의 표라 환경 안이다.
  | { kind: 'releases' }
  // 플레이어 검색·목록 (#36, Metaplay Manage Players 동형).
  | { kind: 'players' }
  // 플레이어 상세 (#37). URL 로 직접 접근 가능해야 해서 id 가 해시에 실린다.
  | { kind: 'player'; id: string }
  // 설정도 환경 안이다. 이름·로그인·배포·위험 영역 전부 **이 프로젝트**의 값이라,
  // 앱 레벨에 두면 "환경을 고르기 전인데 어느 환경을 고치는가" 가 어긋난다(실제로 어긋나 있었다).
  | { kind: 'envSettings' }
  // ── 환경을 고르기 전(앱 레벨) 화면들 ──
  // 이들만 "어느 환경인가" 와 무관하다. 나머지는 전부 특정 Supabase 프로젝트의 데이터다.
  | { kind: 'environments' }
  // 미연결 프로젝트의 셋업. 아직 환경이 아니라서 앱 레벨이다 — 이름을 붙이고 스키마를
  // 반영해야 비로소 환경이 된다. 브리지(Unity)가 있어야만 동작한다.
  | { kind: 'setup'; projectRef: string }

/**
 * 환경을 고르기 전 상태인가. 사이드바가 이걸로 갈린다 —
 * 고르기 전에는 특정 Supabase 프로젝트의 데이터를 보여줄 수 없다.
 */
export function isAppLevel(r: Route): boolean {
  return r.kind === 'environments' || r.kind === 'setup'
}

export function routeToHash(r: Route): string | null {
  switch (r.kind) {
    case 'config':
      return 'config/' + r.tableName
    case 'table':
      return 'table/' + r.tableName
    case 'audit':
      return 'audit_log'
    case 'auditDetail':
      return 'audit_log/' + r.id
    case 'snapshots':
      return 'snapshots'
    case 'environments':
      return 'environments'
    case 'setup':
      return 'setup/' + r.projectRef
    case 'envSettings':
      return 'settings'
    case 'secrets':
      return 'secrets'
    case 'logs':
      return 'logs'
    case 'ops':
      return 'ops'
    case 'roles':
      return 'user_roles'
    case 'versions':
      return 'game_configs'
    case 'compare':
      return 'game_configs/compare'
    case 'releases':
      return 'releases'
    case 'players':
      return 'players'
    case 'player':
      return 'players/' + r.id
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
  if (h.startsWith('setup/')) return { kind: 'setup', projectRef: h.slice(6) }
  if (h.startsWith('audit_log/')) return { kind: 'auditDetail', id: h.slice(10) }
  if (h === 'audit_log') return { kind: 'audit' }
  if (h === 'snapshots') return { kind: 'snapshots' }
  if (h === 'environments') return { kind: 'environments' }
  if (h === 'settings') return { kind: 'envSettings' }
  if (h === 'secrets') return { kind: 'secrets' }
  if (h === 'logs') return { kind: 'logs' }
  if (h === 'ops') return { kind: 'ops' }
  if (h === 'user_roles') return { kind: 'roles' }
  if (h === 'game_configs/compare') return { kind: 'compare' }
  if (h === 'game_configs') return { kind: 'versions' }
  if (h === 'releases') return { kind: 'releases' }
  if (h.startsWith('players/')) return { kind: 'player', id: h.slice(8) }
  if (h === 'players') return { kind: 'players' }
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
