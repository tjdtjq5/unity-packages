import { useState } from 'react'
import { createSnapshot } from '../../shared/snapshot'
import { Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'

/**
 * 타이틀바의 "지금 저장". 어느 화면에서든 한 번에 지점을 남긴다.
 *
 * 전용 화면에도 같은 버튼이 있지만, 위험한 편집은 Config 표 위에서 벌어진다 —
 * 찍으려고 화면을 옮겨야 하면 결국 안 찍게 된다. 그래서 껍데기에 하나 더 둔다.
 *
 * 목록을 들고 있지 않으므로 훅을 쓰지 않는다. 저장 후 스냅샷 화면을 보고 있었다면
 * 그 화면이 자기 목록을 다시 읽어야 하는데, 그건 화면을 옮길 때 어차피 일어난다.
 */
export function QuickSnapshotButton() {
  const [busy, setBusy] = useState(false)

  async function onClick() {
    const label = window.prompt('스냅샷 이름 (영문·숫자 권장)', '')
    if (label === null) return
    if (!label.trim()) {
      toast('이름을 입력하세요', 'error')
      return
    }
    setBusy(true)
    try {
      await createSnapshot(label.trim())
      toast(`스냅샷 "${label.trim()}" 저장됨`, 'success')
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(false)
    }
  }

  return (
    <button
      className="btn btn-sm"
      style={{ marginRight: 10, padding: '1px 8px' }}
      title="지금 상태를 스냅샷으로 저장"
      disabled={busy}
      onClick={() => void onClick()}
    >
      {busy ? <Spinner size={11} /> : <i className="ti ti-camera me-1" />}
      SNAP
    </button>
  )
}
