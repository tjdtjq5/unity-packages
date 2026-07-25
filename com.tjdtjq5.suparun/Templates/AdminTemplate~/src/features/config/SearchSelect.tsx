import { useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'

/** 팝업 최대 높이 — CSS `.ss-pop { max-height: 240px }` 와 맞춰야 위/아래 뒤집기가 정확하다. */
const POP_MAX_H = 240

export interface SelectOption {
  id: string
  name?: string
  /** 아이콘 옵션에만 있는 dataURI 썸네일. FK 옵션에는 없다. */
  thumb?: string
}

/**
 * 검색 가능한 드롭다운. 바닐라의 renderSearchSelect + ssOpenPopup/ssFilter/ssMove/ssCommit 을 대체한다.
 *
 * 바닐라는 hidden input(값 캐리어) + display input + body 에 붙는 전역 팝업(ssOpen) 조합이었다.
 * React 에서는 값이 prop 이고 팝업이 지역 상태라 캐리어도 전역도 필요 없다.
 *
 * **팝업은 반드시 body 로 portal 하고 좌표를 직접 계산한다.** `.ss-pop` 은 `position: fixed` 라
 * 표 셀 안에 두면 앵커가 아니라 뷰포트 기준으로 떠서 표와 뒤엉킨다(바닐라도 body 에 붙이고
 * getBoundingClientRect 로 좌표를 줬다).
 *
 * 동작은 그대로 유지한다:
 *   - 열면 입력을 비우고 전체 목록 노출
 *   - 입력하면 필터, ↑↓ 이동, Enter 선택, Esc 취소
 *   - **옵션에 없는 값도 그대로 표시·보존** (서버 데이터가 옵션보다 오래된 경우)
 */
export function SearchSelect({
  options,
  value,
  onChange,
  placeholder = '(없음)',
}: {
  options: SelectOption[]
  value: string
  onChange: (v: string) => void
  placeholder?: string
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const wrapRef = useRef<HTMLSpanElement>(null)
  const popRef = useRef<HTMLDivElement>(null)
  const [anchor, setAnchor] = useState<DOMRect | null>(null)

  const display = useMemo(() => {
    if (!value) return ''
    const hit = options.find((o) => String(o.id) === value)
    return hit ? (hit.name ?? hit.id) : value
  }, [options, value])

  /**
   * 팝업 항목. 바닐라 ssRenderEntries 와 같은 3종 구성:
   *   clear    — "(없음)". 검색어가 없을 때만 노출
   *   preserve — 옵션에 없는 현재 값. "(미참조)" 태그를 달아 보존
   *   opt      — 일반 옵션. name 이 id 와 다르면 id 를 태그로 곁들임
   */
  const rows = useMemo(() => {
    const q = query.trim().toLowerCase()
    const hit = (s: string) => !q || s.toLowerCase().includes(q)
    const out: { kind: 'clear' | 'preserve' | 'opt'; id: string; label: string; tag?: string }[] = []

    if (!q) out.push({ kind: 'clear', id: '', label: '(없음)' })
    if (value && !options.some((o) => String(o.id) === value) && hit(value))
      out.push({ kind: 'preserve', id: value, label: value, tag: '(미참조)' })

    for (const o of options) {
      const id = String(o.id)
      const name = o.name ?? ''
      if (!hit(id) && !hit(name)) continue
      out.push({
        kind: 'opt',
        id,
        label: name || id,
        tag: name && name !== id ? id : undefined,
      })
    }
    return out
  }, [options, query, value])

  // 앵커 좌표 추적. 표/모달이 스크롤되면 따라가야 하므로 캡처 단계로 모든 스크롤을 듣는다.
  useLayoutEffect(() => {
    if (!open) return
    const el = wrapRef.current
    if (!el) return
    const update = () => setAnchor(el.getBoundingClientRect())
    update()
    window.addEventListener('scroll', update, true)
    window.addEventListener('resize', update)
    return () => {
      window.removeEventListener('scroll', update, true)
      window.removeEventListener('resize', update)
    }
  }, [open])

  // 바깥 클릭으로 닫기 — 팝업은 body 로 나가 있으므로 wrap 만으로는 부족하다
  useEffect(() => {
    if (!open) return
    function onDown(e: MouseEvent) {
      const t = e.target as Node
      if (!wrapRef.current?.contains(t) && !popRef.current?.contains(t)) setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  /** 아래 공간이 모자라고 위가 더 넓으면 위로 뒤집는다. */
  const popStyle = useMemo<CSSProperties>(() => {
    if (!anchor) return { visibility: 'hidden' }
    const below = window.innerHeight - anchor.bottom
    const flip = below < POP_MAX_H && anchor.top > below
    return {
      left: anchor.left,
      minWidth: anchor.width,
      ...(flip
        ? { bottom: window.innerHeight - anchor.top + 2, maxHeight: Math.min(POP_MAX_H, anchor.top - 8) }
        : { top: anchor.bottom + 2, maxHeight: Math.min(POP_MAX_H, below - 8) }),
    }
  }, [anchor])

  function commit(id: string) {
    onChange(id)
    setOpen(false)
  }

  return (
    <span className="ss-wrap" ref={wrapRef} style={{ position: 'relative', display: 'block' }}>
      <input
        className="form-select form-select-sm ss-display"
        value={open ? query : display}
        placeholder={placeholder}
        autoComplete="off"
        onFocus={() => {
          setQuery('')
          setActive(0)
          setOpen(true)
        }}
        onChange={(e) => {
          setQuery(e.target.value)
          setActive(0)
        }}
        onKeyDown={(e) => {
          if (!open) return
          if (e.key === 'ArrowDown') {
            e.preventDefault()
            setActive((a) => Math.min(a + 1, rows.length - 1))
          } else if (e.key === 'ArrowUp') {
            e.preventDefault()
            setActive((a) => Math.max(a - 1, 0))
          } else if (e.key === 'Enter') {
            e.preventDefault()
            const hit = rows[active]
            if (hit) commit(hit.id)
          } else if (e.key === 'Escape') {
            e.preventDefault()
            setOpen(false)
          }
        }}
      />
      {open &&
        createPortal(
          <div className="ss-pop" ref={popRef} style={popStyle}>
            {rows.length === 0 && <div className="ss-empty">결과 없음</div>}
            {rows.map((e, i) => (
              <div
                key={e.kind + e.id}
                className={`ss-opt${i === active ? ' ss-active' : ''}${e.kind === 'clear' ? ' ss-clear' : ''}`}
                onMouseEnter={() => setActive(i)}
                // onClick 은 input blur 뒤라 늦다 — mousedown 으로 잡는다
                onMouseDown={(ev) => {
                  ev.preventDefault()
                  commit(e.id)
                }}
              >
                <span className="ss-opt-name">{e.label}</span>
                {e.tag && <span className="ss-id">{e.tag}</span>}
              </div>
            ))}
          </div>,
          document.body,
        )}
    </span>
  )
}
