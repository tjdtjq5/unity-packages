import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { applyChartDefaults } from './shared/chart'

/**
 * ADR-0003 — React 번들 진입점. 어드민 전체가 React 다.
 *
 * index.html 에 남은 것은 CSS, CDN 스크립트 4종, 플레이스홀더 노출, 프리뷰 mock 뿐이다.
 */

applyChartDefaults()

const host = document.getElementById('root')
if (host) {
  createRoot(host).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

export {}
