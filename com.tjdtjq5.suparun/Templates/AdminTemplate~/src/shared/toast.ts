export type ToastType = 'success' | 'error' | 'info'

const ICONS: Record<ToastType, string> = {
  success: 'ti-check',
  error: 'ti-x',
  info: 'ti-info-circle',
}

/**
 * 우측 상단 알림. 3초 뒤 사라진다. 바닐라 toast() 를 그대로 옮겼다.
 *
 * React 컴포넌트로 만들지 않은 이유: 호출처가 이벤트 핸들러·async 흐름 안이라
 * 훅으로 바꾸면 전부 컴포넌트 컨텍스트를 타야 한다. DOM 한 줄이 정직하다.
 * 컨테이너(`#toast-container`)는 index.html 에 있다 — React 트리 밖이라 화면 전환에 영향받지 않는다.
 */
export function toast(message: string, type: ToastType = 'success'): void {
  const c = document.getElementById('toast-container')
  if (!c) return
  const el = document.createElement('div')
  el.className = `toast-item ${type}`
  const icon = document.createElement('i')
  icon.className = `ti ${ICONS[type] ?? ICONS.info}`
  el.appendChild(icon)
  // textContent 라 이스케이프가 필요 없다 (바닐라는 escHtml 을 거쳤다)
  el.appendChild(document.createTextNode(message))
  c.appendChild(el)
  setTimeout(() => el.remove(), 3000)
}
