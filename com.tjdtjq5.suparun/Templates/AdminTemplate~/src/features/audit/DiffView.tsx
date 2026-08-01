/**
 * before→after 터미널 diff — 감사 상세(#26)와 버전 비교(#33)가 같은 문법을 쓴다.
 * 키 단위로 비교해 바뀐 키만 `- / +` 로 보여주고, 안 바뀐 키는 접는다.
 */

export function tryParse(s: string | null | undefined): unknown {
  if (!s) return null
  try {
    return JSON.parse(s)
  } catch {
    return s
  }
}

export function DiffView({
  before,
  after,
  emptyText = '이 이벤트에는 페이로드가 없습니다 (열람 등 데이터 무변경 이벤트).',
}: {
  before?: string | null
  after?: string | null
  emptyText?: string
}) {
  const b = tryParse(before)
  const a = tryParse(after)

  if (b === null && a === null) {
    return <p className="text-muted">{emptyText}</p>
  }

  // 객체가 아니면(스냅샷 이름 문자열 등) diff 가 아니라 값 그대로가 정직하다.
  if (typeof b !== 'object' || typeof a !== 'object' || Array.isArray(b) || Array.isArray(a)) {
    return (
      <pre className="audit-diff">
        {b !== null && <div className="d-del">- {JSON.stringify(b)}</div>}
        {a !== null && <div className="d-add">+ {JSON.stringify(a)}</div>}
      </pre>
    )
  }

  const bo = (b ?? {}) as Record<string, unknown>
  const ao = (a ?? {}) as Record<string, unknown>
  const keys = [...new Set([...Object.keys(bo), ...Object.keys(ao)])].sort()
  const changed = keys.filter((k) => JSON.stringify(bo[k]) !== JSON.stringify(ao[k]))
  const same = keys.filter((k) => !changed.includes(k))

  return (
    <>
      <pre className="audit-diff">
        {changed.length === 0 && <div className="text-muted">값 변화가 없는 기록입니다 (동일값 저장).</div>}
        {changed.map((k) => (
          <div key={k}>
            {k in bo && (
              <div className="d-del">
                - {k}: {JSON.stringify(bo[k])}
              </div>
            )}
            {k in ao && (
              <div className="d-add">
                + {k}: {JSON.stringify(ao[k])}
              </div>
            )}
          </div>
        ))}
      </pre>
      {same.length > 0 && (
        <details>
          <summary className="text-muted" style={{ cursor: 'pointer' }}>
            변경 없는 필드 {same.length}개
          </summary>
          <pre className="audit-diff mt-2">
            {same.map((k) => (
              <div key={k} className="text-muted">
                {'  '}
                {k}: {JSON.stringify(ao[k] ?? bo[k])}
              </div>
            ))}
          </pre>
        </details>
      )}
    </>
  )
}
