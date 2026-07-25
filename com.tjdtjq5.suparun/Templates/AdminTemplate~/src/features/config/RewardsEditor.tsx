import { useState } from 'react'
import { Modal } from '../../shared/Modal'
import { useAdmin } from '../shell/AdminContext'

interface Reward {
  type: 'currency' | 'item'
  id: string
  amount: number
}

function parseRewards(raw: unknown): Reward[] {
  try {
    const a: unknown = JSON.parse(String(raw ?? '[]'))
    if (!Array.isArray(a)) return []
    return a.map((r) => {
      const o = r as Partial<Reward>
      return {
        type: o.type === 'item' ? 'item' : 'currency',
        id: String(o.id ?? ''),
        amount: Number(o.amount) || 0,
      }
    })
  } catch {
    return []
  }
}

/** 셀 배지 문구. 바닐라 formatRewards 와 동일. */
export function formatRewards(raw: unknown): string {
  const s = String(raw ?? '')
  if (!s) return ''
  const list = parseRewards(s)
  if (list.length === 0) return '(비어있음)'
  return list.map((r) => `${r.type === 'currency' ? '🪙' : '📦'} ${r.id} ×${r.amount}`).join(', ')
}

/**
 * `rewards` / `*_rewards` 전용 셀 + 모달.
 * 바닐라 openRewards / renderRewardsList / collectRewards / saveRewards 를 대체한다.
 *
 * 바닐라는 `.reward-type` / `.reward-id` / `.reward-amount` 클래스를 querySelectorAll 로 훑어
 * 값을 모았다(collectRewards). 여기서는 상태가 곧 진실이라 그 수집이 사라진다.
 */
export function RewardsCell({
  value,
  onChange,
}: {
  value: unknown
  onChange: (json: string) => void
}) {
  const [open, setOpen] = useState(false)
  const display = formatRewards(value)

  return (
    <>
      <span
        className="badge bg-blue-lt json-badge"
        title={String(value ?? '[]')}
        onClick={() => setOpen(true)}
      >
        <i className="ti ti-gift me-1" />
        {display || '(비어있음)'}
      </span>
      {open && (
        <RewardsModal
          initial={parseRewards(value)}
          onSave={(list) => {
            onChange(JSON.stringify(list))
            setOpen(false)
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}

function RewardsModal({
  initial,
  onSave,
  onClose,
}: {
  initial: Reward[]
  onSave: (list: Reward[]) => void
  onClose: () => void
}) {
  const [list, setList] = useState<Reward[]>(initial)
  const sources = useAdmin().rewardSources

  function optionsFor(type: Reward['type']) {
    return sources[type === 'currency' ? 'currency_def' : 'inventory_item_def'] ?? []
  }

  function patch(i: number, next: Partial<Reward>) {
    setList((prev) =>
      prev.map((r, idx) => {
        if (idx !== i) return r
        const merged = { ...r, ...next }
        // 타입이 바뀌면 id 를 비운다 (바닐라 onRewardTypeChange 동작)
        if (next.type && next.type !== r.type) merged.id = ''
        return merged
      }),
    )
  }

  return (
    <Modal
      onClose={onClose}
      maxWidth={640}
      title={<span className="fw-bold px-2">보상 편집</span>}
      footer={
        <div className="d-flex justify-content-end gap-2 p-3 border-top">
          <button className="btn btn-outline-secondary" onClick={onClose}>
            취소
          </button>
          <button className="btn btn-primary" onClick={() => onSave(list)}>
            <i className="ti ti-check me-1" />
            저장
          </button>
        </div>
      }
    >
      <div style={{ padding: 12, maxHeight: '60vh', overflowY: 'auto' }}>
          {list.map((r, i) => {
            const opts = optionsFor(r.type)
            const known = opts.some((o) => String(o.id) === r.id)
            return (
              <div key={i} className="row g-2 mb-2 align-items-end">
                <div className="col-3">
                  <label className="form-label small">타입</label>
                  <select
                    className="form-select form-select-sm"
                    value={r.type}
                    onChange={(e) => patch(i, { type: e.target.value as Reward['type'] })}
                  >
                    <option value="currency">재화</option>
                    <option value="item">아이템</option>
                  </select>
                </div>
                <div className="col-4">
                  <label className="form-label small">ID</label>
                  <select
                    className="form-select form-select-sm"
                    value={r.id}
                    onChange={(e) => patch(i, { id: e.target.value })}
                  >
                    {/* 목록이 비었거나 값이 목록 밖이면 그 값을 보존해 노출 */}
                    {(!known || opts.length === 0) && (
                      <option value={r.id}>{r.id || '(없음)'}</option>
                    )}
                    {opts.map((o) => (
                      <option key={o.id} value={String(o.id)}>
                        {o.name || o.id}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="col-3">
                  <label className="form-label small">수량</label>
                  <input
                    type="number"
                    min={1}
                    className="form-control form-control-sm"
                    value={r.amount}
                    onChange={(e) => patch(i, { amount: parseInt(e.target.value) || 0 })}
                  />
                </div>
                <div className="col-2">
                  <button
                    className="btn btn-ghost-danger btn-icon btn-sm"
                    onClick={() => setList((prev) => prev.filter((_, idx) => idx !== i))}
                  >
                    <i className="ti ti-x" />
                  </button>
                </div>
              </div>
            )
          })}
          <button
            className="btn btn-outline-primary btn-sm mt-2"
            onClick={() => setList((prev) => [...prev, { type: 'currency', id: '', amount: 1 }])}
          >
            <i className="ti ti-plus me-1" />
            보상 추가
          </button>
      </div>
    </Modal>
  )
}
