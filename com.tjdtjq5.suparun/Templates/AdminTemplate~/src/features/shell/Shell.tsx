import { useCallback, useEffect, useMemo, useState } from 'react'
import { AuditDetailPage } from '../audit/AuditDetailPage'
import { AuditPage } from '../audit/AuditPage'
import { ConfigPage } from '../config/ConfigPage'
import { DashboardPage } from '../dashboard/DashboardPage'
import { EnvSettingsPage } from '../environment/EnvSettingsPage'
import { EnvironmentPage } from '../environment/EnvironmentPage'
import { LogsPage } from '../logs/LogsPage'
import { OpsPage } from '../ops/OpsPage'
import { DeveloperPlayersPage } from '../players/DeveloperPlayersPage'
import { PlayerDetailPage } from '../players/PlayerDetailPage'
import { PlayersPage } from '../players/PlayersPage'
import { RolesPage } from '../roles/RolesPage'
import { SegmentDetailPage } from '../segments/SegmentDetailPage'
import { SegmentsPage } from '../segments/SegmentsPage'
import { SetupProjectPage } from '../setup/SetupProjectPage'
import { ComparePage } from '../versions/ComparePage'
import { ReleasesPage } from '../versions/ReleasesPage'
import { VersionsPage } from '../versions/VersionsPage'
import { LoadingBlock } from '../../shared/Spinner'
import { SecretsPage } from '../secrets/SecretsPage'
import { SnapshotPage } from '../snapshot/SnapshotPage'
import { TablePage } from '../table/TablePage'
import { opsVisible } from '../../shared/bridge'
import { AdminProvider, type AdminContextValue, type ToolbarActions } from './AdminContext'
import { EnvSwitcher } from './EnvSwitcher'
import { KeymapHelp } from './KeymapHelp'
import { PageHeader } from './PageHeader'
import { TitlebarClock } from './TitlebarClock'
import { Sidebar, groupConfigTypes } from './Sidebar'
import { hashToRoute, isAppLevel, writeHash, type Route } from './route'
import { useAdminData } from './useAdminData'
import { useKeymap } from './useKeymap'

/** 스크롤 키맵 대상이자 화면 콘텐츠가 들어가는 컨테이너의 id. */
const CONTENT_ID = 'table-container'

/**
 * 어드민 껍데기 — 사이드바 / 탑바 / 페이지 툴바 / 콘텐츠 영역 / 키맵.
 * 레이아웃은 Metaplay 대시보드 클론이다 (ADR-0008, docs/reports/metaplay-screens):
 * 좌측 화이트 사이드바(브랜드+환경 칩, Game/LiveOps/Technical 3그룹, 하단 Log Out),
 * 콘텐츠 상단 탑바(페이지 타이틀 + 로컬/UTC 듀얼 시계 + 아바타).
 *
 * email 은 로그인한 사람 계정이다 (ADR-0009, #23 — 이메일+비밀번호).
 *
 * roles 는 이 사람의 롤 목록(#24). game-admin 이 없으면 조작(Manage) 화면·쓰기 UI 를
 * 걷어낸다 — UI 겹일 뿐, 진짜 거부는 RLS 가 한다. 프리뷰는 전체 UI 를 봐야 하므로 기본값이
 * game-admin 이다.
 */
