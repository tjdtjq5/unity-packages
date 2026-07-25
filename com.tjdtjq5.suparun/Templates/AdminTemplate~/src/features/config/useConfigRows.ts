import { useCallback, useEffect, useRef, useState } from 'react'
import { deleteRow as dbDelete, insertRow, selectAll, updateRow, upsertMany } from '../../shared/db'
import { toast } from '../../shared/toast'
import type { ConfigRow, ConfigType } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'

const SAVE_DEBOUNCE_MS = 500

interface UndoEntry {
  rowId: string
  fieldName: string
  oldValue: unknown
  typeName: string
}

/**
 * Config 행 데이터 + 저장 + 되돌리기. 바닐라의
 * selectType(데이터 로드) / debounceSave / commitSave / toggleBool / undoStack 을 대체한다.
 *
 * 저장은 바닐라와 동일하게 **행 전체를 PUT** 한다. 서버가 id 를 바꿔 돌려주면
 * (PK 를 편집한 경우) 그 값으로 교체한다.
 */
export function useConfigRows(configType: ConfigType) {
  const { setPageSubtitle } = useAdmin()
  // 서버를 거칠 때는 서버가 PK 를 알고 있었다. 직접 붙는 지금은 우리가 알아야 한다.
  // 대부분 `id` 지만 메타에 있는 값을 쓰는 편이 정확하다.
  const pk = configType.fields.find((f) => f.isPrimaryKey)?.name ?? 'id'
  const [rows, setRows] = useState<ConfigRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [savedCell, setSavedCell] = useState<string | null>(null)

  // 렌더와 무관한 것들은 ref 로 — 값이 바뀌어도 다시 그릴 이유가 없다
  const timers = useRef<Record<string, ReturnType<typeof setTimeout>>>({})
  const undoStack = useRef<UndoEntry[]>([])
  const rowsRef = useRef<ConfigRow[] | null>(null)
  rowsRef.current = rows

  const load = useCallback(async () => {
    setRows(null)
    setError(null)
    try {
      const data = await selectAll<ConfigRow>(configType.tableName)
      setRows(data)
      setPageSubtitle(`${data.length}건`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [configType.tableName])

  useEffect(() => {
    void load()
  }, [load])

  // 언마운트 시 예약된 저장을 흘려보내지 않는다
  useEffect(() => {
    const t = timers.current
    return () => {
      for (const k of Object.keys(t)) clearTimeout(t[k])
    }
  }, [])

  const commit = useCallback(
    async (rowId: string, patch: Record<string, unknown>) => {
      const current = rowsRef.current
      const row = current?.find((r) => String(r[pk]) === rowId)
      if (!row) return
      const next = { ...row, ...patch }
      try {
        // 바뀐 필드만 보낸다 — 행 전체를 덮어쓰면 그 사이 다른 곳에서 고친 값이 지워진다.
        const saved = await updateRow<ConfigRow>(configType.tableName, pk, rowId, patch)
        // PK 를 편집했으면 새 값으로 반영한다
        const savedId = saved?.[pk]
        if (savedId !== undefined && String(savedId) !== rowId) {
          setRows((prev) =>
            (prev ?? []).map((r) => (String(r[pk]) === rowId ? { ...next, [pk]: savedId } : r)),
          )
          toast(`ID 변경: ${rowId} → ${String(savedId)}`, 'success')
        } else {
          setSavedCell(rowId)
          setTimeout(() => setSavedCell(null), 800)
          toast('저장됨', 'success')
        }
      } catch (e) {
        toast('저장 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
      }
    },
    [configType.tableName, pk],
  )

  /** 낙관적 반영 + 디바운스 저장. 조건부 셀(VisibleIf)이 즉시 갱신되도록 상태를 먼저 바꾼다. */
  const setField = useCallback(
    (rowId: string, fieldName: string, value: unknown, immediate = false) => {
      const row = rowsRef.current?.find((r) => String(r[pk]) === rowId)
      if (row && row[fieldName] !== value) {
        undoStack.current.push({
          rowId,
          fieldName,
          oldValue: row[fieldName],
          typeName: configType.tableName,
        })
      }
      setRows((prev) =>
        (prev ?? []).map((r) => (String(r[pk]) === rowId ? { ...r, [fieldName]: value } : r)),
      )

      const key = `${rowId}_${fieldName}`
      clearTimeout(timers.current[key])
      if (immediate) {
        void commit(rowId, { [fieldName]: value })
      } else {
        timers.current[key] = setTimeout(
          () => void commit(rowId, { [fieldName]: value }),
          SAVE_DEBOUNCE_MS,
        )
      }
    },
    [commit, configType.tableName],
  )

  /** Ctrl+Z — 바닐라와 동일하게 필드 단위로 되돌린다. */
  const undo = useCallback(async () => {
    const item = undoStack.current.pop()
    if (!item || item.typeName !== configType.tableName) return
    setRows((prev) =>
      (prev ?? []).map((r) =>
        String(r[pk]) === item.rowId ? { ...r, [item.fieldName]: item.oldValue } : r,
      ),
    )
    try {
      await commit(item.rowId, { [item.fieldName]: item.oldValue })
      toast('되돌리기 완료', 'info')
    } catch (e) {
      toast('되돌리기 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    }
  }, [commit, configType.tableName])

  // ── 행 조작 ───────────────────────────────────────────────

  /** 새 행. 바닐라 addRow() 와 동일하게 타입별 기본값을 채우고 문자열 PK 는 prompt 로 받는다. */
  const addRow = useCallback(async () => {
    const draft: ConfigRow = {}
    for (const f of configType.fields) {
      if (f.type === 'string' && f.isPrimaryKey) {
        const id = window.prompt('ID를 입력하세요:', 'new_' + Date.now())
        if (id === null || id.trim() === '') return
        draft[f.name] = id.trim()
      } else if (f.isEnum && f.enumValues && f.enumValues.length > 0) {
        draft[f.name] = f.enumValues[0]
      } else if (f.type === 'string') {
        draft[f.name] = f.isRequired ? 'new' : ''
      } else if (['int', 'long', 'number'].includes(f.type)) {
        draft[f.name] = 0
      } else if (f.type === 'bool') {
        draft[f.name] = false
      } else {
        draft[f.name] = ''
      }
    }
    try {
      const saved = await insertRow<ConfigRow>(configType.tableName, draft)
      setRows((prev) => {
        const next = [saved, ...(prev ?? [])]
        setPageSubtitle(`${next.length}건`)
        return next
      })
      toast('행 추가됨', 'success')
    } catch (e) {
      toast('추가 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    }
  }, [configType])

  const copyRow = useCallback(
    async (rowId: string) => {
      const src = rowsRef.current?.find((r) => String(r[pk]) === rowId)
      if (!src) return
      try {
        const saved = await insertRow<ConfigRow>(configType.tableName, {
          ...src,
          [pk]: String(src[pk]) + '_copy',
        })
        setRows((prev) => {
          const next = [saved, ...(prev ?? [])]
          setPageSubtitle(`${next.length}건`)
          return next
        })
        toast('행 복사됨', 'success')
      } catch (e) {
        toast('복사 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
      }
    },
    [configType.tableName],
  )

  const deleteRow = useCallback(
    async (rowId: string) => {
      if (!rowId) {
        toast(
          'ID가 비어있는 행입니다. Supabase 대시보드에서 직접 삭제하세요.',
          'error',
        )
        return
      }
      try {
        await dbDelete(configType.tableName, pk, rowId)
        setRows((prev) => {
          const next = (prev ?? []).filter((r) => String(r[pk]) !== rowId)
          setPageSubtitle(`${next.length}건`)
          return next
        })
        toast('삭제됨', 'success')
      } catch (e) {
        toast('삭제 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
      }
    },
    [configType.tableName],
  )

  /** 드래그 정렬 결과를 서버에 반영. 실패하면 원본을 다시 읽어 화면을 되돌린다. */
  const reorder = useCallback(
    async (orderedIds: string[]) => {
      const items = orderedIds.map((id, idx) => ({ [pk]: id, sort_order: idx }))
      setRows((prev) => {
        if (!prev) return prev
        const byId = new Map(prev.map((r) => [String(r[pk]), r]))
        const next = orderedIds.map((id) => byId.get(id)).filter((r): r is ConfigRow => !!r)
        return next.length === prev.length ? next : prev
      })
      try {
        // upsert 는 보낸 컬럼만 갱신한다 — sort_order 외의 값은 건드리지 않는다.
        // 서버 _reorder 는 트랜잭션이었지만, 실패하면 다시 로드해 되돌리므로 RPC 까지는 필요 없다.
        await upsertMany(configType.tableName, items, pk)
        toast('순서가 저장되었습니다', 'success')
      } catch {
        toast('순서 저장 실패 — 다시 로드합니다', 'error')
        void load()
      }
    },
    [configType.tableName, load, pk],
  )

  // ── Export ───────────────────────────────────────────────
  // 가져오기는 제거했다 (ADR-0004 결정 9) — 테이블 하나씩 올리는 방식이라 여러 테이블을
  // 같은 시점으로 되돌리지 못해 목적(스냅샷)을 달성할 수 없었다. 스냅샷은 별도 기능으로 만든다.

  /** 서버 `_export` 대신 지금 화면의 데이터를 그대로 파일로 만든다. */
  const exportData = useCallback(async () => {
    try {
      const data = await selectAll<ConfigRow>(configType.tableName)
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${configType.tableName}.json`
      a.click()
      URL.revokeObjectURL(url)
      toast(`${configType.name} ${data.length}건 내보내기 완료`, 'success')
    } catch (e) {
      toast('내보내기 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    }
  }, [configType])

  return {
    rows,
    error,
    savedCell,
    setField,
    undo,
    reload: load,
    addRow,
    copyRow,
    deleteRow,
    reorder,
    exportData,
  }
}
