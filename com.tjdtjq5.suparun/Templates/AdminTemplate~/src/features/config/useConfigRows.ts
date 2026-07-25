import { useCallback, useEffect, useRef, useState } from 'react'
import { configApi } from '../../shared/api'
import { toast } from '../../shared/toast'
import { authFetch } from '../../shared/api'
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
      const data = await configApi<ConfigRow[]>(`/${configType.tableName}`)
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
      const row = current?.find((r) => String(r.id) === rowId)
      if (!row) return
      const next = { ...row, ...patch }
      try {
        const saved = await configApi<ConfigRow | null>(
          `/${configType.tableName}/${encodeURIComponent(rowId)}`,
          'PUT',
          next,
        )
        // 서버가 id 를 바꿔 돌려주면(PK 편집) 그 값으로 반영한다
        const savedId = saved?.id
        if (savedId !== undefined && String(savedId) !== rowId) {
          setRows((prev) =>
            (prev ?? []).map((r) => (String(r.id) === rowId ? { ...next, id: savedId } : r)),
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
    [configType.tableName],
  )

  /** 낙관적 반영 + 디바운스 저장. 조건부 셀(VisibleIf)이 즉시 갱신되도록 상태를 먼저 바꾼다. */
  const setField = useCallback(
    (rowId: string, fieldName: string, value: unknown, immediate = false) => {
      const row = rowsRef.current?.find((r) => String(r.id) === rowId)
      if (row && row[fieldName] !== value) {
        undoStack.current.push({
          rowId,
          fieldName,
          oldValue: row[fieldName],
          typeName: configType.tableName,
        })
      }
      setRows((prev) =>
        (prev ?? []).map((r) => (String(r.id) === rowId ? { ...r, [fieldName]: value } : r)),
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
        String(r.id) === item.rowId ? { ...r, [item.fieldName]: item.oldValue } : r,
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
      const saved = await configApi<ConfigRow>(`/${configType.tableName}`, 'POST', draft)
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
      const src = rowsRef.current?.find((r) => String(r.id) === rowId)
      if (!src) return
      try {
        const saved = await configApi<ConfigRow>(`/${configType.tableName}`, 'POST', {
          ...src,
          id: String(src.id) + '_copy',
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
        await configApi(`/${configType.tableName}/${encodeURIComponent(rowId)}`, 'DELETE')
        setRows((prev) => {
          const next = (prev ?? []).filter((r) => String(r.id) !== rowId)
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
      const items = orderedIds.map((id, idx) => ({ id, sort_order: idx }))
      setRows((prev) => {
        if (!prev) return prev
        const byId = new Map(prev.map((r) => [String(r.id), r]))
        const next = orderedIds.map((id) => byId.get(id)).filter((r): r is ConfigRow => !!r)
        return next.length === prev.length ? next : prev
      })
      try {
        await configApi(`/_reorder/${configType.tableName}`, 'POST', { items })
        toast('순서가 저장되었습니다', 'success')
      } catch {
        toast('순서 저장 실패 — 다시 로드합니다', 'error')
        void load()
      }
    },
    [configType.tableName, load],
  )

  // ── Export / Import ──────────────────────────────────────

  const exportData = useCallback(async () => {
    try {
      const res = await authFetch(
        `/admin/api/config/_export/${configType.tableName}`,
      )
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const url = URL.createObjectURL(await res.blob())
      const a = document.createElement('a')
      a.href = url
      a.download = `${configType.tableName}.json`
      a.click()
      URL.revokeObjectURL(url)
      toast(`${configType.name} 내보내기 완료`, 'success')
    } catch (e) {
      toast('내보내기 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    }
  }, [configType])

  const importData = useCallback(
    async (file: File) => {
      let data: unknown
      try {
        data = JSON.parse(await file.text())
      } catch {
        toast('유효한 JSON 파일이 아닙니다', 'error')
        return
      }
      if (!Array.isArray(data)) {
        toast('JSON 배열이어야 합니다', 'error')
        return
      }
      if (
        !window.confirm(
          `${configType.name}의 기존 데이터를 ${data.length}건으로 교체합니다. 계속?`,
        )
      )
        return
      try {
        const res = await configApi<{ imported: number }>(
          `/_import/${configType.tableName}`,
          'POST',
          data,
        )
        toast(`${res.imported}건 가져오기 완료`, 'success')
        await load()
      } catch (e) {
        toast('가져오기 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
      }
    },
    [configType, load],
  )

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
    importData,
  }
}
