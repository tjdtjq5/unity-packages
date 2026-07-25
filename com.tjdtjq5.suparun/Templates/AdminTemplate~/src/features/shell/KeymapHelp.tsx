import { useEffect } from 'react'
import { createPortal } from 'react-dom'

const KEYMAP_HELP: [string, string][] = [
  ['A / D  ·  ← / →', '가로 스크롤'],
  ['W / S  ·  ↑ / ↓', '세로 스크롤'],
  ['Shift + 스크롤키', '3배 가속'],
  ['Home / End', '맨 위 / 맨 아래'],
  ['PgUp / PgDn', '한 화면씩'],
  ['[  /  ]', '이전 / 다음 Config'],
  ['/', '검색창 포커스'],
  ['Ctrl + Z', '되돌리기'],
  ['?', '이 도움말 (Esc로 닫기)'],
]

/** `?` 단축키로 여는 치트시트. 바닐라 toggleKeymapHelp 를 대체한다. */
export function KeymapHelp({ onClose }: { onClose: () => void }) {
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
    <div
      className="keymap-overlay"
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="keymap-panel">
        <div className="keymap-title">KEYBOARD SHORTCUTS</div>
        {KEYMAP_HELP.map(([k, d]) => (
          <div className="keymap-row" key={k}>
            <kbd>{k}</kbd>
            <span>{d}</span>
          </div>
        ))}
        <div className="keymap-hint">Esc 또는 바깥 클릭으로 닫기</div>
      </div>
    </div>,
    document.body,
  )
}
