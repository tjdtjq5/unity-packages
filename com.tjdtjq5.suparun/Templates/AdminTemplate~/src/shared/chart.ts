/** Chart.js 는 CDN 전역이다 (index.html 의 `<script src="...chart.js">`). 최소 형태만 선언한다. */

export interface ChartInstance {
  destroy(): void
}

export interface ChartCtor {
  new (canvas: HTMLCanvasElement, config: unknown): ChartInstance
  defaults: {
    color: string
    borderColor: string
    font?: { family?: string; size?: number }
  }
}

declare global {
  interface Window {
    Chart?: ChartCtor
  }
}

/** Brutalist Terminal 톤 전역 기본값. 앱 부팅 시 1회 호출한다. */
export function applyChartDefaults(): void {
  const C = window.Chart
  if (!C) return
  C.defaults.color = 'rgba(216,228,216,0.8)'
  C.defaults.borderColor = 'rgba(26,42,26,1)'
  C.defaults.font = C.defaults.font ?? {}
  C.defaults.font.family = "'JetBrains Mono','SF Mono',Consolas,monospace"
  C.defaults.font.size = 11
}
