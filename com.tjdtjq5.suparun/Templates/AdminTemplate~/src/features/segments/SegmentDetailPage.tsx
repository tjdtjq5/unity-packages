import { useEffect, useMemo, useState } from 'react'
import {
  listSegments,
  removeSegment,
  segmentCount,
  updateSegment,
  type Segment,
  type SegmentCondition,
} from '../../shared/segments'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import { useAdmin } from '../shell/AdminContext'

/**
 * 세그먼트 상세 (#44·#45, Metaplay 동형 — 101-segment-detail.png).
 *
 * 조건 폼의 어휘는 ADR-0011 그대로다: source 3종(account/system/table), 다행 표는 agg,
 * 표·컬럼 후보는 메타(playerColumn 있는 table_types)에서 온다 — 표가 늘면 폼도 는다.
 * 대상 수는 저장된 정의 기준이다(전수 평가) — 저장하지 않은 편집은 세지 않는다.
 */
export function SegmentDetailPage({ id }: { id: string }) {
  const { canWrite, tableTypes, navigate } = useAdmin()
  const [segment, setSegment] = useState<Segment | null | undefined>(undefined)
  const [count, setCount] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)

  // 편집 상태 — 저장 전까지 로컬이다.
  const [name, setName] = useState('')
  const [desc, setDesc] = useState('')
  const [match, setMatch] = useState<'all' | 'any'>('all')
  const [conds, setConds] = useState<SegmentCondition[]>([])

  const userTables = useMemo(() => tableTypes.filter((t) => t.playerColumn), [tableTypes])

  useEffect(() => {
    void listSegments()
      .then((all) => {
        const s = all.find((x) => x.id === id) ?? null
        setSegment(s)
        if (s) {
          setName(s.name)
          setDesc(s.description ?? '')
          setMatch(s.match)
          setConds(s.conditions)
          void segmentCount(s.id).then(setCount).catch(() => setCount(null))
        }
      })
      .catch(() => setSegment(null))
  }, [id])

  if (segment === undefined) return <LoadingBlock label="세그먼트 불러오는 중" />
  if (segment === null) {
    return (
      <div className="empty-state">
        <i className="ti ti-users-group" />
        <h3>존재하지 않는 세그먼트입니다</h3>
        <button className="btn btn-sm" onClick={() => navigate({ kind: 'segments' })}>
          <i className="ti ti-arrow-left me-1" /> 목록으로
        </button>
      </div>
    )
  }

  // #44 AC — 저장 전 입력 검증. 서버(평가기)도 화이트리스트로 거부하지만, 폼이 먼저 말한다.
  function validate(): string | null {
    if (!name.trim()) return '이름을 입력하세요.'
    for (const [i, c] of conds.entries()) {
      const at = `조건 ${i + 1}: `
      if (c.source === 'account') {
        if (!c.column) return at + '계정 속성을 고르세요.'
        if (c.value === undefined || c.value === '' || isNaN(Number(c.value))) return at + '숫자 값을 입력하세요.'
      } else if (c.source === 'system') {
        if (!c.column) return at + '상태를 고르세요.'
      } else {
        if (!c.table) return at + '표를 고르세요.'
        const agg = c.agg ?? 'exists'
        if (agg !== 'exists' && agg !== 'count' && !c.column) return at + '집계할 컬럼을 고르세요.'
        if (agg !== 'exists' && (c.value === undefined || c.value === '' || isNaN(Number(c.value))))
          return at + '숫자 값을 입력하세요.'
      }
    }
    return null
  }

  async function save() {
    const err = validate()
    if (err) return toast(err, 'error')
    setBusy(true)
    try {
      await updateSegment(segment!.id, {
        name: name.trim(),
        description: desc.trim() || null,
        match,
        conditions: conds,
      })
      toast('저장했습니다.')
      setCount(null)
      void segmentCount(segment!.id).then(setCount).catch(() => setCount(null))
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    setBusy(true)
    try {
      await removeSegment(segment!.id)
      toast('삭제했습니다.')
      navigate({ kind: 'segments' })
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setBusy(false)
    }
  }

  return (
    <div className="m-3" style={{ maxWidth: 760 }}>
      <div className="d-flex align-items-center mb-2">
        <h2 className="m-0 me-2">{segment.name}</h2>
        <span className="text-muted">
          예상 대상 {count === null ? <Spinner size={12} /> : <b>{count}명</b>}
        </span>
        <code className="ms-auto text-muted">{segment.id}</code>
      </div>

      <div className="row g-2 mb-2">
        <div className="col-sm-5">
          <label className="form-label mb-1">이름</label>
          <input className="form-control form-control-sm" value={name} disabled={!canWrite}
            onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="col-sm-5">
          <label className="form-label mb-1">설명</label>
          <input className="form-control form-control-sm" value={desc} disabled={!canWrite}
            onChange={(e) => setDesc(e.target.value)} />
        </div>
        <div className="col-sm-2">
          <label className="form-label mb-1">결합</label>
          <select className="form-select form-select-sm" value={match} disabled={!canWrite}
            onChange={(e) => setMatch(e.target.value as 'all' | 'any')}>
            <option value="all">all — 전부 만족</option>
            <option value="any">any — 하나라도</option>
          </select>
        </div>
      </div>

      <div className="mb-2">
        {conds.map((c, i) => (
          <ConditionRow key={i} cond={c} canWrite={canWrite} userTables={userTables}
            onChange={(next) => setConds((prev) => prev.map((x, j) => (j === i ? next : x)))}
            onRemove={() => setConds((prev) => prev.filter((_, j) => j !== i))} />
        ))}
        {conds.length === 0 && (
          <p className="text-muted">
            조건이 없습니다 — <code>all</code> 은 전원, <code>any</code> 는 아무도 매치하지 않습니다.
          </p>
        )}
      </div>

      {canWrite && (
        <div className="btn-list">
          <button className="btn btn-sm" onClick={() =>
            setConds((prev) => [...prev, { source: 'account', column: 'last_sign_in_at', op: 'since_days', value: 7 }])}>
            <i className="ti ti-plus me-1" /> 조건 추가
          </button>
          <button className="btn btn-primary btn-sm" disabled={busy} onClick={() => void save()}>
            {busy ? <Spinner size={12} /> : <i className="ti ti-device-floppy me-1" />} 저장
          </button>
          <button className="btn btn-outline-danger btn-sm ms-auto" disabled={busy} onClick={() => void remove()}>
            <i className="ti ti-trash me-1" /> 삭제
          </button>
        </div>
      )}
    </div>
  )
}

