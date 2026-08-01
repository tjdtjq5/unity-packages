import { useCallback, useEffect, useState } from 'react'
import { formatElapsed, ops, opsBusy, type OpsState } from '../../shared/ops'
import { Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'

/**
 * 운영 — **Unity 를 시키는 화면**.
 *
 * 옛 대시보드 Deploy 탭이 통째로 여기로 왔다. 옮기면서 지킨 것 둘:
 *
 *   1. **확인은 여기서 받는다.** 브리지는 받은 대로 실행한다. Unity 쪽에서 DisplayDialog 를
 *      띄우면 브라우저는 눌린 줄 알고 기다리는데 모달은 Unity 창 뒤에 숨어 버린다.
 *   2. **되돌리기 비싼 것만 확인을 받는다.** 스키마 반영·Id 생성은 그냥 실행한다 —
 *      매번 물으면 확인창이 무의미해지고, 정작 위험한 승격에서도 습관적으로 넘기게 된다.
 *
 * 진행 중일 때만 폴링한다. 배포는 몇 분이 걸리고 그동안 화면이 멈춰 보이면 안 된다.
 */
export function OpsPage() {
  const [st, setSt] = useState<OpsState | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [target, setTarget] = useState('')

  const pull = useCallback(async () => {
    try {
      setSt(await ops.state())
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void pull()
  }, [pull])

  // 도는 동안만 지켜본다. 끝나면 멈춘다 — 가만히 있는 화면을 3초마다 두드릴 이유가 없다.
  const running = st ? opsBusy(st) : false
  useEffect(() => {
    if (!running) return
    const t = setInterval(() => void pull(), 3000)
    return () => clearInterval(t)
  }, [running, pull])

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

  if (!st) {
    return (
      <section className="appset-block">
        {error ? <div className="gsetup-warn">{error}</div> : <Spinner size={14} />}
      </section>
    )
  }

  const others = st.environments.filter((e) => e.name !== st.editorEnv)
  const targetName = target || others[0]?.name || ''

  return (
    <>
      {/* 스키마·Id 상수 블록은 없다 — 스키마는 자동(컴파일) 아니면 배포(선반영)가 밀고,
          Id 상수는 행 편집·스냅샷 복원 때 어드민이 자동 트리거한다(shared/idsync.ts,
          설정 > 이 환경 토글). 승격만 아래에 남는다. */}

      {/* ── 배포 ── */}
      <section className="appset-block">
        <h3 className="appset-title">배포</h3>
        <DeployRow
          st={st}
          busy={busy}
          onDeploy={(skipVerify) => void act('deploy', () => ops.deploy(skipVerify))}
          onReset={() => void act('reset', ops.deployReset)}
        />
      </section>

      {/* ── 승격 ── */}
      {/* 환경이 하나면 올릴 곳이 없다. 빈 드롭다운을 띄우는 것보다 통째로 감추는 편이 낫다. */}
      {others.length > 0 && (
        <section className="appset-block">
          <h3 className="appset-title">승격</h3>
          <Row
            name="대상 환경"
            state="ok"
            hint={`[${st.editorEnv}] 의 [SpecData] 를 다른 환경으로 올립니다. 적용 직전 대상 스냅샷이 자동 저장됩니다.`}
          >
            <select
              className="form-select form-select-sm"
              value={targetName}
              onChange={(e) => setTarget(e.target.value)}
            >
              {others.map((e) => (
                <option key={e.name} value={e.name}>
                  {e.name}
                </option>
              ))}
            </select>
          </Row>

          <Row name="순서" state="ok" hint="스키마가 먼저입니다 — 대상에 표가 없으면 데이터를 넣을 자리가 없습니다">
            <button
              className="btn btn-sm"
              disabled={busy !== null || st.schema.running || !targetName}
              onClick={() => {
                if (
                  !window.confirm(
                    `'${targetName}' 에 표·정책·메타를 반영합니다.\n구조만 바뀌고 데이터는 그대로입니다.`,
                  )
                )
                  return
                void act('pschema', () => ops.promoteSchema(targetName))
              }}
            >
              1. 스키마 반영
            </button>
            <button
              className="btn btn-primary btn-sm"
              disabled={busy !== null || st.schema.running || !targetName}
              onClick={() => {
                if (
                  !window.confirm(
                    `'${st.editorEnv}' 의 [SpecData] 전체를 '${targetName}' 에 덮어씁니다.\n\n` +
                      `'${targetName}' 의 현재 데이터는 지워지지만 직전 스냅샷이 자동 저장되어 되돌릴 수 있습니다.\n` +
                      '플레이어 데이터([UserData])는 건드리지 않습니다.',
                  )
                )
                  return
                void act('pdata', () => ops.promoteData(targetName))
              }}
            >
              2. 데이터 승격
            </button>
          </Row>
        </section>
      )}
    </>
  )
}

/** 배포 한 줄. phase 가 화면을 가른다. */
function DeployRow({
  st,
  busy,
  onDeploy,
  onReset,
}: {
  st: OpsState
  busy: string | null
  onDeploy: (skipVerify: boolean) => void
  onReset: () => void
}) {
  const d = st.deploy
  const [showLog, setShowLog] = useState(false)

  if (!st.deployConfigured)
    return (
      <Row
        name="설정 필요"
        state="off"
        hint="설정 화면의 배포 항목(GCP 프로젝트·레포·서비스명)을 먼저 채우세요."
      />
    )

  switch (d.phase) {
    case 'verifying':
    case 'deploying':
      return (
        <Row name={d.phase === 'verifying' ? '빌드 검증 중' : '배포 중'} state="warn" hint={d.message ?? ''}>
          <Spinner size={12} />
        </Row>
      )

    case 'tracking':
      return (
        <Row name={`GitHub Actions 빌드 중 · ${formatElapsed(d.elapsed)}`} state="warn" hint={d.message ?? ''}>
          <Spinner size={12} />
          {d.actionsUrl && (
            <a className="btn btn-sm" href={d.actionsUrl} target="_blank" rel="noreferrer">
              Actions 열기
            </a>
          )}
        </Row>
      )

    case 'success':
      return (
        <Row name="✓ 배포 완료" state="ok" hint={d.url ?? ''}>
          {d.url && (
            <>
              <a className="btn btn-sm" href={`${d.url}/health`} target="_blank" rel="noreferrer">
                Health
              </a>
              <a
                className="btn btn-sm"
                href="https://console.cloud.google.com/run"
                target="_blank"
                rel="noreferrer"
              >
                Cloud Run
              </a>
            </>
          )}
          <button className="btn btn-sm" onClick={onReset}>
            닫기
          </button>
        </Row>
      )

    case 'failed':
      return (
        <div className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-name">⚠ 배포 실패</div>
            <div className="appset-row-key">
              <a
                href="#"
                onClick={(e) => {
                  e.preventDefault()
                  setShowLog((s) => !s)
                }}
              >
                {showLog ? '접기' : '로그 보기'}
              </a>
              {showLog && (
                <pre style={{ whiteSpace: 'pre-wrap', marginTop: 4 }}>{d.error ?? '(내용 없음)'}</pre>
              )}
            </div>
          </div>
          <div className="appset-row-fields">
            {d.error && (
              <button
                className="btn btn-sm"
                onClick={() => {
                  void navigator.clipboard.writeText(d.error!)
                  toast('로그를 복사했습니다', 'success')
                }}
              >
                로그 복사
              </button>
            )}
            {d.actionsUrl && (
              <a className="btn btn-sm" href={d.actionsUrl} target="_blank" rel="noreferrer">
                전체 로그
              </a>
            )}
            <button className="btn btn-primary btn-sm" onClick={onReset}>
              다시
            </button>
          </div>
        </div>
      )

    case 'skipped':
      return (
        <Row name="배포를 건너뛰었습니다" state="ok" hint="코드 변경이 없습니다">
          <button className="btn btn-sm" onClick={onReset}>
            확인
          </button>
        </Row>
      )

    default:
      return (
        <Row
          name="배포"
          state="ok"
          hint={
            st.dotnet
              ? '코드 생성 → 빌드 검증 → GitHub push → Actions → Cloud Run'
              : '.NET SDK 가 없어 빌드 검증을 건너뜁니다 — 오류는 GitHub Actions 에서 드러납니다'
          }
        >
          <button
            className="btn btn-primary btn-sm"
            disabled={busy !== null}
            // dotnet 이 없으면 브리지가 412 로 한 번 막는다. 그 뜻을 위 hint 가 이미 말하고 있으므로
            // 여기서는 처음부터 넘긴다 — 같은 안내를 두 번 읽게 하지 않는다.
            onClick={() => onDeploy(!st.dotnet)}
          >
            {busy === 'deploy' ? <Spinner size={12} /> : '배포'}
          </button>
        </Row>
      )
  }
}

type State = 'ok' | 'warn' | 'off'
const ICON: Record<State, string> = { ok: '✓', warn: '⚠', off: '○' }

/** DeployBlock 의 Row 와 같은 모양. 두 화면이 같은 언어를 쓰게 둔다. */
function Row({
  state,
  name,
  hint,
  children,
}: {
  state: State
  name: string
  hint?: string
  children?: React.ReactNode
}) {
  return (
    <div className="appset-row">
      <div className="appset-row-main">
        <div className="appset-row-name">
          {ICON[state]} {name}
        </div>
        {hint && <div className="appset-row-key">{hint}</div>}
      </div>
      {children && <div className="appset-row-fields">{children}</div>}
    </div>
  )
}
