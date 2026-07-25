import { useCallback, useEffect, useState } from 'react'
import { deleteRow, selectAll, updateRow } from '../../shared/db'
import { toast } from '../../shared/toast'
import type { AdminUser } from '../../shared/types'

/**
 * 관리자 목록과 조작. 서버 `/admin/api/admins` 대신 admin_user 를 직접 다룬다 (ADR-0004).
 *
 * 정책은 `admin_all`(is_admin) + `self_read`(본인 행). 즉 관리자는 전부 보고 고칠 수 있고,
 * 승인 대기 중인 사람은 자기 행만 보인다 — "내가 대기 중"임을 화면에서 알 수 있어야 하기 때문이다.
 *
 * 첫 가입자를 자동으로 admin 으로 만드는 처리는 여전히 서버 미들웨어에 있다.
 * 그건 로그인 시점에 service_role 로 도는 흐름이라 이 화면과 무관하다.
 */
export function useAdmins() {
  const [admins, setAdmins] = useState<AdminUser[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setError(null)
      // admin_user 에는 sort_order 가 없다 — 등록순으로 본다.
      setAdmins(await selectAll<AdminUser>('admin_user', { orderBy: 'created_at' }))
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
        await updateRow('admin_user', 'id', id, { role })
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
      if (!window.confirm('이 관리자를 삭제하시겠습니까?')) return
      try {
        await deleteRow('admin_user', 'id', id)
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