export function Shell({
  email,
  roles = ['game-admin'],
  onLogout,
}: {
  email: string
  roles?: string[]
  onLogout?: () => void
}) {
  const data = useAdminData()
  const [route, setRoute] = useState<Route>({ kind: 'home' })
  const [restored, setRestored] = useState(false)
  const [subtitle, setSubtitle] = useState('')
  const [search, setSearch] = useState('')
  const [actions, setActions] = useState<ToolbarActions | null>(null)
  const [helpOpen, setHelpOpen] = useState(false)

  // 해시 복원은 타입 목록이 온 뒤 1회. 없는 테이블을 가리키면 home 으로 떨어진다.
  useEffect(() => {
    if (!data.ready || restored) return
    setRestored(true)
    const r = hashToRoute(
      location.hash,
      (tn) => data.types.some((t) => t.tableName === tn),
      (tn) => data.tableTypes.some((t) => t.tableName === tn),
    )
    // 호스팅본(#48)은 자기 환경 하나뿐이라 환경 선택이 무의미하다 — 곧장 환경 안(home)으로.
    setRoute(!opsVisible() && r.kind === 'environments' ? { kind: 'home' } : r)
  }, [data.ready, data.types, data.tableTypes, restored])

  const navigate = useCallback((r: Route) => {
    setRoute(r)
    writeHash(r)
    // 화면이 바뀌면 껍데기 상태는 초기화한다 — 새 화면이 필요한 것만 다시 채운다.
    setSubtitle('')
    setSearch('')
    setActions(null)
  }, [])

  const onCycleConfig = useCallback(
    (dir: 1 | -1) => {
      // 사이드바 표시 순서를 따라야 한다 — 그룹이 있으면 types 배열 순서와 다르다.
      const items = groupConfigTypes(data.types).flatMap((g) => g.items)
      if (!items.length) return
      const cur = route.kind === 'config' ? route.tableName : null
      let idx = items.findIndex((t) => t.tableName === cur)
      if (idx < 0) idx = dir > 0 ? -1 : 0 // 미선택 시 첫/끝으로
      const next = items[(idx + dir + items.length) % items.length]
      if (next) navigate({ kind: 'config', tableName: next.tableName })
    },
    [data.types, route, navigate],
  )

  const onToggleHelp = useCallback(() => setHelpOpen((v) => !v), [])

  useKeymap({ scrollTargetId: CONTENT_ID, onCycleConfig, onToggleHelp })

  const canWrite = roles.includes('game-admin')
  // 승격 전용 판정(#50) — 탑바 경고색(isProd)과 같은 이름 규약을 쓴다.
  const promoteOnly = /prod/i.test(data.envName || '')

  const ctx = useMemo<AdminContextValue>(
    () => ({
      types: data.types,
      tableTypes: data.tableTypes,
      fkSources: data.fkSources,
      rewardSources: data.rewardSources,
      typeCatalog: data.typeCatalog,
      canWrite,
      promoteOnly,
      roles,
      setPageSubtitle: setSubtitle,
      navigate,
      setToolbarActions: setActions,
    }),
    [data.types, data.tableTypes, data.fkSources, data.rewardSources, data.typeCatalog, canWrite, promoteOnly, roles, navigate],
  )

  const view = describeRoute(route, data.types, data.tableTypes)

  // 브라우저 탭 타이틀도 같은 컨벤션을 따른다 (#21) — 탭이 여럿일 때 어느 화면인지 보인다.
  useEffect(() => {
    document.title = `${view.title} — SupaRun Admin`
  }, [view.title])

  const appLevel = isAppLevel(route)
  // 이 어드민이 붙은 환경 이름. 브랜드 아래 환경 칩으로 항상 보인다.
  const envLabel = data.envName || '환경'
  // prod 계열 환경이면 탑바·환경 칩이 스스로 경고색을 입는다 (Metaplay 헤더 색 구분 동형, #20).
  // 환경 오인 조작 방지가 목적 — 판정은 승격 전용(#50)과 같은 이름 규약 하나다.
  const isProd = !appLevel && promoteOnly

  /** 사이드바 항목 하나 — 채움형(FA solid) 아이콘 + 라벨, 활성이면 블루 채움 (Metaplay 동형). */
  const item = (
    label: string,
    icon: string,
    active: boolean,
    to: Route,
  ) => (
    <a
      className={`tree-item${active ? ' active' : ''}`}
      href="#"
      onClick={(e) => {
        e.preventDefault()
        navigate(to)
      }}
    >
      <i className={`fa-solid ${icon}`} />
      <span className="label">{label}</span>
    </a>
  )

  return (
    <AdminProvider value={ctx}>
      <aside className={`mp-sidebar${isProd ? ' env-prod' : ''}`}>
        <div className="mp-brand">
          {/* Metaplay 의 초록 사각 "m" 동형 — 글자 하나가 로고다 */}
          <div className="mp-logo">S</div>
          <div className="mp-brand-text">
            <div className="mp-brand-name">SupaRun</div>
            {/* Metaplay 의 "› demo" 자리 — 환경 안일 때만 보인다. 로컬에선 드롭다운 전환기다. */}
            {!appLevel && (
              <EnvSwitcher
                label={envLabel}
                onGoEnvironments={() => navigate({ kind: 'environments' })}
              />
            )}
          </div>
        </div>

        <nav className="mp-nav">
          {/* 환경을 고르기 전에는 앱 레벨 항목만 보인다 —
              나머지는 전부 특정 Supabase 프로젝트의 데이터라 어느 것을 보여줄지 정해지지 않는다. */}
          {appLevel ? (
            <div className="tree-list">
              {/* settings 는 여기 없다 — 설정은 전부 특정 프로젝트의 값이라 환경 안으로 갔다. */}
              {item('Environments', 'fa-cloud', route.kind === 'environments', { kind: 'environments' })}
            </div>
          ) : (
            <>
              {/* Metaplay IA(ADR-0008) — Game / LiveOps / Technical 3그룹. */}
              <div className="tree-section">Game</div>
              <div className="tree-list">
                {/* 플레이어 (#36·#37, Metaplay Game>Players 동형) — 열람은 전 롤(RPC 가드). */}
                {item('Players', 'fa-users', route.kind === 'players' || route.kind === 'player', { kind: 'players' })}
                {/* Game 안의 세부 그룹([PERKS] 등)과 TABLES 는 Sidebar 가 그린다. */}
                <Sidebar
                  types={data.types}
                  tableTypes={data.tableTypes}
                  route={route}
                  onNavigate={navigate}
                  ready={data.ready}
                />
                {/* 버전·게시 (#30, Metaplay Game Configs 동형). 열람은 전 롤 — 게시 버튼만 canWrite. */}
                {item('Game Configs', 'fa-table-cells', route.kind === 'versions' || route.kind === 'compare', { kind: 'versions' })}
                {/* 릴리스 매니페스트 (#51) — 무엇이 함께 나갔는가. 열람 전 롤, 생성은 로컬+game-admin. */}
                {item('Releases', 'fa-rocket', route.kind === 'releases', { kind: 'releases' })}
              </div>

              <div className="tree-section">LiveOps</div>
              <div className="tree-list">
                {/* 세그먼트 (#44) — 라이브옵스의 첫 실기능. 열람은 전 롤, 쓰기는 game-admin. */}
                {item('Player Segments', 'fa-user-group', route.kind === 'segments' || route.kind === 'segment', { kind: 'segments' })}
                {/* 메일·이벤트·실험이 올 자리(#46). 숨기지 않고 자리를 보여준다 —
                    "미설정 기능도 메뉴에 노출" (Metaplay 투어 §3-6, PRD 스토리 52). */}
                <span className="tree-item muted" title="나머지 라이브옵스 기능은 아직 준비 중입니다">
                  <i className="fa-solid fa-ellipsis" />
                  <span className="label">Not enabled</span>
                </span>
              </div>

              <div className="tree-section">Technical</div>
              <div className="tree-list">
                {/* 조작(Manage) 화면들은 game-admin 만 본다 (#24) — 숨김은 UI 겹이고
                    진짜 거부는 RLS·RPC 가드가 한다. 열람 화면(audit·server_log)은 전 롤. */}
                {/* 개발자 플레이어 (#40) — 열람은 전 롤. 지정은 플레이어 상세의 CS 액션. */}
                {item('Developer Players', 'fa-chalkboard-user', route.kind === 'developers', { kind: 'developers' })}
                {item('Audit Logs', 'fa-book', route.kind === 'audit' || route.kind === 'auditDetail', { kind: 'audit' })}
                {canWrite && item('Snapshots', 'fa-camera', route.kind === 'snapshots', { kind: 'snapshots' })}
                {/* 비밀은 이 환경의 데이터다 — `suparun_secret` 은 각 Supabase 프로젝트 안의 표다. */}
                {canWrite && item('Secrets', 'fa-key', route.kind === 'secrets', { kind: 'secrets' })}
                {item('Server Logs', 'fa-message', route.kind === 'logs', { kind: 'logs' })}
                {/* 사람과 롤 (#24). 명단·부여/회수 전부 game-admin 전용이다. */}
                {canWrite && item('User Roles', 'fa-user-shield', route.kind === 'roles', { kind: 'roles' })}
                {/* 운영·설정은 맨 아래다 — 되돌리기 어려운 일들이라 지나가다 누르는 자리가 아니다.
                    ops 는 Unity 를 시키는 화면이라 호스팅본(#48 — 브리지 없음)에는 아예 없다. */}
                {canWrite && opsVisible() && item('Operations', 'fa-screwdriver-wrench', route.kind === 'ops', { kind: 'ops' })}
                {/* 이 환경의 설정. 앱 레벨이 아니다 — 내용물이 전부 이 프로젝트의 값이다. */}
                {canWrite && item('Settings', 'fa-gear', route.kind === 'envSettings', { kind: 'envSettings' })}
              </div>
            </>
          )}

          {/* 로그아웃은 양쪽 레벨 공통이다 — 로그인 직후 착지가 앱 레벨(환경 선택)이라
              환경 안에만 두면 나갈 방법이 없다 (실기에서 확인). */}
          <div className="tree-list mp-logout">
            {onLogout ? (
              <a
                className="tree-item"
                href="#"
                onClick={(e) => {
                  e.preventDefault()
                  onLogout()
                }}
              >
                <i className="fa-solid fa-right-from-bracket" />
                <span className="label">Log Out</span>
              </a>
            ) : (
              // 프리뷰 — 끊을 세션이 없어 자리만 유지한다.
              <span className="tree-item muted">
                <i className="fa-solid fa-right-from-bracket" />
                <span className="label">Log Out</span>
              </span>
            )}
          </div>
        </nav>

        <div className="sidebar-status">
          <div className="row">
            <span className="lbl">conn</span> <span className="ok">●</span> <span>live</span>
          </div>
          <div className="row">
            <span className="lbl">user</span> <span>{email || '—'}</span>
          </div>
          <div className="row">
            <span className="lbl">ver </span> <span>1.1.0</span>
          </div>
        </div>
      </aside>

      <div className="mp-main">
        {/* Metaplay 탑바 동형 — 페이지 타이틀 / 듀얼 시계 / 아바타.
            타이틀은 View/Manage 컨벤션(#21)이라 위험한 화면인지 여기서 바로 읽힌다. */}
        <header className={`mp-topbar${isProd ? ' env-prod' : ''}`}>
          <h2 className="mp-topbar-title">{view.title}</h2>
          <span className="spacer" />
          <TitlebarClock />
          {/* Metaplay 동형 — 회색 원 + 사람 글리프. 누구인지는 title(이메일)로 말한다. */}
          <div className="mp-avatar" title={email || '미로그인'}>
            <i className="fa-solid fa-user" />
          </div>
        </header>

        <div className="mp-body page-transition">
          <div className="container-xl page-fade">
            <PageHeader
              subtitle={subtitle}
              supabaseTable={view.supabaseTable}
              search={search}
              onSearch={setSearch}
              actions={actions}
            />
            <div className="card">
              <div id={CONTENT_ID} className="table-responsive">
                <ScreenContent route={route} data={data} search={search} canWrite={canWrite} />
              </div>
            </div>
          </div>
        </div>
      </div>

      {helpOpen && <KeymapHelp onClose={() => setHelpOpen(false)} />}
    </AdminProvider>
  )
}

