import { useEffect, useRef, useState } from 'react'
import { Modal } from '../../shared/Modal'
import { enableColResize } from '../../shared/colResize'
import type { ConfigType } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'
import { ConfigCell } from './ConfigCell'
import { useConfigRows } from './useConfigRows'

/**
 * Config CRUD 화면. 바닐라 renderTable() 을 옮긴 것이다.
 *
 * 검색창과 툴바 버튼은 껍데기(PageHeader)에 있다 — 검색어는 `filter` 로 내려받고,
 * 버튼 동작은 컨텍스트에 등록해 올려보낸다.
 */
export function ConfigPage({ configType, filter }: { configType: ConfigType; filter: string }) {
  const {
    rows,
    error,
    savedCell,
    setField,
    undo,
    addRow,
    copyRow,
    deleteRow,
    reorder,
    exportData,
    importData,
  } = useConfigRows(configType)
  const { setToolbarActions } = useAdmin()
  const [pendingDelete, setPendingDelete] = useState<string | null>(null)
  const hostRef = useRef<HTMLDivElement>(null)
  const tbodyRef = useRef<HTMLTableSectionElement>(null)

  const fields = configType.fields
  const visible = fields.filter((f) => !f.isHidden)
  // 검색 중에는 드래그 정렬을 막는다 (표시 순서 ≠ 실제 순서이므로) — 바닐라와 동일
  const sortable = !filter && fields.some((f) => f.isSortOrder)

  // 툴바 버튼(추가·내보내기·가져오기)은 껍데기(PageHeader)에 있으므로 동작만 올려보낸다.
  useEffect(() => {
    setToolbarActions({ addRow, exportData, importData })
    return () => setToolbarActions(null)
  }, [addRow, exportData, importData, setToolbarActions])

  // Ctrl+Z — 입력 중에는 무시한다 (바닐라와 동일 정책)
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (!(e.ctrlKey || e.metaKey) || e.key !== 'z') return
      const t = e.target as HTMLElement | null
      if (t?.matches('input,textarea,select')) return
      e.preventDefault()
      void undo()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [undo])

  const filtered =
    rows && filter
      ? rows.filter((row) =>
          fields.some((f) =>
            String(row[f.name] ?? '')
              .toLowerCase()
              .includes(filter.toLowerCase()),
          ),
        )
      : (rows ?? [])

  useEffect(() => {
    if (!rows || !hostRef.current) return
    // 바닐라와 동일 — DOM 컬럼 순서가 [drag(옵션), ...visibleFields, action] 이므로
    // 양 끝에 null 패딩을 둬야 drag/action 컬럼이 인라인 width 를 유지한다
    enableColResize(hostRef.current, 'config_' + configType.tableName, {
      fields: sortable ? [null, ...visible, null] : [...visible, null],
      data: filtered,
    })
  }, [rows, filtered, visible, sortable, configType.tableName])

  // 드래그 정렬 — 목록이 바뀔 때마다 재생성 (바닐라도 재렌더마다 재생성했다)
  useEffect(() => {
    const el = tbodyRef.current
    const Sortable = window.Sortable
    if (!sortable || !el || !Sortable || !rows) return
    const inst = Sortable.create(el, {
      handle: '.drag-handle',
      animation: 150,
      ghostClass: 'sortable-ghost',
      forceFallback: true,
      fallbackOnBody: true,
      fallbackTolerance: 4,
      onEnd: () => {
        const ids = [...el.children].map((tr) => (tr as HTMLElement).dataset.id ?? '')
        void reorder(ids)
      },
    })
    return () => inst.destroy()
  }, [sortable, rows, filtered, reorder])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!rows) {
    return (
      <div className="loading-spinner">
        <div className="spinner-border text-primary" role="status" />
      </div>
    )
  }

  if (rows.length === 0 && !filter) {
    return (
      <div className="empty-state">
        <i className="ti ti-inbox" />
        <h3>데이터가 없습니다</h3>
        <p>위의 &quot;+ 추가&quot; 버튼으로 첫 행을 만들어보세요.</p>
      </div>
    )
  }

  if (filtered.length === 0) {
    return (
      <div className="empty-state">
        <i className="ti ti-search" />
        <h3>검색 결과 없음</h3>
        <p>&quot;{filter}&quot;에 대한 결과가 없습니다.</p>
      </div>
    )
  }

  return (
    <div ref={hostRef}>
      <table className="table table-vcenter card-table table-hover table-striped">
        <thead>
          <tr>
            {sortable && <th className="drag-col" style={{ width: 32 }} />}
            {visible.map((f) => (
              <th key={f.name}>
                {f.isPrimaryKey ? (
                  <i className="ti ti-key text-yellow me-1" title="Primary Key" />
                ) : f.isRequired ? (
                  <span className="text-red me-1" title="필수">
                    *
                  </span>
                ) : f.foreignKey || f.foreignKeyList ? (
                  <i className="ti ti-link text-cyan me-1" title="Foreign Key" />
                ) : null}
                {f.name}
              </th>
            ))}
            <th style={{ width: 80 }} />
          </tr>
        </thead>
        <tbody ref={tbodyRef}>
          {filtered.map((row, ri) => {
            const rowId = String(row.id ?? '')
            return (
              <tr
                key={rowId || ri}
                data-id={rowId}
                style={{ animationDelay: `${Math.min(ri * 20, 400)}ms` }}
              >
                {sortable && (
                  <td
                    className="drag-handle"
                    style={{ cursor: 'grab', width: 32, color: '#6c757d', textAlign: 'center' }}
                    title="드래그로 순서 변경"
                  >
                    <i className="ti ti-grip-vertical" />
                  </td>
                )}
                {visible.map((f) => (
                  <ConfigCell
                    key={f.name}
                    row={row}
                    field={f}
                    saved={savedCell === rowId}
                    onChange={(name, value, immediate) => setField(rowId, name, value, immediate)}
                  />
                ))}
                <td>
                  <div className="btn-list flex-nowrap">
                    <button
                      className="btn btn-ghost-primary btn-icon btn-sm"
                      title="복사"
                      onClick={() => void copyRow(rowId)}
                    >
                      <i className="ti ti-copy" />
                    </button>
                    <button
                      className="btn btn-ghost-danger btn-icon btn-sm"
                      title="삭제"
                      onClick={() => setPendingDelete(rowId)}
                    >
                      <i className="ti ti-trash" />
                    </button>
                  </div>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      {pendingDelete !== null && (
        <Modal
          onClose={() => setPendingDelete(null)}
          maxWidth={380}
          title={<span className="fw-bold px-2">행 삭제</span>}
        >
          <div style={{ padding: 16 }}>
            <p className="mb-3">
              정말 삭제하시겠습니까? <code>ID: {pendingDelete}</code>
            </p>
            <div className="d-flex gap-2 justify-content-end">
              <button className="btn btn-outline-secondary" onClick={() => setPendingDelete(null)}>
                취소
              </button>
              <button
                className="btn btn-danger"
                onClick={() => {
                  void deleteRow(pendingDelete)
                  setPendingDelete(null)
                }}
              >
                <i className="ti ti-trash me-1" />
                삭제
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  )
}