/** 조건 한 줄 — source 에 따라 폼이 갈린다 (ADR-0011 어휘 그대로). */
function ConditionRow({
  cond, canWrite, userTables, onChange, onRemove,
}: {
  cond: SegmentCondition
  canWrite: boolean
  userTables: { tableName: string; playerColumn?: string; fields: { name: string; type: string }[] }[]
  onChange: (c: SegmentCondition) => void
  onRemove: () => void
}) {
  const table = userTables.find((t) => t.tableName === cond.table)
  const numericCols = table
    ? table.fields.filter((f) => ['int', 'long', 'number'].includes(f.type)).map((f) => f.name.toLowerCase())
    : []
  const agg = cond.agg ?? 'exists'

  return (
    <div className="row g-1 align-items-center mb-1">
      <div className="col-auto">
        <select className="form-select form-select-sm" value={cond.source} disabled={!canWrite}
          onChange={(e) => {
            const source = e.target.value as SegmentCondition['source']
            onChange(source === 'account'
              ? { source, column: 'last_sign_in_at', op: 'since_days', value: 7 }
              : source === 'system'
                ? { source, column: 'is_developer', op: '=', value: false }
                : { source, table: userTables[0]?.tableName, agg: 'exists', op: 'exists' })
          }}>
          <option value="account">계정</option>
          <option value="system">상태</option>
          <option value="table">게임 데이터</option>
        </select>
      </div>

      {cond.source === 'account' && (
        <>
          <div className="col-auto">
            <select className="form-select form-select-sm" value={cond.column} disabled={!canWrite}
              onChange={(e) => onChange({ ...cond, column: e.target.value })}>
              <option value="last_sign_in_at">최근 로그인</option>
              <option value="created_at">가입일</option>
            </select>
          </div>
          <div className="col-auto"><span className="text-muted">이(가)</span></div>
          <div className="col-auto" style={{ width: 90 }}>
            <input type="number" className="form-control form-control-sm" value={String(cond.value ?? '')}
              disabled={!canWrite} onChange={(e) => onChange({ ...cond, value: Number(e.target.value) })} />
          </div>
          <div className="col-auto"><span className="text-muted">일 이내</span></div>
        </>
      )}

      {cond.source === 'system' && (
        <>
          <div className="col-auto">
            <select className="form-select form-select-sm" value={cond.column} disabled={!canWrite}
              onChange={(e) => onChange({ ...cond, column: e.target.value })}>
              <option value="is_developer">개발자</option>
              <option value="banned">밴</option>
            </select>
          </div>
          <div className="col-auto">
            <select className="form-select form-select-sm" value={String(cond.value)} disabled={!canWrite}
              onChange={(e) => onChange({ ...cond, value: e.target.value === 'true' })}>
              <option value="true">이다</option>
              <option value="false">아니다</option>
            </select>
          </div>
        </>
      )}

      {cond.source === 'table' && (
        <>
          <div className="col-auto">
            <select className="form-select form-select-sm" value={cond.table} disabled={!canWrite}
              onChange={(e) => onChange({ ...cond, table: e.target.value, column: undefined })}>
            {userTables.map((t) => (
              <option key={t.tableName} value={t.tableName}>{t.tableName}</option>
            ))}
            </select>
          </div>
          <div className="col-auto">
            <select className="form-select form-select-sm" value={agg} disabled={!canWrite}
              onChange={(e) => {
                const a = e.target.value
                onChange({ ...cond, agg: a, op: a === 'exists' ? 'exists' : cond.op === 'exists' ? '>=' : cond.op })
              }}>
              <option value="exists">행이 있다</option>
              <option value="count">행 수</option>
              <option value="sum">합계</option>
              <option value="max">최대</option>
              <option value="min">최소</option>
            </select>
          </div>
          {agg !== 'exists' && agg !== 'count' && (
            <div className="col-auto">
              <select className="form-select form-select-sm" value={cond.column ?? ''} disabled={!canWrite}
                onChange={(e) => onChange({ ...cond, column: e.target.value })}>
                <option value="">컬럼…</option>
                {numericCols.map((c) => (
                  <option key={c} value={c}>{c}</option>
                ))}
              </select>
            </div>
          )}
          {agg !== 'exists' && (
            <>
              <div className="col-auto">
                <select className="form-select form-select-sm" value={cond.op} disabled={!canWrite}
                  onChange={(e) => onChange({ ...cond, op: e.target.value })}>
                  <option value=">=">≥</option>
                  <option value="<=">≤</option>
                  <option value="=">=</option>
                  <option value="!=">≠</option>
                </select>
              </div>
              <div className="col-auto" style={{ width: 100 }}>
                <input type="number" className="form-control form-control-sm" value={String(cond.value ?? '')}
                  disabled={!canWrite} onChange={(e) => onChange({ ...cond, value: Number(e.target.value) })} />
              </div>
            </>
          )}
        </>
      )}

      {canWrite && (
        <div className="col-auto ms-auto">
          <button className="btn btn-sm btn-icon" title="조건 삭제" onClick={onRemove}>
            <i className="ti ti-x" />
          </button>
        </div>
      )}
    </div>
  )
}