/** 라우트에서 껍데기 표시값(탑바 타이틀·Supabase 링크 대상)을 뽑는다. */
function describeRoute(
  route: Route,
  types: AdminContextValue['types'],
  tableTypes: AdminContextValue['tableTypes'],
): { title: string; supabaseTable: string | null } {
  // View/Manage 타이틀 컨벤션 (#21, Metaplay 동형) — 읽기 화면은 View, 조작 화면은 Manage.
  // 지금 위험한 화면(조작 가능)에 있는지 타이틀만 봐도 알게 한다.
  switch (route.kind) {
    case 'config': {
      const t = types.find((x) => x.tableName === route.tableName)
      return { title: `Manage ${t?.name ?? route.tableName}`, supabaseTable: route.tableName }
    }
    case 'table': {
      // [UserData] 테이블은 읽기 전용 조회다 — 쓰기는 서버([Service])만 한다 (ADR-0004 결정 20)
      const t = tableTypes.find((x) => x.tableName === route.tableName)
      return { title: `View ${t?.name ?? route.tableName}`, supabaseTable: route.tableName }
    }
    case 'audit':
      return { title: 'View Audit Logs', supabaseTable: null }
    case 'auditDetail':
      return { title: 'View Audit Event', supabaseTable: null }
    case 'snapshots':
      return { title: 'Manage Snapshots', supabaseTable: null }
    case 'environments':
      return { title: 'Manage Environments', supabaseTable: null }
    case 'setup':
      return { title: 'Manage Project Setup', supabaseTable: null }
    case 'envSettings':
      return { title: 'Manage Settings', supabaseTable: null }
    case 'secrets':
      return { title: 'Manage Secrets', supabaseTable: null }
    case 'logs':
      return { title: 'View Server Logs', supabaseTable: null }
    case 'ops':
      return { title: 'Manage Operations', supabaseTable: null }
    case 'roles':
      return { title: 'Manage User Roles', supabaseTable: null }
    case 'versions':
      return { title: 'Manage Game Configs', supabaseTable: null }
    case 'compare':
      return { title: 'Compare Game Configs', supabaseTable: null }
    case 'releases':
      return { title: 'Manage Releases', supabaseTable: null }
    case 'players':
      return { title: 'Manage Players', supabaseTable: null }
    case 'player':
      return { title: 'Manage Player', supabaseTable: null }
    case 'developers':
      return { title: 'Developer Players', supabaseTable: null }
    case 'segments':
      return { title: 'Player Segments', supabaseTable: null }
    case 'segment':
      return { title: 'Manage Segment', supabaseTable: null }
    case 'home':
      return { title: 'Overview', supabaseTable: null }
  }
}

