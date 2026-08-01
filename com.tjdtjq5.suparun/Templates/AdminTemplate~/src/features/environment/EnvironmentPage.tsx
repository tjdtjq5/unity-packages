import { useCallback, useEffect, useState } from 'react'
import { listProjects, type SupabaseProject } from '../../shared/projects'
import {
  formatBytes,
  isHealthy,
  levelOf,
  loadEnvironments,
  type EnvironmentInfo,
} from '../../shared/environments'
import { env as suparunEnv } from '../../shared/env'
import { bridgeAvailable } from '../../shared/bridge'
import { ops } from '../../shared/ops'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import { useAdmin } from '../shell/AdminContext'
import { CreateProjectModal } from './CreateProjectModal'

/**
 * 이 어드민 페이지가 붙어 있는 Supabase 프로젝트 ref.
 * 카드가 "여기"인지 "다른 곳"인지를 가르는 기준이다 — 인증이 프로젝트마다 별개라
 * 다른 환경으로 가는 것은 **이동**(재로그인)이지 전환이 아니다.
 */
function currentProjectRef(): string {
  const url = suparunEnv().supabaseUrl
  try {
    return new URL(url).hostname.split('.')[0]
  } catch {
    return ''
  }
}

/**
 * 환경 현황판 — **카드 하나 = 프로젝트 하나**.
 *
 * 두 출처를 겹쳐 그린다:
 *   환경 메타(`suparun_meta`) — Unity 가 넣어 둔 지표. 지표는 Unity 만 모을 수 있다(낡을 수 있음)
 *   프로젝트 목록            — 지금 이 순간의 Supabase 계정 상태
 *
 * 겹치는 이유: 새로 만든 프로젝트는 아직 어느 환경에도 속하지 않아 메타에 없다.
 * 그래도 목록에는 떠야 "만들었는데 안 보인다" 가 없다.
 */
