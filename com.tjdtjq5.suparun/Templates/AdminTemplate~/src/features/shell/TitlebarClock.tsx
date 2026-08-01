import { useEffect, useState } from 'react'

/** 시간대 선택을 기억하는 키. '1' 이면 UTC 표시다. */
const TZ_KEY = 'suparun_clock_utc'

/**
 * 타이틀바 시계 — `YY.MM.DD HH:mm:ss` + 시간대 배지.
 *
 * 기본은 보는 사람의 로컬 시간이다. 배지를 누르면 UTC 로 토글된다 —
 * 서버(Cloud Run·Supabase)는 UTC 로 돌아서, 로그·스냅샷의 원본 시각과
 * 대조할 때 머릿속 +9 암산을 없애 준다. 선택은 localStorage 에 남는다.
 */
export function TitlebarClock() {
  const [utc, setUtc] = useState(() => {
    try {
      return localStorage.getItem(TZ_KEY) === '1'
    } catch {
      return false
    }
  })
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(t)
  }, [])

  function toggle() {
    setUtc((v) => {
      const next = !v
      try {
        localStorage.setItem(TZ_KEY, next ? '1' : '0')
      } catch {
        /* 프라이빗 모드 등 — 표시는 계속 동작한다 */
      }
      return next
    })
  }

  return (
    <span className="tb-clock">
      {fmt(now, utc)}
      <button className="tz" onClick={toggle} title="로컬 ↔ UTC 전환">
        {utc ? 'UTC' : localZone()}
      </button>
    </span>
  )
}

function fmt(n: Date, utc: boolean): string {
  const p = (x: number) => String(x).padStart(2, '0')
  const [y, mo, d, h, mi, s] = utc
    ? [n.getUTCFullYear(), n.getUTCMonth() + 1, n.getUTCDate(), n.getUTCHours(), n.getUTCMinutes(), n.getUTCSeconds()]
    : [n.getFullYear(), n.getMonth() + 1, n.getDate(), n.getHours(), n.getMinutes(), n.getSeconds()]
  return `${p(y % 100)}.${p(mo)}.${p(d)} ${p(h)}:${p(mi)}:${p(s)}`
}

/** 로컬 시간대의 짧은 이름(서울이면 GMT+9). KST 하드코딩을 피한다 — 보는 사람이 어디든 맞는다. */
function localZone(): string {
  try {
    return (
      new Intl.DateTimeFormat('en-US', { timeZoneName: 'short' })
        .formatToParts(new Date())
        .find((x) => x.type === 'timeZoneName')?.value ?? 'LOCAL'
    )
  } catch {
    return 'LOCAL'
  }
}
