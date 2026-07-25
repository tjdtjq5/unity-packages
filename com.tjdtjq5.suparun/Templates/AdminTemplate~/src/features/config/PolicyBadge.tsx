import { useEffect, useState } from 'react'
import { PRESET_LABEL, loadPolicies, setPolicy, type Preset, type PolicyState } from '../../shared/policy'
import { toast } from '../../shared/toast'

/**
 * 이 시트의 접근 정책을 보여주고 바꾼다 (ADR-0004 결정 19).
 *
 * 항상 보이는 것이 핵심이다 — 정책은 코드에도 화면에도 안 나타나서 조용히 어긋난다.
 * 실제로 `FOR ALL USING (true)` 정책 8개가 아무도 모르게 있었고, DB 를 직접 캐물어서야 발견했다.
 */
export function PolicyBadge({ tableName }: { tableName: string }) {
  const [state, setState] = useState<PolicyState | null>(null)
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const [denied, setDenied] = useState(false)

  async function refresh() {
    try {
      const all = await loadPolicies()
      setState(all.find((p) => p.table_name === tableName) ?? null)
      setDenied(false)
    } catch {
      // 관리자가 아니거나 RPC 가 아직 없다(스키마 미반영). 배지를 숨긴다.
      setDenied(true)
    }
  }

  useEffect(() => {
    void refresh()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tableName])

  if (denied || !state) return null

  async function apply(preset: Exclude<Preset, 'custom'>) {
    if (preset === state?.preset) return setOpen(false)
    setBusy(true)
    try {
      await setPolicy(tableName, preset)
      await refresh()
      toast(`접근 정책: ${PRESET_LABEL[preset]}`, 'success')
      setOpen(false)
    } catch (e) {
      toast('정책 변경 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    } finally {
      setBusy(false)
    }
  }

  const danger = state.unsafe
  const presets: Exclude<Preset, 'custom'>[] = ['public', 'admin', 'locked']

  return (
    <div className="policy-box">
      <button
        className={`policy-badge${danger ? ' danger' : ''}`}
        onClick={() => setOpen((v) => !v)}
        title="이 시트에 누가 접근할 수 있는지"
      >
        <i className={`ti ${danger ? 'ti-alert-triangle' : 'ti-lock'} me-1`} />
        {PRESET_LABEL[state.preset]}
        {danger && ' — 쓰기가 열려 있음'}
      </button>

      {open && (
        <div className="policy-panel">
          {presets.map((p) => (
            <label key={p} className={`policy-opt${busy ? ' disabled' : ''}`}>
              <input
                type="radio"
                name={`policy_${tableName}`}
                checked={state.preset === p}
                disabled={busy}
                onChange={() => void apply(p)}
              />
              <span>{PRESET_LABEL[p]}</span>
            </label>
          ))}
        </div>
      )}
    </div>
  )
}
