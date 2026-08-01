import { useCallback, useEffect, useRef, useState } from 'react'
import {
  deploy,
  MIN_INSTANCES,
  REGIONS,
  sanitizeServiceName,
  type DeployStatus,
  type ToolState,
} from '../../shared/deploy'
import {
  EMPTY_ENV,
  loadEnvSettings,
  saveEnvSettings,
  type EnvSettings,
} from '../../shared/envSettings'
import { Spinner } from '../../shared/Spinner'
import { sb } from '../../shared/supabase'
import { toast } from '../../shared/toast'
import { useAdmin } from '../shell/AdminContext'

/**
 * 배포 체크리스트.
 *
 * **단계가 아니라 항목 목록이다.** 처음 세팅은 위에서 아래로 밟아 내려가는 일이 되고,
 * 나중에 하나만 바꾸는 일은 그 줄만 고치는 일이 된다. 단계 기계로 만들면 뒤쪽이 접혀서
 * "다 끝난 뒤에 리전만 바꾸기" 가 불가능해진다 — Unity 의 `GcpSetupUI` 가 정확히 그 상태다.
 *
 * 두 블록으로 나눈다:
 *   연결 — 시스템이 확인한다. 사람은 **버튼만** 누른다. 다 되면 한 줄로 접힌다
 *   대상 — 사람이 정한다. **항상 편집 가능**하고 즉시 저장된다
 *
 * 로그인은 완료를 알려주지 않으므로 상태를 주기적으로 물어본다. 값이 바뀌는 순간이 곧 "연결됨"이다.
 */
