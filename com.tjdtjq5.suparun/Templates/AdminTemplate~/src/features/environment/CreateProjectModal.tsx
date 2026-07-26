import { useEffect, useState } from 'react'
import { availableRegions, createProject } from '../../shared/bridge'
import { Modal } from '../../shared/Modal'
import { Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'

/**
 * 새 Supabase 프로젝트. 실행은 **로컬 Unity 브리지**가 한다 — PAT 가 거기에만 있기 때문이다.
 *
 * 만들어도 곧바로 쓸 수 있는 것은 아니다(`COMING_UP` 으로 몇 분). 목록에서 상태로 드러난다.
 */
export function CreateProjectModal({
  onClose,
  onCreated,
}: {
  onClose: () => void
  onCreated: () => Promise<void>
}) {
  const [name, setName] = useState('')
  const [plan, setPlan] = useState('free')
  const [region, setRegion] = useState('')
  const [regions, setRegions] = useState<{ code: string; label: string }[] | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let alive = true
    void (async () => {
      try {
        const r = await availableRegions()
        if (alive) {
          setRegions(r)
          if (r.length > 0) setRegion(r[0].code)
        }
      } catch {
        if (alive) setRegions([])
      }
    })()
    return () => {
      alive = false
    }
  }, [])

  async function submit() {
    if (!name.trim()) return
    setBusy(true)
    try {
      const p = await createProject(name.trim(), region || undefined, plan)
      toast(`'${p.name}' 생성됨 — 준비되기까지 몇 분 걸립니다`, 'success')
      await onCreated()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setBusy(false)
    }
  }

  return (
    <Modal
      onClose={onClose}
      maxWidth={480}
      title={<span className="fw-bold px-2">새 Supabase 프로젝트</span>}
      footer={
        <div className="d-flex justify-content-end p-3 border-top btn-list">
          <button className="btn" onClick={onClose} disabled={busy}>
            취소
          </button>
          <button
            className="btn btn-primary"
            disabled={busy || !name.trim()}
            onClick={() => void submit()}
          >
            {busy ? (
              <>
                <Spinner size={12} />
                만드는 중…
              </>
            ) : (
              '만들기'
            )}
          </button>
        </div>
      }
    >
      <div style={{ padding: 16 }}>
        <label className="form-label">이름</label>
        <input
          className="form-control"
          value={name}
          autoFocus
          spellCheck={false}
          onChange={(e) => setName(e.target.value)}
        />

        <label className="form-label mt-3">리전</label>
        {regions === null ? (
          <div className="text-muted small">리전 목록 불러오는 중…</div>
        ) : regions.length === 0 ? (
          <div className="text-muted small">목록을 못 받아 기본 리전으로 만듭니다.</div>
        ) : (
          <select className="form-select" value={region} onChange={(e) => setRegion(e.target.value)}>
            {regions.map((r) => (
              <option key={r.code} value={r.code}>
                {r.label}
              </option>
            ))}
          </select>
        )}

        <label className="form-label mt-3">플랜</label>
        <select className="form-select" value={plan} onChange={(e) => setPlan(e.target.value)}>
          <option value="free">free</option>
          <option value="pro">pro</option>
        </select>
      </div>
    </Modal>
  )
}
