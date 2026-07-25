import { useEffect } from 'react'

/** 키 1회당 스크롤 px. Shift 를 누르면 3배. */
const KEYMAP_STEP = 60

/**
 * 입력 중이거나 모달이 열려 있으면 전부 스킵한다 — 편집 무간섭 (Ctrl+Z 가드와 같은 정책).
 * `.modal.show` = Bootstrap 모달, `.icon-grid-overlay` = React 모달(shared/Modal.tsx).
 */
function blocked(e: KeyboardEvent): boolean {
  if (document.querySelector('.modal.show, .icon-grid-overlay')) return true
  const t = e.target as HTMLElement | null
  return !!t?.matches?.('input,textarea,select,[contenteditable]')
}

/**
 * 시트 키맵 (스크롤/Config 이동/검색 포커스/도움말).
 * 바닐라의 document keydown 핸들러를 그대로 옮긴 것이다.
 *
 * @param scrollTargetId 스크롤 대상 컨테이너 id
 */
export function useKeymap({
  scrollTargetId,
  onCycleConfig,
  onToggleHelp,
}: {
  scrollTargetId: string
  onCycleConfig: (dir: 1 | -1) => void
  onToggleHelp: () => void
}): void {
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.ctrlKey || e.metaKey || e.altKey) return // 조합키(Ctrl+Z 등)는 별도 핸들러 담당
      if (e.key === '?' && !blocked(e)) {
        e.preventDefault()
        onToggleHelp()
        return
      }
      if (blocked(e)) return
      const c = document.getElementById(scrollTargetId)
      if (!c) return
      const step = KEYMAP_STEP * (e.shiftKey ? 3 : 1)
      switch (e.key) {
        case 'a':
        case 'A':
        case 'ArrowLeft':
          c.scrollBy(-step, 0)
          break
        case 'd':
        case 'D':
        case 'ArrowRight':
          c.scrollBy(step, 0)
          break
        case 'w':
        case 'W':
        case 'ArrowUp':
          c.scrollBy(0, -step)
          break
        case 's':
        case 'S':
        case 'ArrowDown':
          c.scrollBy(0, step)
          break
        case 'Home':
          c.scrollTo({ left: c.scrollLeft, top: 0 })
          break
        case 'End':
          c.scrollTo({ left: c.scrollLeft, top: c.scrollHeight })
          break
        case 'PageUp':
          c.scrollBy(0, -c.clientHeight * 0.9)
          break
        case 'PageDown':
          c.scrollBy(0, c.clientHeight * 0.9)
          break
        case '[':
          onCycleConfig(-1)
          break
        case ']':
          onCycleConfig(1)
          break
        case '/': {
          const s = document.getElementById('search-input') as HTMLInputElement | null
          // 툴바가 숨어 있으면(Config 이외 화면) 아무것도 하지 않는다
          if (s && s.offsetParent !== null) {
            s.focus()
            s.select()
          } else return
          break
        }
        default:
          return
      }
      e.preventDefault()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [scrollTargetId, onCycleConfig, onToggleHelp])
}
