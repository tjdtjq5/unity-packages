import { useEffect } from 'react'

/** 키 1회당 스크롤 px. Shift 를 누르면 3배. */
const KEYMAP_STEP = 60

/**
 * 입력 중이면 스킵한다 — 안 그러면 'a' 를 못 친다.
 *
 * 모달이 열려 있어도 스크롤은 살린다. 정작 가로로 긴 표는 모달 안(JSON·다형 편집기)이라
 * 거기서 막으면 키맵이 가장 필요한 자리에서 죽는다.
 * 대신 스크롤 대상을 모달 안쪽으로 바꾼다 — scrollTarget() 참고.
 */
function blocked(e: KeyboardEvent): boolean {
  const t = e.target as HTMLElement | null
  return !!t?.matches?.('input,textarea,select,[contenteditable]')
}

/**
 * 지금 스크롤해야 할 컨테이너.
 *
 * 모달이 열려 있으면 그 안에서 실제로 넘치는 영역을 찾는다. 모달마다 id 를 붙여 두는 방식은
 * 새 모달이 생길 때마다 빠뜨리기 쉬워, 넘침 여부로 고른다.
 * `.modal.show` = Bootstrap 모달, `.icon-grid-overlay` = React 모달(shared/Modal.tsx).
 */
function modalOpen(): boolean {
  return !!document.querySelector('.modal.show, .icon-grid-overlay')
}

function scrollTarget(fallbackId: string): HTMLElement | null {
  const modal = document.querySelector('.modal.show, .icon-grid-overlay')
  if (modal) {
    const panes = modal.querySelectorAll<HTMLElement>('*')
    for (const el of panes) {
      const style = getComputedStyle(el)
      const scrollableX =
        (style.overflowX === 'auto' || style.overflowX === 'scroll') && el.scrollWidth > el.clientWidth
      const scrollableY =
        (style.overflowY === 'auto' || style.overflowY === 'scroll') && el.scrollHeight > el.clientHeight
      if (scrollableX || scrollableY) return el
    }
    // 모달은 떠 있는데 넘치는 데가 없으면 뒤 목록을 움직이지 않는다 — 엉뚱한 화면이 흔들린다.
    return null
  }
  return document.getElementById(fallbackId)
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
      const c = scrollTarget(scrollTargetId)
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
        // 아래는 화면을 바꾸는 키다. 모달이 떠 있을 때 뒤 화면이 바뀌면 닫았을 때 엉뚱한 곳에 있다.
        case '[':
          if (modalOpen()) return
          onCycleConfig(-1)
          break
        case ']':
          if (modalOpen()) return
          onCycleConfig(1)
          break
        case '/': {
          if (modalOpen()) return
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
