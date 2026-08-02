import { useCallback, useEffect, useState } from 'react'
import { needsSetup, setup, type SetupState } from '../../shared/bridge'
import { Spinner } from '../../shared/Spinner'

/**
 * 첫 셋업.
 *
 * **1스텝뿐이다** — Supabase 연결(PAT + 프로젝트). 사람이 **답해야 하는 것**만 묻는다.
 * 스키마 반영은 물어보지 않는다: 안 하면 아무것도 동작하지 않으므로 "하시겠습니까?" 가
 * 의미 없는 질문이다. 프로젝트를 고르면 바로 돌린다.
 *
 * **진입·이탈은 상태가 정한다.** '첫 실행' 플래그를 두지 않는다 — PAT 만료, 새 환경, 팀원 클론처럼
 * 빈칸은 나중에도 생기고, 그때 플래그는 사실과 어긋난다.
 *
 * 관리자 단계는 없다 — 스키마가 준비되면 새로고침되어 로그인 화면으로 넘어가고,
 * 첫 관리자 등록은 로그인 직후 `/auth/claim-admin` 이 한다(App.tsx 참조).
 */
export function OnboardingPage() {
  const [st, setSt] = useState<SetupState | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const [pat, setPat] = useState('')
  const [projects, setProjects] = useState<{ ref: string; name: string; status: string }[] | null>(null)

  const pull = useCallback(async () => {
    try {
      setSt(await setup.state())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void pull()
  }, [pull])

  // 초기화가 도는 동안만 지켜본다. 끝나면 새로고침해서 ② 로 넘어간다.
  useEffect(() => {
    if (!st?.initRunning) return
    const t = setInterval(() => void pull(), 2000)
    return () => clearInterval(t)
  }, [st?.initRunning, pull])

  // 스키마까지 준비되면 여기 있을 이유가 없다.
  useEffect(() => {
    if (st && !needsSetup(st)) window.location.reload()
  }, [st])

  async function act(key: string, fn: () => Promise<unknown>) {
    setBusy(key)
    setError(null)
    try {
      await fn()
      await pull()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(null)
    }
  }

  if (!st) {
    return (
      <div className="auth-page">
        <div className="auth-card">
          {error ? <div className="alert alert-danger">{error}</div> : <Spinner size={16} />}
        </div>
      </div>
    )
  }

  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="auth-head">
          <div className="auth-logo">S</div>
          <h1 className="auth-title">프로젝트 연결</h1>
          <p className="auth-sub">Supabase 프로젝트를 연결하고 초기화합니다</p>
        </div>

        <div>
          {error && <div className="alert alert-danger">{error}</div>}

          {/* ── 토큰 ── */}
          {!st.hasPat ? (
            <div style={{ marginTop: 18 }}>
              <div className="appset-row-name">Supabase Access Token</div>
              <div className="appset-row-key" style={{ marginBottom: 8 }}>
                프로젝트를 만들고 스키마를 반영하는 데 씁니다. <strong>이 컴퓨터에만 저장됩니다.</strong>
              </div>
              <a
                href="https://supabase.com/dashboard/account/tokens"
                target="_blank"
                rel="noreferrer"
              >
                토큰 발급하기
              </a>
              <input
                className="form-control"
                type="password"
                autoComplete="off"
                placeholder="sbp_..."
                style={{ marginTop: 8 }}
                value={pat}
                onChange={(e) => setPat(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && pat.trim()) void act('pat', () => setup.savePat(pat.trim()))
                }}
              />
              <button
                className="btn btn-primary oauth-btn"
                style={{ marginTop: 8, width: '100%' }}
                disabled={busy !== null || !pat.trim()}
                onClick={() => void act('pat', () => setup.savePat(pat.trim()))}
              >
                {busy === 'pat' ? <Spinner size={14} /> : null} 연결
              </button>
            </div>
          ) : (
            <div className="appset-row-key" style={{ marginTop: 18 }}>✓ 토큰 확인됨</div>
          )}

          {/* ── 프로젝트 ── */}
          {st.hasPat && !st.hasProject && (
            <div style={{ marginTop: 18 }}>
              <div className="appset-row-name">프로젝트</div>
              <div className="appset-row-key" style={{ marginBottom: 8 }}>
                게임 데이터가 들어갈 Supabase 프로젝트입니다.
              </div>

              {projects === null ? (
                <button
                  className="btn btn-primary oauth-btn"
                  style={{ width: '100%' }}
                  disabled={busy !== null}
                  onClick={() =>
                    void act('list', async () => setProjects((await setup.projects()).projects))
                  }
                >
                  {busy === 'list' ? <Spinner size={14} /> : null} 목록 불러오기
                </button>
              ) : (
                <>
                  {projects.map((p) => (
                    <button
                      key={p.ref}
                      className="btn btn-outline-secondary oauth-btn"
                      style={{ width: '100%', marginBottom: 6 }}
                      disabled={busy !== null}
                      onClick={() => void act('pick', () => setup.chooseProject(p.ref))}
                    >
                      {busy === 'pick' ? <Spinner size={14} /> : null} {p.name}{' '}
                      <span className="dim">({p.status})</span>
                    </button>
                  ))}
                  <button
                    className="btn oauth-btn"
                    style={{ width: '100%' }}
                    disabled={busy !== null}
                    onClick={() => {
                      const name = window.prompt('새 프로젝트 이름')
                      if (!name?.trim()) return
                      void act('new', async () => {
                        const r = await setup.createProject(name.trim())
                        await setup.chooseProject(r.ref)
                        setProjects(null)
                      })
                    }}
                  >
                    {busy === 'new' ? <Spinner size={14} /> : null} + 새 프로젝트 만들기
                  </button>
                </>
              )}
            </div>
          )}
          {st.hasProject && (
            <div className="appset-row-key" style={{ marginTop: 8 }}>✓ {st.projectRef}</div>
          )}

          {/* ── 초기화 (자동) ── */}
          {st.hasPat && st.hasProject && !st.schemaReady && (
            <div style={{ marginTop: 18 }}>
              {st.initRunning ? (
                <div className="alert alert-info">
                  <Spinner size={14} /> 표와 보안 정책을 만드는 중입니다… 수십 초 걸릴 수 있습니다.
                </div>
              ) : st.initError ? (
                <>
                  <div className="alert alert-danger">{st.initError}</div>
                  <button
                    className="btn btn-primary oauth-btn"
                    style={{ width: '100%' }}
                    disabled={busy !== null}
                    onClick={() => void act('init', setup.init)}
                  >
                    다시 시도
                  </button>
                </>
              ) : (
                <button
                  className="btn btn-primary oauth-btn"
                  style={{ width: '100%' }}
                  disabled={busy !== null}
                  onClick={() => void act('init', setup.init)}
                >
                  {busy === 'init' ? <Spinner size={14} /> : null} 초기화
                </button>
              )}
            </div>
          )}

          <div className="auth-foot" style={{ marginTop: 18 }}>
            Unity 가 켜져 있어야 이 화면이 동작합니다.
          </div>
        </div>
      </div>
    </div>
  )
}
