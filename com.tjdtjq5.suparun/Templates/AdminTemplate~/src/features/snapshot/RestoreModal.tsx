import { useEffect, useState } from 'react'
import { Modal } from '../../shared/Modal'
import { loadDiff, summarizeDiff, type Snapshot, type SnapshotDiff } from '../../shared/snapshot'
import { LoadingBlock, Spinner } from '../../shared/Spinner'

/**
 * 복원 확인. **라벨을 손으로 쳐야** 버튼이 열린다.
 *
 * 복원은 TRUNCATE 가 들어가는 파괴적 동작이라 오클릭 비용이 크다. 직전 상태가 자동으로
 * 한 장 찍히므로 되돌아올 수는 있지만, 그건 사고를 수습하는 길이지 사고를 막는 길이 아니다.
 *
 * 무엇이 바뀌는지는 서버 `suparun_snapshot_diff` 가 계산한다 —
 * 행 수는 브라우저에서 세면 테이블마다 왕복이 생기고, 컬럼 차이는 정보 스키마를 봐야 한다.
 */
export function RestoreModal({
  snapshot,
  onClose,
  onConfirm,
  busy,
}: {
  snapshot: Snapshot
  onClose: () => void
  onConfirm: () => void
  busy: boolean
}) {
  const [diff, setDiff] = useState<SnapshotDiff[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [typed, setTyped] = useState('')

  useEffect(() => {
    let alive = true
    void (async () => {
      try {
        const d = await loadDiff(snapshot.schema_name)
        if (alive) setDiff(d)
      } catch (e) {
        if (alive) setError(e instanceof Error ? e.message : String(e))
      }
    })()
    return () => {
      alive = false
    }
  }, [snapshot.schema_name])

  const matched = typed.trim() === snapshot.label
  const summary = diff ? summarizeDiff(diff) : null

  return (
    <Modal
      onClose={onClose}
      maxWidth={620}
      title={<span className="fw-bold px-2">복원 — {snapshot.label}</span>}
      footer={
        <div className="d-flex justify-content-between align-items-center p-3 border-top">
          <span className="text-muted small">{snapshot.schema_name}</span>
          <div className="btn-list">
            <button className="btn" onClick={onClose} disabled={busy}>
              취소
            </button>
            <button
              className="btn btn-danger"
              disabled={!matched || busy || !diff}
              onClick={onConfirm}
            >
              {busy ? (
                <>
                  <Spinner size={12} />
                  복원 중…
                </>
              ) : (
                '이 시점으로 복원'
              )}
            </button>
          </div>
        </div>
      }
    >
      <div style={{ padding: 16 }}>
        {error && <div className="alert alert-danger">{error}</div>}

        {!diff && !error && <LoadingBlock label="바뀔 내용 확인 중" size={24} />}

        {diff && summary && (
          <>
            {summary.changedTables.length === 0 ? (
              <div className="alert alert-info">
                행 수 기준으로는 바뀌는 테이블이 없습니다. 값만 다를 수 있습니다.
              </div>
            ) : (
              <div className="table-responsive" style={{ maxHeight: 260, overflowY: 'auto' }}>
                <table className="table table-sm table-vcenter">
                  <thead>
                    <tr>
                      <th>테이블</th>
                      <th className="text-end">지금</th>
                      <th className="text-end">복원 후</th>
                      <th>컬럼</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.changedTables.map((d) => (
                      <tr key={d.tbl_name}>
                        <td>{d.tbl_name}</td>
                        <td className="text-end text-muted">{d.cur_rows}</td>
                        <td className="text-end">{d.snap_rows}</td>
                        <td>
                          {/* 공통 컬럼만 옮기므로 여기 뜬 것은 '복원되지 않는 부분' 이다 */}
                          {d.added_cols?.length ? (
                            <span className="badge bg-yellow me-1" title={d.added_cols.join(', ')}>
                              +{d.added_cols.length} 기본값
                            </span>
                          ) : null}
                          {d.removed_cols?.length ? (
                            <span className="badge bg-orange" title={d.removed_cols.join(', ')}>
                              -{d.removed_cols.length} 버려짐
                            </span>
                          ) : null}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {summary.skipped.length > 0 && (
              <div className="text-muted small mt-2">
                스냅샷에 없는 테이블 {summary.skipped.length}개는 그대로 둡니다 —{' '}
                {summary.skipped.map((d) => d.tbl_name).join(', ')}
              </div>
            )}

            <div className="text-muted small mt-3">
              지금 상태는 <code>auto</code> 스냅샷으로 자동 저장되므로 되돌아올 수 있습니다.
            </div>

            <div className="mt-3">
              <label className="form-label">
                확인을 위해 <code>{snapshot.label}</code> 을(를) 입력하세요
              </label>
              <input
                className="form-control"
                value={typed}
                autoFocus
                spellCheck={false}
                onChange={(e) => setTyped(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && matched && !busy) onConfirm()
                }}
              />
            </div>
          </>
        )}
      </div>
    </Modal>
  )
}
