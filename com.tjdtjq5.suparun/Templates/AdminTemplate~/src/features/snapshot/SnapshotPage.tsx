import { useState } from 'react'
import { queueIdConstants } from '../../shared/idsync'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import type { Snapshot } from '../../shared/snapshot'
import { toast } from '../../shared/toast'
import { RestoreModal } from './RestoreModal'
import { useSnapshots } from './useSnapshots'

/**
 * 스냅샷 목록 화면 (ADR-0004 Backlog).
 *
 * `[SpecData]` 를 통째로 찍고 되돌린다. 되돌리기는 `RestoreModal` 이 라벨 타이핑으로 막는다.
 *
 * 핀은 **보관 여부**지 출처가 아니다 — 복원 직전 자동으로 찍힌 것도 핀을 꽂으면 남는다.
 * 그 구분이 필요한 이유: 되돌리기 직전 상태가 사실 가장 중요한 지점인 경우가 잦다.
 */
export function SnapshotPage() {
  const { snapshots, error, busy, create, restore, remove, patch } = useSnapshots()
  const [restoring, setRestoring] = useState<Snapshot | null>(null)

  async function onCreate() {
    const label = window.prompt('스냅샷 이름 (영문·숫자 권장)', '')
    if (label === null) return
    if (!label.trim()) {
      toast('이름을 입력하세요', 'error')
      return
    }
    const comment = window.prompt('메모 (선택)', '') ?? undefined
    await create(label.trim(), comment)
  }

  async function onRestore(s: Snapshot) {
    const backup = await restore(s.schema_name)
    setRestoring(null)
    if (backup) {
      // 복원은 PK 집합째 되돌린다 — Id 상수도 따라가야 한다. 곧 리로드되므로 즉시 발사.
      queueIdConstants(true)
      toast(`"${s.label}" 로 복원됨 — 직전 상태는 ${backup}`, 'success')
      // 복원은 보고 있던 표까지 바꾼다. 낡은 화면을 그대로 두면 다음 편집이 옛 값 위에 얹힌다.
      setTimeout(() => location.reload(), 900)
    }
  }

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>스냅샷 목록을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!snapshots) return <LoadingBlock label="스냅샷 목록 불러오는 중" />

  return (
    <div>
      <table className="table table-vcenter card-table table-striped">
        <thead>
          <tr>
            <th style={{ width: 40 }} title="보관 — 켜면 자동 정리에서 제외됩니다" />
            <th>이름</th>
            <th>메모</th>
            <th style={{ width: 150 }}>시각</th>
            <th style={{ width: 150 }} />
          </tr>
        </thead>
        <tbody>
          {snapshots.map((s) => (
            <tr key={s.schema_name}>
              <td>
                <button
                  className={`btn btn-icon btn-sm ${s.pinned ? 'btn-ghost-warning' : 'btn-ghost-secondary'}`}
                  title={s.pinned ? '보관 중 — 눌러서 해제' : '자동 정리 대상 — 눌러서 보관'}
                  onClick={() => void patch(s.schema_name, { pinned: !s.pinned })}
                >
                  <i className={s.pinned ? 'ti ti-pin-filled' : 'ti ti-pin'} />
                </button>
              </td>
              <td>
                <InlineText
                  value={s.label}
                  placeholder="이름 없음"
                  onSave={(v) => void patch(s.schema_name, { label: v })}
                />
                {s.created_by_auto && (
                  <span className="badge bg-blue-lt ms-2" title="복원 직전 자동 저장">
                    auto
                  </span>
                )}
              </td>
              <td className="text-muted">
                <InlineText
                  value={s.comment ?? ''}
                  placeholder="메모 추가…"
                  onSave={(v) => void patch(s.schema_name, { comment: v || null })}
                />
              </td>
              <td className="text-muted">{new Date(s.created_at).toLocaleString('ko-KR')}</td>
              <td>
                <div className="btn-list flex-nowrap justify-content-end">
                  <button
                    className="btn btn-ghost-primary btn-sm"
                    disabled={busy !== null}
                    onClick={() => setRestoring(s)}
                  >
                    <i className="ti ti-history me-1" />
                    복원
                  </button>
                  <button
                    className="btn btn-ghost-danger btn-icon btn-sm"
                    title="삭제"
                    disabled={busy !== null}
                    onClick={() => {
                      if (window.confirm(`"${s.label}" 스냅샷을 지웁니다. 계속할까요?`))
                        void remove(s.schema_name)
                    }}
                  >
                    <i className="ti ti-trash" />
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {snapshots.length === 0 && (
        <div className="empty-state">
          <i className="ti ti-camera" />
          <h3>저장된 시점이 없습니다</h3>
          <p>위험한 편집 전에 한 장 찍어두면 언제든 그 시점으로 되돌릴 수 있습니다.</p>
        </div>
      )}

      <div className="p-3 border-top d-flex justify-content-between align-items-center">
        <div className="text-muted small">
          <code>[SpecData]</code> 전체를 담습니다. 플레이어 데이터는 포함되지 않습니다.
          <br />
          핀이 없는 자동 저장본은 최근 5개까지만 남습니다.
        </div>
        <button className="btn btn-primary" disabled={busy !== null} onClick={() => void onCreate()}>
          {busy === 'create' ? (
            <>
              <Spinner size={12} />
              저장 중…
            </>
          ) : (
            <>
              <i className="ti ti-camera me-1" />
              지금 저장
            </>
          )}
        </button>
      </div>

      {restoring && (
        <RestoreModal
          snapshot={restoring}
          busy={busy === restoring.schema_name}
          onClose={() => setRestoring(null)}
          onConfirm={() => void onRestore(restoring)}
        />
      )}
    </div>
  )
}

/** 클릭하면 입력칸이 되는 셀. Config 표의 인라인 편집과 같은 감각으로 맞춘다. */
function InlineText({
  value,
  placeholder,
  onSave,
}: {
  value: string
  placeholder: string
  onSave: (v: string) => void
}) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(value)

  if (!editing) {
    return (
      <span
        role="button"
        tabIndex={0}
        className={value ? '' : 'text-muted'}
        style={{ cursor: 'text' }}
        onClick={() => {
          setDraft(value)
          setEditing(true)
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            setDraft(value)
            setEditing(true)
          }
        }}
      >
        {value || placeholder}
      </span>
    )
  }

  function commit() {
    setEditing(false)
    if (draft !== value) onSave(draft.trim())
  }

  return (
    <input
      className="form-control form-control-sm"
      value={draft}
      autoFocus
      spellCheck={false}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') commit()
        if (e.key === 'Escape') setEditing(false)
      }}
    />
  )
}
