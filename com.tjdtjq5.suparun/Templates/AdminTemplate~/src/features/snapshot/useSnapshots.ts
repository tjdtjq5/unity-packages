import { useCallback, useEffect, useState } from 'react'
import {
  createSnapshot,
  deleteSnapshot,
  loadSnapshots,
  patchSnapshot,
  restoreSnapshot,
  type Snapshot,
} from '../../shared/snapshot'
import { toast } from '../../shared/toast'

/**
 * 스냅샷 목록과 조작.
 *
 * 핀·라벨·코멘트는 **낙관적으로 먼저 반영**한다 — 표 한 줄 UPDATE 라 실패가 드물고,
 * 핀을 누를 때마다 전체를 다시 읽으면 목록이 깜빡인다. 실패하면 되돌리고 토스트로 알린다.
 * 반면 찍기·복원·삭제는 DB 구조가 바뀌므로 끝난 뒤 다시 읽는다.
 */
export function useSnapshots() {
  const [snapshots, setSnapshots] = useState<Snapshot[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  /** 진행 중인 무거운 작업. 버튼을 잠그고 스피너를 띄우는 데 쓴다. */
  const [busy, setBusy] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setError(null)
      setSnapshots(await loadSnapshots())
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  const create = useCallback(
    async (label: string, comment?: string) => {
      setBusy('create')
      try {
        await createSnapshot(label, comment)
        await reload()
        toast(`스냅샷 "${label}" 저장됨`, 'success')
      } catch (e) {
        toast(e instanceof Error ? e.message : String(e), 'error')
      } finally {
        setBusy(null)
      }
    },
    [reload],
  )

  /** 성공하면 되돌아올 자리(직전 상태 자동 스냅샷)의 이름을 반환한다. */
  const restore = useCallback(
    async (schemaName: string): Promise<string | null> => {
      setBusy(schemaName)
      try {
        const backup = await restoreSnapshot(schemaName)
        await reload()
        return backup
      } catch (e) {
        toast(e instanceof Error ? e.message : String(e), 'error')
        return null
      } finally {
        setBusy(null)
      }
    },
    [reload],
  )

  const remove = useCallback(
    async (schemaName: string) => {
      setBusy(schemaName)
      try {
        await deleteSnapshot(schemaName)
        await reload()
        toast('스냅샷 삭제됨', 'success')
      } catch (e) {
        toast(e instanceof Error ? e.message : String(e), 'error')
      } finally {
        setBusy(null)
      }
    },
    [reload],
  )

  const patch = useCallback(
    async (schemaName: string, p: Partial<Pick<Snapshot, 'label' | 'comment' | 'pinned'>>) => {
      const before = snapshots
      setSnapshots((list) =>
        list ? list.map((s) => (s.schema_name === schemaName ? { ...s, ...p } : s)) : list,
      )
      try {
        await patchSnapshot(schemaName, p)
      } catch (e) {
        setSnapshots(before) // 낙관적 반영을 되돌린다
        toast(e instanceof Error ? e.message : String(e), 'error')
      }
    },
    [snapshots],
  )

  return { snapshots, error, busy, create, restore, remove, patch, reload }
}
