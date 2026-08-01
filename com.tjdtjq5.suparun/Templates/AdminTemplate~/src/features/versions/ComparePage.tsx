import { useCallback, useEffect, useMemo, useState } from 'react'
import { isPreview } from '../../shared/env'
import { LoadingBlock } from '../../shared/Spinner'
import {
  ACTIVE_COORD,
  diffRows,
  diffTables,
  listVersions,
  type ConfigVersion,
  type RowDiff,
  type TableDiff,
} from '../../shared/versions'
import { DiffView } from '../audit/DiffView'
import { timeAgo } from '../audit/format'

/**
 * 버전 비교 (#32·#33 — Metaplay Compare Game Configs 동형: 62/63-game-config-diff*.png).
 *
 * Baseline/New 좌표를 고르고(활성본 public 포함) 테이블 단위 배지 → 펼치면 행 단위 diff.
 * Added/Removed/Modified 체크박스가 양쪽 단위 모두를 거른다. 두 좌표가 같은 내용이면
 * 그 사실을 명시한다 — 다른 게 없다는 것도 정보다.
 */
export function ComparePage({ base: baseInit, next: nextInit }: { base?: string; next?: string }) {
  const [versions, setVersions] = useState<ConfigVersion[] | null>(null)
  const [base, setBase] = useState(baseInit ?? ACTIVE_COORD)
  const [next, setNext] = useState(nextInit ?? '')
  const [tables, setTables] = useState<TableDiff[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [show, setShow] = useState({ added: true, removed: true, modified: true })

  useEffect(() => {
    if (isPreview()) {
      setVersions([])
      return
    }
    void listVersions()
      .then((v) => {
        setVersions(v)
        // next 미지정이면 가장 최근 버전과 활성본을 비교한다 — 가장 흔한 질문이 그것이다.
        setNext((cur) => cur || (v[0]?.schema_name ?? ACTIVE_COORD))
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
  }, [])

  const runDiff = useCallback(async () => {
    if (!base || !next) return
    setTables(null)
    setError(null)
    try {
      setTables(await diffTables(base, next))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [base, next])

  useEffect(() => {
    if (versions) void runDiff()
  }, [versions, runDiff])

  const changed = useMemo(
    () =>
      (tables ?? []).filter(
        (t) =>
          (show.added && t.added > 0) ||
          (show.removed && t.removed > 0) ||
          (show.modified && t.modified > 0),
      ),
    [tables, show],
  )

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>비교하지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!versions) return <LoadingBlock label="버전 목록 불러오는 중" />

  const coordLabel = (c: string) => {
    if (c === ACTIVE_COORD) return 'public (활성본)'
    const v = versions.find((x) => x.schema_name === c)
    return v ? `${v.label} · ${v.content_hash?.slice(0, 12)} · ${timeAgo(v.created_at)}` : c
  }

  const options = [ACTIVE_COORD, ...versions.map((v) => v.schema_name)]

  return (
    <div className="compare-page">
      <p className="text-muted m-3 mb-2">두 버전의 차이를 봅니다. 게시 전 검토가 이 화면의 일입니다.</p>

      {/* ── 좌표 선택 (Baseline / New + 스왑) ── */}
      <div className="m-3 mt-0">
        <div className="row g-2 align-items-end">
          <div className="col-sm-5">
            <label className="form-label mb-1">Baseline</label>
            <select className="form-select form-select-sm" value={base} onChange={(e) => setBase(e.target.value)}>
              {options.map((c) => (
                <option key={c} value={c}>
                  {coordLabel(c)}
                </option>
              ))}
            </select>
          </div>
          <div className="col-auto">
            <button
              className="btn btn-sm"
              title="양쪽 바꾸기"
              onClick={() => {
                setBase(next)
                setNext(base)
              }}
            >
              <i className="ti ti-arrows-exchange" />
            </button>
          </div>
          <div className="col-sm-5">
            <label className="form-label mb-1">New</label>
            <select className="form-select form-select-sm" value={next} onChange={(e) => setNext(e.target.value)}>
              {options.map((c) => (
                <option key={c} value={c}>
                  {coordLabel(c)}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="d-flex gap-3 mt-2">
          {(['added', 'removed', 'modified'] as const).map((k) => (
            <label key={k} className="form-check m-0">
              <input
                type="checkbox"
                className="form-check-input"
                checked={show[k]}
                onChange={(e) => setShow((s) => ({ ...s, [k]: e.target.checked }))}
              />
              <span className="form-check-label text-capitalize">{k}</span>
            </label>
          ))}
        </div>
      </div>

      {/* ── 내용 ── */}
      {!tables ? (
        <LoadingBlock label="비교 중" />
      ) : base === next ? (
        <div className="empty-state">
          <i className="ti ti-equal" />
          <h3>같은 좌표입니다</h3>
          <p>서로 다른 두 버전을 고르세요.</p>
        </div>
      ) : changed.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-equal" />
          <h3>두 버전이 동일합니다</h3>
          <p>선택한 필터 기준으로 차이가 없습니다.</p>
        </div>
      ) : (
        <div className="m-2">
          {changed.map((t) => (
            <TableSection key={t.tbl_name} t={t} base={base} next={next} show={show} />
          ))}
        </div>
      )}
    </div>
  )
}

/** 테이블 하나 — 배지 요약(#32), 펼치면 행 단위 diff 를 그때 불러온다(#33). */
function TableSection({
  t,
  base,
  next,
  show,
}: {
  t: TableDiff
  base: string
  next: string
  show: { added: boolean; removed: boolean; modified: boolean }
}) {
  const [rows, setRows] = useState<RowDiff[] | null>(null)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open || rows) return
    void diffRows(base, next, t.tbl_name).then(setRows).catch(() => setRows([]))
  }, [open, rows, base, next, t.tbl_name])

  const visible = (rows ?? []).filter((r) => show[r.status])

  return (
    <details className="compare-table" open={open} onToggle={(e) => setOpen((e.target as HTMLDetailsElement).open)}>
      <summary>
        <b>{t.tbl_name}</b>
        {t.added > 0 && <span className="badge bg-green ms-2">Added {t.added}</span>}
        {t.removed > 0 && <span className="badge bg-red ms-2">Removed {t.removed}</span>}
        {t.modified > 0 && <span className="badge bg-orange ms-2">Modified {t.modified}</span>}
        {(t.base_missing || t.new_missing) && (
          <span className="badge bg-secondary ms-2" title="한쪽 좌표에 테이블 자체가 없습니다">
            table {t.base_missing ? 'missing in baseline' : 'missing in new'}
          </span>
        )}
      </summary>

      {open &&
        (rows === null ? (
          <LoadingBlock label={`${t.tbl_name} 행 비교 중`} size={20} />
        ) : (
          <div className="compare-rows">
            {visible.map((r) => (
              <div key={r.row_id} className="compare-row">
                <div className="mb-1">
                  <code>{r.row_id}</code>{' '}
                  <span
                    className={`badge ${
                      r.status === 'added' ? 'bg-green' : r.status === 'removed' ? 'bg-red' : 'bg-orange'
                    }`}
                  >
                    {r.status}
                  </span>
                </div>
                <DiffView before={r.before_json} after={r.after_json} />
              </div>
            ))}
            {visible.length === 0 && <p className="text-muted p-2">필터 기준으로 보일 행이 없습니다.</p>}
          </div>
        ))}
    </details>
  )
}
