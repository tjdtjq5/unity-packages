import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { bridgeAvailable } from '../../shared/bridge'
import { ops, type OpsState } from '../../shared/ops'
import { Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'

/**
 * 타이틀바의 환경 자리 — 지금 어느 환경 안에 있는지 항상 보인다.
 *
 * 로컬(브리지)에서는 드롭다운 전환기다. 다른 환경을 고르면 **묻지 않고 즉시 전환 입장**
 * (`selectEnv` + 리로드 — 컴파일 대상도 같이 바뀐다). 환경 카드 클릭과 같은 결정이다:
 * 고르는 행위가 곧 의도이고, prod 여도 확인창을 두지 않는다.
 *
 * 배포 어드민에는 브리지가 없어 목록을 물어볼 곳이 없다 — 표시 전용 칩으로 강등되고,
 * 누르면 환경 화면으로 간다.
 */
export function EnvSwitcher({
  label,
  onGoEnvironments,
}: {
  label: string
  onGoEnvironments: () => void
}) {
  const local = bridgeAvailable()
  const [open, setOpen] = useState(false)
  const [st, setSt] = useState<OpsState | null>(null)
  const [switching, setSwitching] = useState<string | null>(null)
  const chipRef = useRef<HTMLButtonElement>(null)

  // 목록은 열 때마다 새로 받는다 — 슬롯은 다른 화면(셋업·설정)에서 바뀔 수 있다.
  useEffect(() => {
    if (!open) return
    ops.state().then(setSt).catch(() => setSt(null))
  }, [open])

  if (!local)
    return (
      <button className="tb-env-chip" onClick={onGoEnvironments} title="환경 선택으로">
        {label}
      </button>
    )

  function pick(name: string) {
    if (!st || switching) return
    if (name === st.editorEnv) {
      setOpen(false)
      return
    }
    setSwitching(name)
    ops
      .selectEnv(name)
      .then(() => window.location.reload())
      .catch((e) => {
        toast(e instanceof Error ? e.message : String(e), 'error')
        setSwitching(null)
      })
  }

  // 메뉴는 body 로 portal 한다 — 타이틀바(z 1000) 안에 그리면 사이드바(Tabler navbar,
  // z 1030)가 덮는다. `.ss-pop`·`.icon-grid-overlay` 와 같은 함정, 같은 해법이다.
  const chipRect = open ? chipRef.current?.getBoundingClientRect() : undefined

  return (
    <span className="tb-env">
      <button ref={chipRef} className="tb-env-chip" onClick={() => setOpen((v) => !v)}>
        {switching ? <Spinner size={10} /> : label}
        <span className="caret">▾</span>
      </button>
      {open && chipRect && createPortal(
        <>
          {/* 바깥 클릭으로 닫는다 — 메뉴보다 아래, 화면 전체를 덮는 투명막 */}
          <span className="tb-env-backdrop" onClick={() => setOpen(false)} />
          <div
            className="tb-env-menu"
            style={{ top: chipRect.bottom + 6, left: chipRect.left }}
          >
            {!st ? (
              <div style={{ padding: '5px 8px' }}>
                <Spinner size={11} />
              </div>
            ) : (
              // 연결 안 된 슬롯은 목록에서 뺀다 — 전환해 봐야 빈 환경이 스탬프된다.
              st.environments
                .filter((e) => e.configured)
                .map((e) => (
                  <button
                    key={e.name}
                    className={e.name === st.editorEnv ? 'cur' : ''}
                    onClick={() => pick(e.name)}
                  >
                    {e.name === st.editorEnv ? '✓ ' : ''}
                    {e.name}
                    {switching === e.name && (
                      <>
                        {' '}
                        <Spinner size={10} />
                      </>
                    )}
                  </button>
                ))
            )}
            <div className="sep" />
            <button
              onClick={() => {
                setOpen(false)
                onGoEnvironments()
              }}
            >
              환경 화면으로…
            </button>
          </div>
        </>,
        document.body,
      )}
    </span>
  )
}
