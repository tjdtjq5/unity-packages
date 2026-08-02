import { useEffect, useState } from 'react'

/**
 * 탑바 시계 — UTC 와 로컬을 **2단으로 쌓아 상시 표시**한다 (Metaplay 헤더 동형, ADR-0008).
 *
 * 토글이었을 때는 한쪽을 보는 동안 다른 쪽을 머릿속으로 암산해야 했다. 서버(Cloud Run·
 * Supabase)는 UTC 로 돌고 사람은 로컬로 사니, 로그·스냅샷 시각과 대조하는 자리에는
 * 둘 다 보이는 편이 맞다. 보는 사람의 로컬이 UTC 면 한 줄만 보여준다.
 */
export function TitlebarClock() {
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(t)
  }, [])

  const zone = localZone()
  return (
    <span className="tb-clock">
      <span className="tb-clock-row">
        <span className="tz">UTC</span>
        <span className="t">{fmtTime(now, true)}</span>
      </span>
      {zone !== 'UTC' && (
        <span className="tb-clock-row">
          <span className="tz">{zone}</span>
          <span className="t">{fmtTime(now, false)}</span>
        </span>
      )}
    </span>
  )
}

const p = (x: number) => String(x).padStart(2, '0')

function fmtTime(n: Date, utc: boolean): string {
  const [h, mi, s] = utc
    ? [n.getUTCHours(), n.getUTCMinutes(), n.getUTCSeconds()]
    : [n.getHours(), n.getMinutes(), n.getSeconds()]
  return `${p(h)}:${p(mi)}:${p(s)}`
}

/**
 * 로컬 시간대의 짧은 이름. KST 하드코딩을 피한다 — 보는 사람이 어디든 맞는다.
 * Intl 은 GMT+9 로 주지만 표기는 Metaplay 를 따라 UTC+9 로 통일한다 — 윗줄 라벨과 같은
 * 단어여야 두 줄이 같은 축(UTC 기준 오프셋)이라는 것이 읽힌다.
 */
function localZone(): string {
  try {
    const raw =
      new Intl.DateTimeFormat('en-US', { timeZoneName: 'short' })
        .formatToParts(new Date())
        .find((x) => x.type === 'timeZoneName')?.value ?? 'LOCAL'
    return raw.replace(/^GMT/, 'UTC')
  } catch {
    return 'LOCAL'
  }
}
