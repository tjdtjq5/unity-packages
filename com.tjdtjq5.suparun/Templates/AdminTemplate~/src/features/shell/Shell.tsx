import { useCallback, useEffect, useMemo, useState } from 'react'
import { AdminsPage } from '../admins/AdminsPage'
import { AuditPage } from '../audit/AuditPage'
import { ConfigPage } from '../config/ConfigPage'
import { DashboardPage } from '../dashboard/DashboardPage'
import { AppSettingsPage } from '../environment/AppSettingsPage'
import { EnvironmentPage } from '../environment/EnvironmentPage'
import { LoadingBlock } from '../../shared/Spinner'
import { SnapshotPage } from '../snapshot/SnapshotPage'
import { QuickSnapshotButton } from '../snapshot/QuickSnapshotButton'
import { TablePage } from '../table/TablePage'
import { AdminProvider, type AdminContextValue, type ToolbarActions } from './AdminContext'
import { KeymapHelp } from './KeymapHelp'
import { PageHeader } from './PageHeader'
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
 * 로그인은 아직 바닐라가 소유한다(5d) — 그래서 email/onLogout 을 인자로 받는다.
 */
export function Shell({
  email,
  onLogout,
  unlocked = false,
}: {
  email: string
  onLogout: () => void
  /** 로그인 없이 열려 있는 상태(로그인 수단 미설정). 배너로 계속 알린다. */
  unlocked?: boolean
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

  return (
    <AdminProvider value={ctx}>
      <div className="terminal-titlebar" style={{ position: 'fixed', top: 0, left: 0, right: 0, zIndex: 1000 }}>
        <span>
          <span className="dot" />
          <span className="title">SUPARUN.ADMIN :: {view.context}</span>
        </span>
        <span className="meta">
          <QuickSnapshotButton />
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
                  <a
                    className={`tree-item${route.kind === 'environments' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'environments' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">environments</span>
                  </a>
                  <a
                    className={`tree-item${route.kind === 'admins' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'admins' })
                    }}
                  >
                    <span className="branch">├─</span>
                    <span className="label">admins</span>
                  </a>
                  <a
                    className={`tree-item${route.kind === 'appSettings' ? ' active' : ''}`}
                    href="#"
                    onClick={(e) => {
                      e.preventDefault()
                      navigate({ kind: 'appSettings' })
                    }}
                  >
                    <span className="branch">└─</span>
                    <span className="label">settings</span>
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
                <div className="tree-list">
                  <Sidebar
                    types={data.types}
                    tableTypes={data.tableTypes}
                    route={route}
                    onNavigate={navigate}
                    ready={data.ready}
                  />
                </div>

                <div className="tree-section">[SYSTEM]</div>
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
                    <span className="branch">└─</span>
                    <span className="label">snapshots</span>
                  </a>
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
              <div className="row">
                <span className="lbl">env </span> <span>dev</span>
              </div>
              <div className="row">
                <span className="lbl">ver </span> <span>0.7.0</span>
              </div>
              {/* 로그인 없이 들어온 상태에서는 나갈 세션이 없다 — 눌러도 아무 일도 안 일어나는
                  버튼을 두면 로그인한 것처럼 오해하게 된다. */}
              {!unlocked && (
                <div style={{ marginTop: 10 }}>
                  <button className="btn btn-sm" style={{ width: '100%' }} onClick={onLogout}>
                    [LOGOUT]
                  </button>
                </div>
              )}
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
    case 'admins':
      return shell('관리자 목록', 'ADMINS.SH', '~/admins')
    case 'audit':
      return shell('변경 이력', 'AUDIT_LOG.SH', '~/audit_log')
    case 'snapshots':
      return shell('스냅샷', 'SNAPSHOTS.SH', '~/snapshots')
    case 'environments':
      return shell('환경', 'SELECT.SH', '~/environments')
    case 'appSettings':
      return shell('설정', 'SETTINGS.SH', '~/settings')
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
    case 'admins':
      return <AdminsPage />
    case 'audit':
      return <AuditPage />
    case 'snapshots':
      return <SnapshotPage />
    case 'environments':
      return <EnvironmentPage />
    case 'appSettings':
      return <AppSettingsPage />
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