export function DeployBlock() {
  const { navigate } = useAdmin()
  const [st, setSt] = useState<DeployStatus | null>(null)
  const [stError, setStError] = useState<string | null>(null)
  const [env, setEnv] = useState<EnvSettings | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [open, setOpen] = useState(false)

  const [projects, setProjects] = useState<{ id: string; name: string }[] | null>(null)
  const [repos, setRepos] = useState<string[] | null>(null)
  const [accounts, setAccounts] = useState<{ id: string; name: string }[] | null>(null)

  const saved = useRef<EnvSettings>(EMPTY_ENV)

  const pull = useCallback(async () => {
    try {
      setSt(await deploy.status())
      setStError(null)
    } catch (e) {
      setStError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void (async () => {
      try {
        const e = await loadEnvSettings()
        saved.current = e
        setEnv(e)
      } catch {
        setEnv(EMPTY_ENV)
      }
      await pull()
    })()

    // 로그인·자동 설정의 완료를 잡는 유일한 길이다. localhost 라 비용이 없다.
    const t = setInterval(() => void pull(), 3000)
    return () => clearInterval(t)
  }, [pull])

  /** 대상 값 저장 → Unity 에게 다시 읽으라고 알린다(안 그러면 ready 판정이 낡는다). */
  async function commit(next: EnvSettings) {
    setEnv(next)
    try {
      const { data } = sb ? await sb.auth.getSession() : { data: { session: null } }
      const n = await saveEnvSettings(next, saved.current, data.session?.user?.email ?? 'admin')
      saved.current = next
      if (n > 0) {
        await deploy.refresh()
        await pull()
      }
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    }
  }

  async function act(key: string, fn: () => Promise<unknown>) {
    setBusy(key)
    try {
      await fn()
      await pull()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(null)
    }
  }

  if (!st || !env) {
    return (
      <section className="appset-block">
        <h3 className="appset-title">배포</h3>
        {stError ? <div className="gsetup-warn">{stError}</div> : <Spinner size={14} />}
      </section>
    )
  }

  const connected =
    st.tools.gcloud.loggedIn && st.tools.gh.loggedIn && st.billing.enabled && st.permission.ok

  return (
    <>
      {/* ── 연결 ── */}
      <section className="appset-block">
        <h3 className="appset-title">
          연결{' '}
          {connected && (
            <button className="btn btn-sm" onClick={() => setOpen((o) => !o)}>
              {open ? '접기' : '✓ 모두 연결됨'}
            </button>
          )}
        </h3>

        {(!connected || open) && (
          <>
            <ToolRow
              label="Google Cloud CLI"
              tool={st.tools.gcloud}
              busy={busy === 'gcloud'}
              onLogin={() => void act('gcloud', deploy.gcloudLogin)}
            />
            <ToolRow
              label="GitHub CLI"
              tool={st.tools.gh}
              busy={busy === 'gh'}
              onLogin={() => void act('gh', deploy.ghLogin)}
            />
            <ToolRow label=".NET SDK" tool={st.tools.dotnet} optional />

            {/* 결제 — 자동 설정 실패의 최다 원인. 누르기 전에 잡는다. */}
            <Row
              state={st.billing.enabled ? 'ok' : st.billing.blocked ? 'off' : 'warn'}
              name="결제 계정"
              hint={st.billing.enabled ? '연결됨' : st.billing.blocked ?? '연결되지 않았습니다'}
            >
              {!st.billing.enabled && !st.billing.blocked && (
                <>
                  <select
                    className="form-select form-select-sm"
                    onFocus={() => {
                      if (!accounts) void deploy.billingAccounts().then((r) => setAccounts(r.accounts))
                    }}
                    onChange={(e) =>
                      e.target.value && void act('billing', () => deploy.linkBilling(e.target.value))
                    }
                    defaultValue=""
                  >
                    <option value="">계정 선택…</option>
                    {(accounts ?? []).map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.name}
                      </option>
                    ))}
                  </select>
                  {busy === 'billing' && <Spinner size={12} />}
                </>
              )}
            </Row>

            {/* 권한 — 버튼 하나가 API 활성화 + 서비스계정 + Secret 을 처리한다. */}
            <Row
              state={st.permission.ok ? 'ok' : st.autoSetup.error ? 'warn' : 'off'}
              name="Cloud Run 권한"
              hint={
                st.autoSetup.running
                  ? st.autoSetup.step
                  : st.permission.ok
                    ? st.permission.serviceAccount ?? '설정됨'
                    : (st.autoSetup.error ?? st.permission.blocked ?? 'API·서비스계정·Secret 을 한 번에 설정합니다')
              }
              detail={st.autoSetup.error}
            >
              {!st.permission.ok && (
                <button
                  className="btn btn-primary btn-sm"
                  disabled={st.autoSetup.running || !!st.permission.blocked}
                  onClick={() => void act('auto', deploy.autoSetup)}
                >
                  {st.autoSetup.running ? <Spinner size={12} /> : '자동 설정'}
                </button>
              )}
            </Row>
          </>
        )}
      </section>

      {/* ── 대상 ── */}
      <section className="appset-block">
        <h3 className="appset-title">대상</h3>

        <Row state={env.gcpProjectId ? 'ok' : 'off'} name="GCP 프로젝트" hint={env.gcpProjectId || '고르지 않음'}>
          <select
            className="form-select form-select-sm"
            value={env.gcpProjectId}
            onFocus={() => {
              if (!projects) void deploy.gcpProjects().then((r) => setProjects(r.projects))
            }}
            onChange={(e) => void commit({ ...env, gcpProjectId: e.target.value })}
          >
            <option value="">선택…</option>
            {(projects ?? (env.gcpProjectId ? [{ id: env.gcpProjectId, name: env.gcpProjectId }] : [])).map(
              (p) => (
                <option key={p.id} value={p.id}>
                  {p.name || p.id}
                </option>
              ),
            )}
          </select>
          <button
            className="btn btn-sm"
            disabled={busy === 'newgcp'}
            onClick={() => {
              const id = window.prompt('새 GCP 프로젝트 ID (소문자·숫자·하이픈)')
              if (!id) return
              void act('newgcp', async () => {
                await deploy.createGcpProject(id.trim(), id.trim())
                setProjects(null)
                await commit({ ...env, gcpProjectId: id.trim() })
              })
            }}
          >
            {busy === 'newgcp' ? <Spinner size={11} /> : '새로 만들기'}
          </button>
        </Row>

        <Row state="ok" name="리전" hint="가까울수록 빠릅니다">
          <select
            className="form-select form-select-sm"
            value={env.gcpRegion || 'asia-northeast3'}
            onChange={(e) => void commit({ ...env, gcpRegion: e.target.value })}
          >
            {REGIONS.map((r) => (
              <option key={r.code} value={r.code}>
                {r.label} ({r.code})
              </option>
            ))}
          </select>
        </Row>

        <Row
          state={env.gcpServiceName ? 'ok' : 'off'}
          name="Cloud Run 서비스명"
          hint={env.gcpServiceName ? 'URL 에 들어갑니다' : '레포 이름에서 만들어 드립니다'}
        >
          <input
            className="form-control form-control-sm"
            value={env.gcpServiceName}
            placeholder={sanitizeServiceName(env.githubRepoName)}
            onChange={(e) => setEnv({ ...env, gcpServiceName: e.target.value })}
            onBlur={(e) => void commit({ ...env, gcpServiceName: sanitizeServiceName(e.target.value) })}
          />
        </Row>

        <Row state="ok" name="최소 인스턴스" hint="서버를 항상 켜둘지">
          <select
            className="form-select form-select-sm"
            value={env.gcpMinInstances || '0'}
            onChange={(e) => void commit({ ...env, gcpMinInstances: e.target.value })}
          >
            {MIN_INSTANCES.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </Row>

        <Row
          state={env.githubRepoName ? 'ok' : 'off'}
          name="GitHub 레포"
          hint={env.githubRepoName || '고르지 않음'}
        >
          <select
            className="form-select form-select-sm"
            value={env.githubRepoName}
            onFocus={() => {
              if (!repos) void deploy.ghRepos().then((r) => setRepos(r.repos))
            }}
            onChange={(e) => void commit({ ...env, githubRepoName: e.target.value })}
          >
            <option value="">선택…</option>
            {(repos ?? (env.githubRepoName ? [env.githubRepoName] : [])).map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
          <button
            className="btn btn-sm"
            disabled={busy === 'newrepo'}
            onClick={() => {
              const name = window.prompt('새 레포 이름')
              if (!name) return
              void act('newrepo', async () => {
                await deploy.createGhRepo(name.trim())
                setRepos(null)
                await commit({ ...env, githubRepoName: name.trim() })
              })
            }}
          >
            {busy === 'newrepo' ? <Spinner size={11} /> : '새로 만들기'}
          </button>
        </Row>

        <Row state="ok" name="빌드 캐시" hint="비우면 매번 처음부터 빌드합니다">
          {['nuget', 'docker'].map((c) => {
            const on = env.serverCaches.split(',').includes(c)
            return (
              <button
                key={c}
                className={`btn btn-sm${on ? ' btn-primary' : ''}`}
                onClick={() => {
                  const cur = env.serverCaches.split(',').filter(Boolean)
                  const next = on ? cur.filter((x) => x !== c) : [...cur, c]
                  void commit({ ...env, serverCaches: next.join(',') })
                }}
              >
                {c}
              </button>
            )
          })}
        </Row>

        {/* 이 화면은 **대상을 정하는 곳**이다. 실행은 ops 화면이 한다 —
            거기서는 진행 로그와 결과를 계속 보여줘야 해서 성격이 다르다. */}
        <div className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-name">
              {st.ready ? '✓ 배포 준비 완료' : '○ 준비 중'}
            </div>
            <div className="appset-row-key">
              {st.ready ? '운영 화면에서 실행하세요' : (st.permission.blocked ?? '위 항목을 마저 채우세요')}
            </div>
          </div>
          {st.ready && (
            <div className="appset-row-fields">
              <button className="btn btn-primary btn-sm" onClick={() => navigate({ kind: 'ops' })}>
                운영 화면으로
              </button>
            </div>
          )}
        </div>
      </section>
    </>
  )
}

