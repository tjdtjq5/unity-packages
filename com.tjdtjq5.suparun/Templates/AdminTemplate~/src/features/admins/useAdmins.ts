import { useCallback, useEffect, useState } from 'react'
import { adminApi } from '../../shared/api'
import { toast } from '../../shared/toast'
import type { AdminUser } from '../../shared/types'

/**
 * 관리자 목록 데이터와 조작. 바닐라 showAdmins/changeRole/removeAdmin 을 대체한다.
 *
 * 바닐라는 조작 후 showAdmins() 를 다시 불러 화면 전체를 재조립했다.
 * 여기서는 목록만 다시 받아오면 React 가 바뀐 행만 갱신한다.
 */
export function useAdmins() {
  const [admins, setAdmins] = useState<AdminUser[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setError(null)
      setAdmins(await adminApi<AdminUser[]>(''))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  const changeRole = useCallback(
    async (id: string, role: AdminUser['role']) => {
      try {
        await adminApi(`/${id}/role`, 'PUT', { role })
        await reload()
        toast(role === 'admin' ? '승인 완료' : '권한 해제됨', 'info')
      } catch (e) {
        toast(e instanceof Error ? e.message : String(e), 'error')
      }
    },
    [reload],
  )

  const remove = useCallback(
    async (id: string) => {
      // 바닐라와 동일하게 브라우저 confirm 을 쓴다. 모달 통일은 껍데기 이관(5단계) 때 함께.
      if (!window.confirm('이 관리자를 삭제하시겠습니까?')) return
      try {
        await adminApi(`/${id}`, 'DELETE')
        await reload()
        toast('관리자 삭제됨', 'success')
      } catch (e) {
        toast(e instanceof Error ? e.message : String(e), 'error')
      }
    },
    [reload],
  )

  return { admins, error, changeRole, remove }
}
