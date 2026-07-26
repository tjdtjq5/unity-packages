import { useCallback, useEffect, useState } from 'react'
import { listProjects, pingBridge, type BridgeProject } from '../../shared/bridge'
import {
  formatAge,
  formatBytes,
  isHealthy,
  levelOf,
  loadEnvironments,
  type EnvironmentInfo,
} from '../../shared/environments'
import { env as suparunEnv } from '../../shared/env'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
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
 *   환경 메타(`suparun_meta`) — Unity 가 넣어 둔 지표. **Unity 가 꺼져도 보인다**(낡을 수 있음)
 *   브리지 목록              — 지금 이 순간의 Supabase 계정 상태. Unity 가 켜져야 온다
 *
 * 겹치는 이유: 새로 만든 프로젝트는 아직 어느 환경에도 속하지 않아 메타에 없다.
 * 그래도 목록에는 떠야 "만들었는데 안 보인다" 가 없다.
 */
export function EnvironmentPage() {
  const { navigate } = useAdmin()
  const here = currentProjectRef()
  const [envs, setEnvs] = useState<EnvironmentInfo[] | null>(null)
  const [projects, setProjects] = useState<BridgeProject[] | null>(null)
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

    // 브리지는 따로 — 없어도 화면은 떠야 한다.
    const ping = await pingBridge()
    setOnline(!!ping)
    if (!ping) {
      setProjects(null)
      return
    }
    try {
      setProjects(await listProjects())
    } catch {
      setProjects(null)
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

  // 브리지가 살아있으면 계정의 모든 프로젝트가 기준이 된다(미연결 포함).
  // 꺼져 있으면 아는 것은 환경 메타뿐이다.
  const cards = online && projects
    ? projects.map((p) => ({ key: p.ref, env: byRef.get(p.ref), project: p }))
    : envs.map((e) => ({ key: e.name, env: e, project: undefined as BridgeProject | undefined }))

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
            title={online ? '' : 'Unity 에디터가 실행 중이어야 합니다'}
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
        cards.map((c) => (
          <EnvCard
            key={c.key}
            env={c.env}
            project={c.project}
            busy={false}
            isHere={!!here && (c.project?.ref ?? c.env?.project_ref) === here}
            onEnter={() => navigate({ kind: 'home' })}
          />
        ))
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
  onEnter,
}: {
  env?: EnvironmentInfo
  project?: BridgeProject
  busy: boolean
  /** 이 어드민이 붙어 있는 프로젝트인가. 그러면 입장이 화면 전환이고, 아니면 다른 사이트로 이동이다. */
  isHere: boolean
  onEnter: () => void
}) {
  // 이름은 환경명이 있으면 그것, 없으면 프로젝트명. 환경 이름이 사람이 부르는 이름이다.
  const label = e?.name ?? p?.name ?? '(이름 없음)'
  const projectRef = p?.ref ?? e?.project_ref
  const status = p?.status ?? e?.status
  const region = p?.region ?? e?.region

  const paused = status === 'INACTIVE'
  const starting = status === 'COMING_UP' || status === 'RESTORING' || busy
  const healthy = !paused && !starting && (e ? isHealthy(e) : status === 'ACTIVE_HEALTHY')
  const linked = !!e
  // 들어갈 수 있는 조건: 여기이거나(화면 전환), 배포된 다른 환경이거나(새 탭 + 재로그인)
  const canEnter = healthy && (isHere || !!e?.cloud_run_url)

  function enter() {
    if (!canEnter) return
    if (isHere) onEnter()
    else if (e?.cloud_run_url)
      window.open(`${e.cloud_run_url.replace(/\/$/, '')}/admin`, '_blank', 'noopener')
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
          {starting ? (
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
          {e?.is_build && <span className="env-badge build">빌드</span>}
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
          <dd className="env-domain" title={e?.cloud_run_url ?? p?.url ?? ''}>
            {e?.cloud_run_url ? (
              <a href={e.cloud_run_url} target="_blank" rel="noreferrer">
                {stripScheme(e.cloud_run_url)}
              </a>
            ) : p?.url ? (
              stripScheme(p.url)
            ) : (
              '미배포'
            )}
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

            {e.services && (
              <div className="env-services">
                {Object.entries(e.services).map(([name, ok]) => (
                  <span key={name} className={`env-service ${ok ? 'ok' : 'down'}`}>
                    {ok ? '●' : '○'} {name}
                  </span>
                ))}
              </div>
            )}
          </>
        ) : (
          // 환경에 안 붙은 프로젝트는 지표를 모은 적이 없다. 무엇을 해야 하는지만 말한다.
          <div className="env-unlinked">
            아직 환경에 연결되지 않았습니다.
            <br />
            Unity 대시보드 &gt; Settings 에서 이 프로젝트를 환경으로 등록하면 지표가 표시됩니다.
          </div>
        )}

        {/* 카드 전체가 진입점이다 — 되돌릴 수 없는 조작은 설정으로 옮겼다.
            들어가려다 지우는 사고를 구조로 막는다. */}
        <div className="env-enter-hint">
          {canEnter ? (
            isHere ? (
              <>
                <i className="ti ti-login me-1" />
                눌러서 입장
              </>
            ) : (
              <>
                <i className="ti ti-external-link me-1" />
                눌러서 이 환경 열기 (다시 로그인해야 합니다)
              </>
            )
          ) : paused ? (
            <span className="text-muted">일시정지 상태입니다 — 설정에서 깨울 수 있습니다</span>
          ) : !linked ? (
            <span className="text-muted">환경에 연결되지 않아 입장할 수 없습니다</span>
          ) : null}
        </div>

        {/* 실시간이 아니라는 사실을 숨기지 않는다 */}
        {e && <div className="env-collected">Unity 가 수집: {formatAge(e.collected_at)}</div>}
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