function ScreenContent({
  route,
  data,
  search,
  canWrite,
}: {
  route: Route
  data: ReturnType<typeof useAdminData>
  search: string
  canWrite: boolean
}) {
  // 사이드바에서 숨겨도 해시 직접 입력으로 올 수 있다 — 화면 자체도 막는다 (#24).
  // 이것도 UI 겹이다: 진짜 거부는 RLS·RPC 가드·브리지가 한다.
  const adminOnly: Route['kind'][] = ['snapshots', 'secrets', 'ops', 'envSettings', 'roles']
  if (!canWrite && adminOnly.includes(route.kind)) {
    return (
      <div className="empty-state">
        <i className="ti ti-lock" />
        <h3>game-admin 전용 화면입니다</h3>
        <p>현재 롤로는 조작 화면에 들어갈 수 없습니다.</p>
      </div>
    )
  }

  // ops 는 Unity(브리지)를 시키는 화면 — 호스팅본(#48)에는 시킬 Unity 가 없다.
  if ((route.kind === 'ops' || route.kind === 'setup') && !opsVisible()) {
    return (
      <div className="empty-state">
        <i className="ti ti-plug-off" />
        <h3>로컬 어드민 전용 화면입니다</h3>
        <p>이 화면은 Unity 브리지가 필요합니다 — 개발 머신의 어드민에서 여세요.</p>
      </div>
    )
  }

  switch (route.kind) {
    case 'config': {
      const t = data.types.find((x) => x.tableName === route.tableName)
      if (!t) return null
      // key — Config 를 바꾸면 표 상태(편집 중인 셀 등)를 통째로 새로 시작한다
      return <ConfigPage key={t.tableName} configType={t} filter={search} />
    }
    case 'table': {
      const t = data.tableTypes.find((x) => x.tableName === route.tableName)
      return t ? <TablePage key={t.tableName} tableType={t} /> : null
    }
    case 'audit':
      // key — 프리셋이 바뀌면(카드 "전체 보기" ↔ 사이드바 직접 진입) 필터 상태를 새로 시작한다.
      return <AuditPage key={route.presetType ?? ''} presetType={route.presetType} />
    case 'auditDetail':
      return <AuditDetailPage id={route.id} />
    case 'snapshots':
      return <SnapshotPage />
    case 'environments':
      return <EnvironmentPage />
    case 'setup':
      return <SetupProjectPage projectRef={route.projectRef} />
    case 'envSettings':
      return <EnvSettingsPage />
    case 'secrets':
      return <SecretsPage />
    case 'logs':
      return <LogsPage />
    case 'ops':
      return <OpsPage />
    case 'roles':
      return <RolesPage />
    case 'versions':
      return <VersionsPage />
    case 'compare':
      return <ComparePage base={route.base} next={route.next} />
    case 'releases':
      return <ReleasesPage />
    case 'players':
      return <PlayersPage />
    case 'player':
      // key — 다른 플레이어로 이동하면 상태(카드·열람 기록 플래그)를 새로 시작한다
      return <PlayerDetailPage key={route.id} id={route.id} />
    case 'developers':
      return <DeveloperPlayersPage />
    case 'segments':
      return <SegmentsPage />
    case 'segment':
      return <SegmentDetailPage key={route.id} id={route.id} />
    case 'home': {
      // 타입 메타가 오기 전에 "선택하세요"를 띄우면 사이드바가 비어 있는 이유를 오해하게 된다.
      if (!data.ready) return <LoadingBlock label="Config 목록 불러오는 중" />

      if (data.types.length === 0 && data.tableTypes.length === 0) {
        return (
          <div className="empty-state">
            <i className="ti ti-package" />
            <h3>등록된 Config가 없습니다</h3>
            <p>Unity에서 Feature를 설치하고 Deploy하면 여기에 나타납니다.</p>
          </div>
        )
      }
      // 예전에는 여기가 "Config 를 선택하세요" 빈 화면이었다. 들어오자마자 상태가 보이는 편이 낫다.
      return <DashboardPage />
    }
  }
}
