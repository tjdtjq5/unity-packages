import { useCallback, useEffect, useState } from 'react'
import { bridgeAvailable, setup } from '../../shared/bridge'
import { deleteProject } from '../../shared/projects'
import { ops, type OpsState } from '../../shared/ops'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import { useAdmin } from '../shell/AdminContext'

/**
 * 미연결 프로젝트의 셋업 — 환경 카드에서 **들어와서** 환경으로 만드는 화면.
 *
 * 예전에는 설정의 "환경 슬롯" 리스트에서 원격으로 이름을 만들고 드롭다운으로 연결했다.
 * 그 리스트는 해체됐다 — 연결은 이제 대상 프로젝트 **안에서** 한다. 슬롯을 미리 이름만
 * 만들어 두는 흐름도 같이 사라졌다(이름 짓기가 여기로 왔으므로 빈 슬롯이 생길 이유가 없다).
 *
 * 이름을 정하면 멈추지 않고 끝까지 간다: 슬롯 생성 → 연결(키 수신) → **편집 환경 전환** →
 * 스키마 반영 시작 → 리로드. 방금 본인이 셋업한 환경이므로 prod 이름이어도 묻지 않는다.
 * 리로드 뒤는 온보딩이 이어받는다 — 스키마 진행 표시와 첫 관리자 등록이 이미 거기 있다.
 *
 * 브리지 전용이다. 배포 어드민에는 이 화면으로 오는 길 자체가 없다(카드가 막는다).
 */
export function SetupProjectPage({ projectRef }: { projectRef: string }) {
  const { navigate } = useAdmin()
  const [projects, setProjects] = useState<
    { ref: string; name: string; status: string }[] | null
  >(null)
  const [st, setSt] = useState<OpsState | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [name, setName] = useState('')
  /** 진행 중인 단계 문구. null 이면 대기 상태다. */
  const [phase, setPhase] = useState<string | null>(null)
  const [removing, setRemoving] = useState(false)

  const pull = useCallback(async () => {
    try {
      const [p, s] = await Promise.all([setup.projects(), ops.state()])
      setProjects(p.projects)
      setSt(s)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void pull()
  }, [pull])

  if (!bridgeAvailable())
    return (
      <div className="empty-state">
        <i className="ti ti-plug-off" />
        <h3>로컬 어드민에서만 셋업할 수 있습니다</h3>
        <p>셋업은 Unity(브리지)의 손이 필요합니다.</p>
      </div>
    )

  if (error)
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>프로젝트 정보를 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )

  if (!projects || !st) return <LoadingBlock label="프로젝트 확인 중" />

  const project = projects.find((p) => p.ref === projectRef)
  const trimmed = name.trim()
  // 이미 프로젝트가 붙어 있는 이름은 못 쓴다. 이름만 있던 옛 빈 슬롯이면 그 자리를 재사용한다.
  const taken = st.environments.find((e) => e.name === trimmed)
  const conflict = !!taken?.configured

  async function run() {
    if (!trimmed || conflict || phase) return
    try {
      if (!taken) {
        setPhase('환경 슬롯 만드는 중')
        await ops.addEnv(trimmed)
      }
      setPhase('프로젝트 연결 중 (접속 키 수신)')
      await setup.chooseProject(projectRef, trimmed)
      setPhase('편집 환경 전환 중')
      await ops.selectEnv(trimmed)
      setPhase('스키마 반영 시작')
      await setup.init()
      // 리로드하면 브리지가 새 환경 값을 꽂고, 온보딩이 반영 진행을 이어서 보여준다.
      window.location.reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setPhase(null)
      await pull()
    }
  }

  async function remove() {
    if (!project || removing) return
    if (
      !window.confirm(
        `'${project.name}' 을 삭제합니다.\n데이터·백업이 함께 사라지며 되돌릴 수 없습니다.`,
      )
    )
      return
    const typed = window.prompt(`확인을 위해 '${project.name}' 을 입력하세요`, '')
    if (typed === null) return
    if (typed.trim() !== project.name) {
      toast('이름이 일치하지 않아 취소했습니다', 'info')
      return
    }
    setRemoving(true)
    try {
      await deleteProject(projectRef)
      toast(`'${project.name}' 삭제됨`, 'success')
      navigate({ kind: 'environments' })
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setRemoving(false)
    }
  }

  return (
    <div className="appset">
      <section className="appset-block">
        <h3 className="appset-title">환경으로 셋업</h3>
        <p className="appset-desc">
          <strong>{project?.name ?? projectRef}</strong> ({projectRef}) 은 아직 어느 환경도
          아닙니다. 이름을 정하면 연결 → 편집 환경 전환 → 스키마 반영이 한 번에 이어지고,
          끝나면 이 환경으로 들어갑니다.
        </p>

        <div className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-name">환경 이름</div>
            <div className="appset-row-key">
              {conflict
                ? `'${trimmed}' 은 이미 다른 프로젝트가 쓰고 있습니다`
                : '대시보드와 로그에 표시됩니다 (dev, prod …)'}
            </div>
          </div>
          <div className="appset-row-fields">
            <input
              className="form-control form-control-sm"
              placeholder="prod"
              value={name}
              disabled={phase !== null}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void run()
              }}
            />
            <button
              className="btn btn-sm btn-primary"
              disabled={!trimmed || conflict || phase !== null}
              onClick={() => void run()}
            >
              {phase ? <Spinner size={11} /> : '셋업 시작'}
            </button>
          </div>
        </div>

        {phase && (
          <div className="alert alert-info" style={{ marginTop: 10 }}>
            <Spinner size={13} /> {phase}…
          </div>
        )}
      </section>

      <section className="appset-block danger">
        <h3 className="appset-title danger">위험 영역</h3>
        <p className="appset-desc">
          아래 조작은 <strong>되돌릴 수 없습니다.</strong> 데이터·백업·스냅샷이 함께 사라집니다.
        </p>
        <div className="appset-danger-row">
          <div>
            <div className="appset-danger-name">{project?.name ?? projectRef} 삭제</div>
            <div className="appset-row-key">{projectRef}</div>
          </div>
          <button
            className="btn btn-sm btn-danger"
            disabled={removing || phase !== null}
            onClick={() => void remove()}
          >
            {removing ? <Spinner size={11} /> : '삭제'}
          </button>
        </div>
      </section>
    </div>
  )
}
