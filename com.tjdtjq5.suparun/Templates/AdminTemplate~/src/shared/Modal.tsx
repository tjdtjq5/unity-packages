import { useEffect, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

/**
 * 오버레이 모달. **반드시 document.body 로 portal 한다.**
 *
 * 왜 portal 이 필요한가:
 *   이 모달들은 표 셀(`<td>`) 안에서 열린다. 거기서 그대로 렌더하면
 *   `position: fixed` 가 테이블 레이아웃에 갇혀 배경 오버레이가 깔리지 않고
 *   패널이 표 위에 흩어져 보인다. 바닐라도 같은 이유로 `document.body.appendChild(ov)` 했다.
 *
 * 4곳(아이콘 그리드 · FK 리스트 · JSON 에디터 · Rewards · 삭제 확인)이 같은 패턴이라
 * shared 로 승격했다 (ADR-0003 결정 8 — 2회 이상일 때 승격).
 */
export function Modal({
  title,
  onClose,
  children,
  footer,
  maxWidth,
}: {
  /** head 에 그대로 들어간다. 일반 제목은 `<span className="fw-bold px-2">` 로 감싸 넘길 것.
   *  (아이콘 그리드처럼 head 에 검색 input 을 넣는 경우가 있어 감싸지 않는다) */
  title: ReactNode
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  /** 기본은 CSS 의 .icon-grid-panel 값(640px). 다른 폭이 필요할 때만 지정한다. */
  maxWidth?: number
}) {
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault()
        onClose()
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  return createPortal(
    <div className="icon-grid-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      {/* CSS 기본값과 같은 식(min(W, 100vw-40px))을 유지해야 좁은 화면에서 안 넘친다 */}
      <div
        className="icon-grid-panel"
        style={maxWidth ? { width: `min(${maxWidth}px, calc(100vw - 40px))` } : undefined}
      >
        <div className="icon-grid-head">
          {title}
          <button className="icon-grid-close" title="닫기 (Esc)" onClick={onClose}>
            ×
          </button>
        </div>
        {children}
        {footer}
      </div>
    </div>,
    document.body,
  )
}