// ── 조각 ──

type State = 'ok' | 'warn' | 'off'

const ICON: Record<State, string> = { ok: '✓', warn: '⚠', off: '○' }

/**
 * 체크리스트 한 줄. 상태 아이콘 + 이름 + 한 줄 + 액션.
 * 실패해도 토스트로 흘리지 않고 **여기 남는다** — 놓칠 수 없어야 하고, 다음 행동이 옆에 있어야 한다.
 */
function Row({
  state,
  name,
  hint,
  detail,
  children,
}: {
  state: State
  name: string
  hint?: string
  /** CLI 원문처럼 길고 무서운 것. 접어 둔다. */
  detail?: string | null
  children?: React.ReactNode
}) {
  const [showDetail, setShowDetail] = useState(false)
  return (
    <div className="appset-row">
      <div className="appset-row-main">
        <div className="appset-row-name">
          {ICON[state]} {name}
        </div>
        {hint && <div className="appset-row-key">{hint}</div>}
        {detail && (
          <div className="appset-row-key">
            <a
              href="#"
              onClick={(e) => {
                e.preventDefault()
                setShowDetail((s) => !s)
              }}
            >
              {showDetail ? '접기' : '자세히'}
            </a>
            {showDetail && <pre style={{ whiteSpace: 'pre-wrap', marginTop: 4 }}>{detail}</pre>}
          </div>
        )}
      </div>
      {children && <div className="appset-row-fields">{children}</div>}
    </div>
  )
}

/** 도구 한 줄. 안 깔렸으면 링크 대신 **복사할 명령**을 준다 — 사이트에 가서 받는 것보다 빠르다. */
function ToolRow({
  label,
  tool,
  optional,
  busy,
  onLogin,
}: {
  label: string
  tool: ToolState
  optional?: boolean
  busy?: boolean
  onLogin?: () => void
}) {
  const state: State = !tool.installed
    ? optional
      ? 'off'
      : 'warn'
    : optional || tool.loggedIn
      ? 'ok'
      : 'warn'

  const hint = !tool.installed
    ? optional
      ? '없어도 배포됩니다 (있으면 배포 전에 코드 오류를 잡습니다)'
      : '설치되지 않았습니다'
    : tool.loggedIn
      ? `${tool.account} · ${tool.version}`
      : optional
        ? `설치됨 · ${tool.version}`
        : '로그인이 필요합니다'

  return (
    <Row state={state} name={label} hint={hint}>
      {!tool.installed && tool.installCommand && (
        <button
          className="btn btn-sm"
          onClick={() => {
            void navigator.clipboard.writeText(tool.installCommand!)
            toast('설치 명령을 복사했습니다', 'success')
          }}
        >
          명령 복사
        </button>
      )}
      {tool.installed && !tool.loggedIn && onLogin && (
        <button className="btn btn-primary btn-sm" disabled={busy} onClick={onLogin}>
          {busy ? <Spinner size={12} /> : '로그인'}
        </button>
      )}
    </Row>
  )
}