export function EnvironmentPage() {
  const { navigate } = useAdmin()
  const here = currentProjectRef()
  const [envs, setEnvs] = useState<EnvironmentInfo[] | null>(null)
  const [projects, setProjects] = useState<SupabaseProject[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [online, setOnline] = useState(false)
  const [creating, setCreating] = useState(false)

  const reload = useCallback(async () => {
    try {
      setError(null)
      setEnvs(await loadEnvironments())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }

    // 목록은 따로 — 못 받아도 환경 메타로 화면은 떠야 한다.
    try {
      setProjects(await listProjects())
      setOnline(true)
    } catch {
      setProjects(null)
      setOnline(false)
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>환경 정보를 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!envs) return <LoadingBlock label="환경 현황 불러오는 중" />

  // 환경에 연결된 프로젝트 ref → 그 환경. 카드를 합치는 기준이다.
  const byRef = new Map<string, EnvironmentInfo>()
  for (const e of envs) if (e.project_ref) byRef.set(e.project_ref, e)

  // 목록을 받았으면 계정의 모든 프로젝트가 기준이 된다(아직 환경으로 등록 안 한 것 포함).
  // 못 받았으면 아는 것은 환경 메타뿐이다.
  const cards = online && projects
    ? projects.map((p) => ({ key: p.ref, env: byRef.get(p.ref), project: p }))
    : envs.map((e) => ({ key: e.name, env: e, project: undefined as SupabaseProject | undefined }))

  return (
    <div className="env-list">
      <div className="env-intro">
        <div>
          <h2 className="env-heading">환경</h2>
          <p className="env-subheading">들어갈 환경을 고르세요.</p>
        </div>
        <div className="btn-list">
          <button className="btn btn-sm" onClick={() => void reload()}>
            새로고침
          </button>
          <button
            className="btn btn-sm btn-primary"
            disabled={!online}
            title={online ? '' : '프로젝트 목록을 받지 못했습니다'}
            onClick={() => setCreating(true)}
          >
            <i className="ti ti-plus me-1" />
            프로젝트 만들기
          </button>
        </div>
      </div>

      {cards.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-server-2" />
          <h3>표시할 환경이 없습니다</h3>
          <p>Unity 대시보드에서 어드민을 한 번 열면 현황이 기록됩니다.</p>
        </div>
      ) : (
        cards.map((c) => {
          const ref = c.project?.ref ?? c.env?.project_ref
          return (
            <EnvCard
              key={c.key}
              env={c.env}
              project={c.project}
              busy={false}
              isHere={!!here && ref === here}
              onHome={() => navigate({ kind: 'home' })}
              onSetup={() => ref && navigate({ kind: 'setup', projectRef: ref })}
            />
          )
        })
      )}

      {creating && (
        <CreateProjectModal
          onClose={() => setCreating(false)}
          onCreated={async () => {
            setCreating(false)
            await reload()
          }}
        />
      )}
    </div>
  )
}

function EnvCard({
  env: e,
  project: p,
  busy,
  isHere,
  onHome,
  onSetup,
}: {
  env?: EnvironmentInfo
  project?: SupabaseProject
  busy: boolean
  /** 이 어드민이 붙어 있는 프로젝트인가. 그러면 입장이 화면 전환이고, 아니면 이동이다. */
  isHere: boolean
  onHome: () => void
  /** 미연결 프로젝트의 셋업 화면으로. 로컬(브리지)에서만 불린다. */
  onSetup: () => void
}) {
  // 이름은 환경명이 있으면 그것, 없으면 프로젝트명. 환경 이름이 사람이 부르는 이름이다.
  const label = e?.name ?? p?.name ?? '(이름 없음)'
  const projectRef = p?.ref ?? e?.project_ref
  const status = p?.status ?? e?.status
  const region = p?.region ?? e?.region
  const local = bridgeAvailable()
  /** 전환 입장 진행 중 — selectEnv 가 돌아오면 페이지째 리로드된다. */
  const [entering, setEntering] = useState(false)

  const paused = status === 'INACTIVE'
  const starting = status === 'COMING_UP' || status === 'RESTORING' || busy
  const healthy = !paused && !starting && (e ? isHealthy(e) : status === 'ACTIVE_HEALTHY')
  const linked = !!e
  // 입장은 로컬(브리지)뿐이다 — 배포 어드민은 없다(어드민은 로컬 전용, 접근 통제는
  // Supabase 조직 멤버십). 연결 환경은 편집 환경 전환+리로드로, 미연결은 셋업 화면으로.
  const canEnter = healthy && (isHere || local)

  function enter() {
    if (!canEnter || entering) return
    if (isHere) {
      onHome()
      return
    }
    if (!local) return
    if (!linked) {
      onSetup()
      return
    }
    // 전환 입장 — 편집 환경을 옮기고 리로드하면 브리지가 새 환경 값을 다시 꽂는다.
    // 컴파일 대상도 함께 바뀐다. prod 여도 묻지 않는 것이 이 화면의 결정이다(카드 클릭이 곧 의도).
    setEntering(true)
    ops
      .selectEnv(e!.name)
      .then(() => window.location.reload())
      .catch((err) => {
        toast(err instanceof Error ? err.message : String(err), 'error')
        setEntering(false)
      })
  }

  const diskPercent =
    e?.disk_total && e.disk_used != null ? (e.disk_used / e.disk_total) * 100 : undefined
  const connPercent =
    e?.max_connections && e.connections != null
      ? (e.connections / e.max_connections) * 100
      : undefined

  return (
    <div
      className={`env-card${canEnter ? ' clickable' : ''}`}
      role={canEnter ? 'button' : undefined}
      tabIndex={canEnter ? 0 : undefined}
      onClick={enter}
      onKeyDown={(ev) => {
        if (canEnter && (ev.key === 'Enter' || ev.key === ' ')) {
          ev.preventDefault()
          enter()
        }
      }}
    >
      <div className="env-card-side">
        <div className="env-card-title">
          {starting || entering ? (
            <Spinner size={11} />
          ) : (
            <span className={`env-dot ${healthy ? 'ok' : paused ? 'paused' : 'down'}`} />
          )}
          <span className="env-name">{label}</span>
        </div>

        <div className="env-badges">
          <span
            className={`env-badge ${starting ? 'starting' : healthy ? 'ok' : paused ? 'paused' : 'down'}`}
          >
            {starting ? '기동 중' : paused ? '일시정지' : healthy ? '실행 중' : '점검 필요'}
          </span>
          {isHere && <span className="env-badge here">현재 위치</span>}
          {e?.is_editor && <span className="env-badge editor">에디터</span>}
          {/* 빌드 뱃지는 없다 — 빌드 = 편집 환경(빌드 환경 포인터 삭제) */}
          {/* 환경에 안 붙은 프로젝트 — 만들었지만 아직 역할이 없다 */}
          {!linked && <span className="env-badge">미연결</span>}
        </div>

        <dl className="env-meta">
          <dt>리전</dt>
          <dd>{region ?? '—'}</dd>
          <dt>상태</dt>
          <dd>{status ?? '—'}</dd>
          {e?.created_at && (
            <>
              <dt>생성일</dt>
              <dd>{new Date(e.created_at).toLocaleDateString('ko-KR')}</dd>
            </>
          )}
          <dt>도메인</dt>
          {/* 게임 서버 도메인 — 표시만 한다. 배포 어드민이 없으니 열 곳도 없다. */}
          <dd className="env-domain" title={e?.cloud_run_url ?? p?.url ?? ''}>
            {e?.cloud_run_url
              ? stripScheme(e.cloud_run_url)
              : p?.url
                ? stripScheme(p.url)
                : '미배포'}
          </dd>
          {projectRef && (
            <>
              <dt>ref</dt>
              <dd className="env-ref">{projectRef}</dd>
            </>
          )}
        </dl>
      </div>

      <div className="env-card-main">
        {e?.error ? (
          <div className="env-error">
            <i className="ti ti-alert-triangle me-1" />
            {e.error}
          </div>
        ) : e ? (
          <>
            <div className="env-metrics">
              <Metric
                label="CPU"
                value={e.cpu_percent != null ? `${e.cpu_percent}%` : '—'}
                sub={e.cpu_cores ? `${e.cpu_cores} core` : undefined}
                percent={e.cpu_percent}
              />
              <Metric
                label="메모리"
                value={e.mem_percent != null ? `${e.mem_percent}%` : '—'}
                sub={e.mem_total ? `${formatBytes(e.mem_used)} / ${formatBytes(e.mem_total)}` : undefined}
                percent={e.mem_percent}
              />
              <Metric
                label="스토리지"
                value={diskPercent != null ? `${diskPercent.toFixed(1)}%` : '—'}
                sub={e.disk_total ? `${formatBytes(e.disk_used)} / ${formatBytes(e.disk_total)}` : undefined}
                percent={diskPercent}
              />
              <Metric
                label="커넥션"
                value={
                  e.connections != null
                    ? `${e.connections}${e.max_connections ? ` / ${e.max_connections}` : ''}`
                    : '—'
                }
                percent={connPercent}
              />
            </div>

          </>
        ) : (
          // 환경에 안 붙은 프로젝트는 지표를 모은 적이 없다. 무엇을 해야 하는지만 말한다.
          <div className="env-unlinked">
            아직 환경에 연결되지 않았습니다.
            <br />
            {local
              ? '들어가서 이름을 붙이고 셋업하면 환경이 됩니다.'
              : '로컬 어드민(Unity)에서 들어가 셋업할 수 있습니다.'}
          </div>
        )}

        {/* 카드 전체가 진입점이다 — 되돌릴 수 없는 조작은 설정으로 옮겼다.
            들어가려다 지우는 사고를 구조로 막는다.
            들어갈 수 없을 때만 이유를 적는다. 들어갈 수 있으면 커서와 뱃지로 이미 보인다. */}
        {!canEnter && (
          <div className="env-enter-hint">
            {paused ? (
              <span className="text-muted">일시정지 상태입니다 — 설정에서 깨울 수 있습니다</span>
            ) : !linked ? (
              <span className="text-muted">로컬 어드민에서만 셋업할 수 있습니다</span>
            ) : null}
          </div>
        )}
      </div>
    </div>
  )
}

function Metric({
  label,
  value,
  sub,
  percent,
}: {
  label: string
  value: string
  sub?: string
  percent?: number
}) {
  return (
    <div className="env-metric">
      <div className="env-metric-label">{label}</div>
      <div className={`env-metric-value ${levelOf(percent)}`}>{value}</div>
      {sub && <div className="env-metric-sub">{sub}</div>}
    </div>
  )
}

function stripScheme(url: string): string {
  return url.replace(/^https?:\/\//, '')
}
