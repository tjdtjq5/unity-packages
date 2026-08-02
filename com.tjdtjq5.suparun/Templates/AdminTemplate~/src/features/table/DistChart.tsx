import { useEffect, useRef } from 'react'
import type { DistBucket } from '../../shared/types'
// window.Chart 의 타입은 shared/chart.ts 가 전역으로 선언한다

/**
 * 분포 바 차트. 바닐라 renderDistChart() 를 옮긴 것이다.
 *
 * 바닐라는 전역 `chartInstance` 에 인스턴스를 담고 다음 렌더 때 destroy 했다.
 * React 에서는 effect cleanup 이 그 역할을 하므로 전역이 필요 없다.
 */
export function DistChart({ buckets, field }: { buckets: DistBucket[]; field: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const Chart = window.Chart
    if (!canvas || !Chart) return

    const chart = new Chart(canvas, {
      type: 'bar',
      data: {
        labels: buckets.map(
          (b) => `${Number(b.min).toLocaleString()} ~ ${Number(b.max).toLocaleString()}`,
        ),
        datasets: [
          {
            label: field,
            data: buckets.map((b) => b.count),
            backgroundColor: 'rgba(74,144,217,0.55)',
            borderColor: '#4a90d9',
            borderWidth: 1,
          },
        ],
      },
      options: {
        responsive: true,
        plugins: { legend: { display: false } },
        scales: {
          y: { beginAtZero: true, ticks: { precision: 0 } },
          x: { ticks: { maxRotation: 45 } },
        },
      },
    })

    return () => chart.destroy()
  }, [buckets, field])

  return <canvas ref={canvasRef} height={200} />
}
