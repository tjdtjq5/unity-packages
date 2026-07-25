import { useState } from 'react'
import { Modal } from '../../shared/Modal'
import { useIconMap } from './useLazyMaps'

/**
 * `[Icon]` 셀 + 아이콘 그리드 모달.
 * 바닐라 renderIconCell / iconCellInner / openIconGrid / renderIconGrid / commitIconGrid 를 대체한다.
 *
 * 바닐라는 hidden 캐리어 + body 오버레이 + 전역 iconGridState 조합이었고,
 * 선택 후 셀 DOM 을 직접 갈아끼웠다. 여기서는 값이 prop 이라 그 과정이 전부 사라진다.
 */
export function IconCell({
  atlas,
  value,
  onChange,
}: {
  atlas: string
  value: string
  onChange: (v: string) => void
}) {
  const [open, setOpen] = useState(false)
  const iconMap = useIconMap(true)
  const icons = iconMap?.[atlas] ?? []
  const hit = icons.find((s) => s.name === value)

  return (
    <>
      <span className="icon-cell" onClick={() => setOpen(true)}>
        {hit ? (
          <img className="icon-cell-thumb" src={hit.thumb} alt="" />
        ) : (
          <span className="icon-cell-noimg">▦</span>
        )}
        <span className="icon-cell-name">{value || '(없음)'}</span>
      </span>
      {open && (
        <IconGrid
          atlas={atlas}
          value={value}
          onPick={(v) => {
            onChange(v)
            setOpen(false)
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}

function IconGrid({
  atlas,
  value,
  onPick,
  onClose,
}: {
  atlas: string
  value: string
  onPick: (v: string) => void
  onClose: () => void
}) {
  const [query, setQuery] = useState('')
  const iconMap = useIconMap(true)
  const icons = iconMap?.[atlas] ?? []

  const q = query.trim().toLowerCase()
  const match = (s: string) => !q || s.toLowerCase().includes(q)
  const filtered = icons.filter((s) => match(s.name))
  // 서버 값이 아틀라스에 없는 경우에도 선택 상태를 보여준다 (바닐라와 동일)
  const orphan = value && !icons.some((s) => s.name === value) && match(value)

  return (
    <Modal
      onClose={onClose}
      title={
        <input
          className="form-control form-control-sm icon-grid-search"
          placeholder="아이콘 검색..."
          autoComplete="off"
          autoFocus
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      }
    >
      <div className="icon-grid-body">
          {!q && (
            <div
              className={`icon-grid-cell icon-grid-clear${value ? '' : ' sel'}`}
              onClick={() => onPick('')}
            >
              <span className="icon-grid-x">∅</span>
              <span className="icon-grid-label">(없음)</span>
            </div>
          )}
          {orphan && (
            <div className="icon-grid-cell sel" onClick={() => onPick(value)}>
              <span className="icon-grid-x">?</span>
              <span className="icon-grid-label">{value} (미참조)</span>
            </div>
          )}
          {filtered.map((s) => (
            <div
              key={s.name}
              className={`icon-grid-cell${s.name === value ? ' sel' : ''}`}
              onClick={() => onPick(s.name)}
            >
              <img src={s.thumb} alt="" />
              <span className="icon-grid-label">{s.name}</span>
            </div>
          ))}
          {filtered.length === 0 && !orphan && (
            <div className="icon-grid-empty">{iconMap ? '결과 없음' : '로딩 중…'}</div>
          )}
      </div>
    </Modal>
  )
}
