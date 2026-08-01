import { useCallback, useEffect, useMemo, useState } from 'react'
import { AuditPage } from '../audit/AuditPage'
import { ConfigPage } from '../config/ConfigPage'
import { DashboardPage } from '../dashboard/DashboardPage'
import { EnvSettingsPage } from '../environment/EnvSettingsPage'
import { EnvironmentPage } from '../environment/EnvironmentPage'
import { LogsPage } from '../logs/LogsPage'
import { OpsPage } from '../ops/OpsPage'
import { SetupProjectPage } from '../setup/SetupProjectPage'
import { LoadingBlock } from '../../shared/Spinner'
import { SecretsPage } from '../secrets/SecretsPage'
import { SnapshotPage } from '../snapshot/SnapshotPage'
import { TablePage } from '../table/TablePage'
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
 * 어드민 껍데기 — titlebar / 사이드바 / 페이지 헤더 / 콘텐츠 영역 / 키맵.
 * 바닐라의 `#admin-page` HTML 과 showAdmin·renderSidebar·showToolbar·selectType·
 * show* 진입점들을 통째로 대체한다.
 *
 * email 은 기계 계정 신원(사람.머신@suparun.local)이다. 세션은 브리지가 만들어 주입한다.
 * 사이드바는 Metaplay IA(ADR-0008)를 따라 Game / LiveOps / Technical 3그룹이고,
 * 하단의 log out 은 자리만 있다 — 사람 로그인(ADR-0009, #23)이 들어오면 활성화된다.
 */
export function Shell({ email }: { email: string }) {
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
    setRoute(
      hashToRoute(
        location.hash,
        (tn) => data.types.some((t) => t.tableName === tn),
        (tn) => data.tableTypes.some((t) => t.tableName === tn),
      ),
    )
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

  const ctx = useMemo<AdminContextValue>(
    () => ({
      types: data.types,
      tableTypes: data.tableTypes,
      fkSources: data.fkSources,
      rewardSources: data.rewardSources,
      typeCatalog: data.typeCatalog,
      setPageSubtitle: setSubtitle,
      navigate,
      setToolbarActions: setActions,
    }),
    [data.types, data.tableTypes, data.fkSources, data.rewardSources, data.typeCatalog, navigate],
  )

  const view = describeRoute(route, data.types, data.tableTypes)
  const appLevel = isAppLevel(route)
  // 이 어드민이 붙은 환경 이름. 환경 안에 있을 때 사이드바 맨 위에 띄운다.
  const envLabel = data.envName || '환경'
  // prod 계열 환경이면 타이틀바가 스스로 경고색을 입는다 (Metaplay 헤더 색 구분 동형, #20).
  // 환경 오인 조작 방지가 목적 — 이름 규약(prod 포함)으로 판별한다.
  const isProd = !appLevel && /prod/i.test(envLabel)

  return (
    <AdminProvider value={ctx}>
      <div className={`terminal-titlebar${isProd ? ' env-prod' : ''}`} style={{ position: 'fixed', top: 0, left: 0, right: 0, zIndex: 1000 }}>
        <span>
          <span className="dot" />
          <span className="title">SUPARUN.ADMIN :: </span>
          {/* 환경 안일 때만 경로에 환경이 낀다 — 고르기 전에는 보여줄 환경이 없다. */}
          {!appLevel && (
            <>
              <EnvSwitcher
                label={envLabel}
                onGoEnvironments={() => navigate({ kind: 'environments' })}
              />
              <span className="title"> › </span>
            </>
          )}
          <span className="title">{view.context}</span>
        </span>
        {/* 스냅샷 저장 버튼은 여기 없다 — snapshots 화면의 [지금 저장] 과 같은 일을 했다.
            타이틀바는 "지금 어디에 누구로, 언제인가" 만 말한다. */}
        <span className="meta">
          <TitlebarClock />
          v0.7.0 / {email || '—'}
        </span>
      </div>

      <aside
        className="navbar navbar-vertical navbar-expand-lg navbar-dark"
        style={{ marginTop: 32 }}
      >
        <div className="container-fluid">
          <h1 className="navbar-brand">
            <i className="ti ti-server-bolt me-2" />
            <span className="nav-link-title">SupaRun.ADMIN</span>
          </h1>
          <div className="collapse navbar-collapse" id="sidebar-menu">
            {/* 환경을 고르기 전에는 앱 레벨 항목만 보인다 —
                나머지는 전부 특정 Supabase 프로젝트의 데이터라 어느 것을 보여줄지 정해지지 않는다. */}
            {appLevel ? (
              <>
                <div className="sidebar-prompt">
                  ~/ <span className="dim">$ select env</span>
                </div>
                <div className="tree-list">
                  {/* settings 는 여기 없다 — 설정은 전부 특정 프로젝트의 값이라 환경 안으로 갔다.
                      admins 도 없다 — 사람 관리는 Supabase 조직 멤버십(각자 PAT)이 맡는다. */}
                  <a
                    className={`tree-item${route.kind === 'environments' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'environments' })
                    }}
                  >
                    <span className="branch">└─</span>
                    <span className="label">environments</span>
                  </a>
                </div>
              </>
            ) : (
              <>
                {/* 환경 안. 어디에 들어와 있는지 항상 보이고, 눌러서 나갈 수 있다. */}
                <a
                  className="sidebar-envchip"
                  href="#"
                  onClick={(e) => {
                    e.preventDefault()
                    navigate({ kind: 'environments' })
                  }}
                  title="환경 선택으로 돌아가기"
                >
                  <i className="ti ti-chevron-left me-1" />
                  {envLabel}
                </a>

                <div className="sidebar-prompt">
                  ~/admin <span className="dim">$ ls -la</span>
                </div>
                {/* Metaplay IA(ADR-0008) — Game / LiveOps / Technical 3그룹.
                    Game 안의 세부 그룹([PERKS] 등)과 TABLES 는 Sidebar 가 그린다. */}
                <div className="tree-section">[GAME]</div>
                <div className="tree-list">
                  <Sidebar
                    types={data.types}
                    tableTypes={data.tableTypes}
                    route={route}
                    onNavigate={navigate}
                    ready={data.ready}
                  />
                </div>

                <div className="tree-section">[LIVEOPS]</div>
                <div className="tree-list">
                  {/* 메일·이벤트·실험·세그먼트가 올 자리(#46). 숨기지 않고 자리를 보여준다 —
                      "미설정 기능도 메뉴에 노출" (Metaplay 투어 §3-6, PRD 스토리 52). */}
                  <span className="tree-item muted" title="라이브옵스 기능은 아직 준비 중입니다">
                    <span className="branch">└─</span>
                    <span className="label">not enabled</span>
                  </span>
                </div>

                <div className="tree-section">[TECHNICAL]</div>
                <div className="tree-list">
                  <a
                    className={`tree-item${route.kind === 'audit' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'audit' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">audit_log</span>
                  </a>
                  <a
                    className={`tree-item${route.kind === 'snapshots' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'snapshots' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">snapshots</span>
                  </a>
                  {/* 비밀은 이 환경의 데이터다 — `suparun_secret` 은 각 Supabase 프로젝트 안의 표다. */}
                  <a
                    className={`tree-item${route.kind === 'secrets' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'secrets' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">secrets</span>
                  </a>
                  <a
                    className={`tree-item${route.kind === 'logs' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'logs' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">server_log</span>
                  </a>
                  {/* 운영·설정은 맨 아래다 — 되돌리기 어려운 일들이라 지나가다 누르는 자리가 아니다. */}
                  <a
                    className={`tree-item${route.kind === 'ops' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'ops' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">ops</span>
                  </a>
                  {/* 이 환경의 설정. 앱 레벨이 아니다 — 내용물이 전부 이 프로젝트의 값이다. */}
                  <a
                    className={`tree-item${route.kind === 'envSettings' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'envSettings' })
                    }}
                  >
                    <span className="branch">└─</span>
                    <span className="label">settings</span>
                  </a>
                </div>

                <div className="tree-list" style={{ marginTop: 8 }}>
                  {/* 자리만 있다 — 사람 로그인(ADR-0009, #23)이 들어오면 활성화된다. */}
                  <span className="tree-item muted" title="사람 로그인 도입(#23) 후 활성화">
                    <span className="branch">└─</span>
                    <span className="label">log out</span>
                  </span>
                </div>
              </>
            )}

            <div className="sidebar-status">
              <div className="row">
                <span className="lbl">conn</span> <span className="ok">●</span> <span>live</span>
              </div>
              <div className="row">
                <span className="lbl">user</span> <span>{email || '—'}</span>
              </div>
              {/* env 행은 없다 — "dev" 로 하드코딩돼 prod 에서도 dev 라고 말하던 자리다.
                  지금은 타이틀바의 환경 전환기가 진실을 보여준다.
                  LOGOUT 은 사이드바 하단에 자리만 있다 — 사람 로그인(#23) 전까지 비활성. */}
              <div className="row">
                <span className="lbl">ver </span> <span>0.7.0</span>
              </div>
            </div>
          </div>
        </div>
      </aside>

      <div className="page-wrapper page-transition" style={{ marginTop: 32 }}>
        <div className="terminal-prompt">
          <span className="user">admin@suparun</span>
          <span className="sep">:</span>
          <span className="path">{view.path}</span>
          <span className="sep">$</span> <span className="cmd">inspect</span>{' '}
          <span className="arg">{view.arg}</span>
          <span className="cursor">_</span>
        </div>

        <PageHeader
          title={view.title}
          subtitle={subtitle}
          supabaseTable={view.supabaseTable}
          search={search}
          onSearch={setSearch}
          actions={actions}
        />

        <div className="page-body">
          <div className="container-xl page-fade">
            <div className="card shadow-sm">
              <div id={CONTENT_ID} className="table-responsive">
                <ScreenContent route={route} data={data} search={search} />
              </div>
            </div>
          </div>
        </div>
      </div>

      {helpOpen && <KeymapHelp onClose={() => setHelpOpen(false)} />}
    </AdminProvider>
  )
}

/** 라우트에서 껍데기 표시값을 뽑는다. */
function describeRoute(
  route: Route,
  types: AdminContextValue['types'],
  tableTypes: AdminContextValue['tableTypes'],
): { title: string; context: string; path: string; arg: string; supabaseTable: string | null } {
  const shell = (title: string, ctx: string, path: string) => ({
    title,
    context: ctx,
    path,
    arg: '--list-all',
    supabaseTable: null,
  })
  switch (route.kind) {
    case 'config': {
      const t = types.find((x) => x.tableName === route.tableName)
      return {
        title: t?.name ?? route.tableName,
        context: `${route.tableName.toUpperCase()}.SH`,
        path: `~/configs/${route.tableName}`,
        arg: '--list-all',
        supabaseTable: route.tableName,
      }
    }
    case 'table': {
      const t = tableTypes.find((x) => x.tableName === route.tableName)
      return {
        title: t?.name ?? route.tableName,
        context: `${route.tableName.toUpperCase()}.SH`,
        path: `~/tables/${route.tableName}`,
        arg: '--list-all',
        supabaseTable: route.tableName,
      }
    }
    case 'audit':
      return shell('변경 이력', 'AUDIT_LOG.SH', '~/audit_log')
    case 'snapshots':
      return shell('스냅샷', 'SNAPSHOTS.SH', '~/snapshots')
    case 'environments':
      return shell('환경', 'SELECT.SH', '~/environments')
    case 'setup':
      return shell('셋업', 'SETUP.SH', `~/setup/${route.projectRef}`)
    case 'envSettings':
      return shell('설정', 'SETTINGS.SH', '~/settings')
    case 'secrets':
      return shell('공유 비밀', 'SECRETS.SH', '~/secrets')
    case 'logs':
      return shell('서버 로그', 'SERVER_LOG.SH', '~/server_log')
    case 'ops':
      return shell('운영', 'OPS.SH', '~/ops')
    case 'home':
      return shell('대시보드', 'DASHBOARD.SH', '~/admin')
  }
}

function ScreenContent({
  route,
  data,
  search,
}: {
  route: Route
  data: ReturnType<typeof useAdminData>
  search: string
}) {
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
      return <AuditPage />
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
