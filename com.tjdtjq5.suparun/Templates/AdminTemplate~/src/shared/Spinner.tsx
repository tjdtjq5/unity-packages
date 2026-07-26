import type { CSSProperties } from 'react'

/**
 * 로딩 표시 3종. 모양은 전부 `index.html` 의 `.sr-spinner` 가 갖는다 —
 * 여기서는 "어느 자리를 대신 채우는가" 만 다르다.
 *
 *   Spinner         버튼·문장 안에 들어가는 알맹이
 *   LoadingBlock    표/목록 자리를 대신 채운다 (기존 `.loading-spinner` 자리)
 *   FullScreenLoader 아직 그릴 화면 자체가 없을 때 (첫 진입)
 */

/** 회전 원호 하나. `size` 는 지름(px). */
export function Spinner({ size = 32 }: { size?: number }) {
  // 굵기는 지름에 비례시킨다 — 12px 스피너에 3px 링은 뭉개진다.
  const style = {
    '--sr-size': `${size}px`,
    '--sr-thick': `${Math.max(2, Math.round(size / 11))}px`,
  } as CSSProperties
  return <span className="sr-spinner" style={style} role="status" aria-label="로딩 중" />
}

/** 콘텐츠 영역 한가운데. 표가 오기 전 그 자리를 지킨다. */
export function LoadingBlock({ label, size = 32 }: { label?: string; size?: number }) {
  return (
    <div className="loading-spinner">
      <Spinner size={size} />
      {label && <div className="sr-label">{label}</div>}
    </div>
  )
}

/** 화면 전체를 덮는다. 첫 세션 확인처럼 껍데기조차 아직 없을 때만 쓴다. */
export function FullScreenLoader({ label }: { label?: string }) {
  return (
    <div className="sr-fullscreen">
      <Spinner size={44} />
      {label && <div className="sr-label">{label}</div>}
    </div>
  )
}
