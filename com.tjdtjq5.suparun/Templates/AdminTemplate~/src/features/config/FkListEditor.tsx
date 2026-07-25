import { useEffect, useRef, useState } from 'react'
import { Modal } from '../../shared/Modal'
import { useAdmin } from '../shell/AdminContext'

/** Sortable 은 CDN 전역이다 (index.html 의 sortablejs). */
interface SortableInstance {
  destroy(): void
}
declare global {
  interface Window {
    Sortable?: { create(el: HTMLElement, opts: Record<string, unknown>): SortableInstance }
  }
}

export function parseFkList(v: unknown): string[] {
  try {
    const a: unknown = JSON.parse(String(v ?? ''))
    return Array.isArray(a) ? a.map(String) : []
  } catch {
    return []
  }
}

/**
 * `[ForeignKey(typeof(List<T>))]` 셀 + 리스트 편집 모달.
 * 바닐라 openFkList / renderFkListRows / fkListSet / fkListAdd / fkListRemove / initFkListSorting 을 대체한다.
 *
 * 값은 TEXT 컬럼에 담긴 id JSON 배열이다. 다른 행이 쓰는 id 는 드롭다운에서 disabled 처리해
 * 중복을 막는다(바닐라와 동일).
 */
export function FkListCell({
  fieldName,
  target,
  value,
  onChange,
}: {
  fieldName: string
  target: string
  value: unknown
  onChange: (v: string) => void
}) {
  const [open, setOpen] = useState(false)
  const ids = parseFkList(value)

  return (
    <>
      <span
        className="badge bg-cyan-lt json-badge"
        title={String(value ?? '[]')}
        onClick={() => setOpen(true)}
      >
        <i className="ti ti-link me-1" />
        {ids.length ? ids.join(', ') : '(비어있음)'}
      </span>
      {open && (
        <FkListModal
          fieldName={fieldName}
          target={target}
          ids={ids}
          onChange={onChange}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}

function FkListModal({
  fieldName,
  target,
  ids,
  onChange,
  onClose,
}: {
  fieldName: string
  target: string
  ids: string[]
  onChange: (v: string) => void
  onClose: () => void
}) {
  const options = useAdmin().fkSources[target] ?? []
  const rowsRef = useRef<HTMLDivElement>(null)
  // 드래그 정렬 결과를 즉시 반영하기 위한 지역 사본 — 커밋할 때마다 부모로 올린다
  const [local, setLocal] = useState<string[]>(ids)

  function commit(next: string[]) {
    setLocal(next)
    onChange(JSON.stringify(next))
  }

  // 드래그 정렬. 목록이 바뀔 때마다 재생성한다(바닐라도 재렌더마다 재생성했다).
  useEffect(() => {
    const el = rowsRef.current
    const Sortable = window.Sortable
    if (!el || !Sortable) return
    const inst = Sortable.create(el, {
      handle: '.fk-drag-handle',
      animation: 150,
      ghostClass: 'sortable-ghost',
      forceFallback: true,
      fallbackOnBody: true,
      fallbackTolerance: 4,
      onEnd: (evt: { oldIndex?: number; newIndex?: number }) => {
        const { oldIndex, newIndex } = evt
        if (oldIndex == null || newIndex == null || oldIndex === newIndex) return
        const next = [...local]
        const [moved] = next.splice(oldIndex, 1)
        next.splice(newIndex, 0, moved)
        commit(next)
      },
    })
    return () => inst.destroy()
  }, [local])

  const unused = options.find((o) => !local.includes(String(o.id)))

  return (
    <Modal
      onClose={onClose}
      maxWidth={480}
      title={
        <span className="fw-bold px-2">
          {fieldName} — {target}
        </span>
      }
    >
      <div style={{ padding: 12, maxHeight: '60vh', overflowY: 'auto' }}>
          <div ref={rowsRef}>
            {local.map((id, i) => {
              const known = options.some((o) => String(o.id) === id)
              return (
                <div key={`${id}_${i}`} className="d-flex gap-1 mb-1 align-items-center">
                  <span
                    className="fk-drag-handle"
                    style={{ cursor: 'grab', color: '#6c757d', padding: '0 4px' }}
                    title="드래그로 순서 변경"
                  >
                    <i className="ti ti-grip-vertical" />
                  </span>
                  <select
                    className="form-select form-select-sm"
                    value={id}
                    onChange={(e) =>
                      commit(local.map((x, idx) => (idx === i ? e.target.value : x)))
                    }
                  >
                    {/* 옵션에 없는 id 도 보존한다 */}
                    {!known && <option value={id}>{id} (없는 id)</option>}
                    {options.map((o) => (
                      <option
                        key={o.id}
                        value={String(o.id)}
                        disabled={local.includes(String(o.id)) && String(o.id) !== id}
                      >
                        {o.name ? `${o.id} — ${o.name}` : o.id}
                      </option>
                    ))}
                  </select>
                  <button
                    className="btn btn-ghost-danger btn-icon btn-sm"
                    title="삭제"
                    onClick={() => commit(local.filter((_, idx) => idx !== i))}
                  >
                    <i className="ti ti-trash" />
                  </button>
                </div>
              )
            })}
          </div>
          <button
            className="btn btn-sm btn-outline-primary mt-2"
            disabled={!unused}
            onClick={() => unused && commit([...local, String(unused.id)])}
          >
            <i className="ti ti-plus me-1" />행 추가
          </button>
      </div>
    </Modal>
  )
}
