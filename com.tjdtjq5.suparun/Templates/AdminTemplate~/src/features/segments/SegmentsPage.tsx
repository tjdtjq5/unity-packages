import { useCallback, useEffect, useState } from 'react'
import { createSegment, listSegments, type Segment } from '../../shared/segments'
import { LoadingBlock } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import { timeAgo } from '../audit/format'
import { useAdmin } from '../shell/AdminContext'

/**
 * Player Segments 목록 (#44, Metaplay 동형 — 100-segments.png).
 *
 * 대상 수는 여기서 세지 않는다 — 전수 평가라 목록에서 남발하면 세그먼트 수 × 유저 수의
 * 쿼리가 된다. 수는 상세가 센다.
 */
export function SegmentsPage() {
  const { canWrite, navigate, setPageSubtitle } = useAdmin()
  const [segments, setSegments] = useState<Segment[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')

  const load = useCallback(async () => {
    try {
      const r = await listSegments()
      setSegments(r)
      setPageSubtitle(`${r.length}개`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [setPageSubtitle])

  useEffect(() => {
    void load()
  }, [load])

  async function create() {
    if (!name.trim()) return toast('세그먼트 이름을 입력하세요.', 'error')
    try {
      const seg = await createSegment({
        id: 'seg_' + Math.random().toString(36).slice(2, 10),
        name: name.trim(),
        description: null,
        match: 'all',
        conditions: [],
      })
      setName('')
      navigate({ kind: 'segment', id: seg.id })
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    }
  }

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>세그먼트를 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!segments) return <LoadingBlock label="세그먼트 불러오는 중" />

  return (
    <div className="m-3">
      <p className="text-muted mb-2">
        세그먼트는 <b>조건으로 정의되는 플레이어 부분집합</b>입니다 — 브로드캐스트·실험·오퍼가
        대상을 고를 때 공용으로 씁니다. 소속은 저장되지 않고 매번 평가됩니다 (ADR-0011).
      </p>

      {canWrite && (
        <div className="row g-2 mb-3" style={{ maxWidth: 480 }}>
          <div className="col">
            <input
              className="form-control form-control-sm"
              placeholder="새 세그먼트 이름"
              value={name}
              onChange={(e) => setName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') void create()
              }}
            />
          </div>
          <div className="col-auto">
            <button className="btn btn-primary btn-sm" onClick={() => void create()}>
              <i className="ti ti-plus me-1" /> 만들기
            </button>
          </div>
        </div>
      )}

      {segments.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-users-group" />
          <h3>세그먼트가 없습니다</h3>
          <p>{canWrite ? '위에서 첫 세그먼트를 만드세요.' : '아직 정의된 세그먼트가 없습니다.'}</p>
        </div>
      ) : (
        <div className="table-responsive">
          <table className="table table-sm table-hover">
            <thead>
              <tr>
                <th>이름</th>
                <th>설명</th>
                <th style={{ width: 90 }}>결합</th>
                <th style={{ width: 90 }}>조건 수</th>
                <th style={{ width: 120 }}>수정</th>
              </tr>
            </thead>
            <tbody>
              {segments.map((s) => (
                <tr key={s.id} style={{ cursor: 'pointer' }} onClick={() => navigate({ kind: 'segment', id: s.id })}>
                  <td><b>{s.name}</b></td>
                  <td className="text-muted">{s.description || '-'}</td>
                  <td><span className="badge">{s.match}</span></td>
                  <td>{s.conditions.length}</td>
                  <td className="text-muted">{timeAgo(s.updated_at ?? s.created_at)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
