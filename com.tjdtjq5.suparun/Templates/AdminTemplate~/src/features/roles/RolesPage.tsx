import { useCallback, useEffect, useState } from 'react'
import { LoadingBlock } from '../../shared/Spinner'
import { sb } from '../../shared/supabase'
import { toast } from '../../shared/toast'
import { BUILTIN_ROLES, type AdminUser, type AdminUserRole } from '../../shared/types'
import { useAdmin } from '../shell/AdminContext'

/**
 * User Roles — 롤 부여/회수 (#24, ADR-0009 결정 4).
 *
 * 명단은 admin_user(신원), 롤은 admin_user_role(매핑·합집합)이다. 명단에는 로그인해 본
 * 사람만 올라온다 — 로컬 로그인은 claim 이, 그 외에는 self-register 가 행을 만든다.
 * 이메일로 미리 초대하는 기능은 없다: auth 계정이 생기기 전에는 부여할 user_id 가 없다.
 *
 * 화면·쓰기 전부 game-admin 전용이다(사이드바 게이트 + admin_user/admin_user_role RLS).
 */
export function RolesPage() {
  const { setPageSubtitle } = useAdmin()
  const [users, setUsers] = useState<AdminUser[] | null>(null)
  const [roleRows, setRoleRows] = useState<AdminUserRole[]>([])
  const [error, setError] = useState<string | null>(null)
  /** 진행 중인 토글 — `${userId}:${role}`. 왕복 동안 그 칸만 잠근다. */
  const [busy, setBusy] = useState<string | null>(null)
  /** 내 uid — 자기 자신의 game-admin 회수를 막는 데 쓴다. */
  const [myUid, setMyUid] = useState('')

  const load = useCallback(async () => {
    if (!sb) return
    const [u, r] = await Promise.all([
      sb.from('admin_user').select<AdminUser[]>('*').order('created_at', { ascending: true }),
      sb.from('admin_user_role').select<AdminUserRole[]>('*'),
    ])
    if (u.error || r.error) {
      setError((u.error ?? r.error)?.message ?? '불러오지 못했습니다')
      return
    }
    setUsers(u.data ?? [])
    setRoleRows(r.data ?? [])
  }, [])

  useEffect(() => {
    void load()
    void sb?.auth.getSession().then(({ data }) => setMyUid(data.session?.user?.id ?? ''))
  }, [load])

  useEffect(() => {
    if (users) setPageSubtitle(`${users.length}명`)
  }, [users, setPageSubtitle])

  async function toggle(user: AdminUser, role: string, has: boolean) {
    if (!sb || !user.user_id) return

    // 마지막 문을 자기 손으로 잠그는 것만 막는다 — 다른 game-admin 이 회수하는 것은 정상 경로다.
    if (has && role === 'game-admin' && user.user_id === myUid) {
      toast('자기 자신의 game-admin 은 회수할 수 없습니다. 다른 game-admin 에게 요청하세요.', 'error')
      return
    }

    const key = `${user.user_id}:${role}`
    setBusy(key)
    try {
      const r = has
        ? await sb.from('admin_user_role').delete().eq('user_id', user.user_id).eq('role', role)
        : await sb.from('admin_user_role').insert({
            user_id: user.user_id,
            role,
            granted_at: Date.now(),
            granted_by: myUid,
          })
      if (r.error) {
        toast(r.error.message, 'error')
        return
      }
      toast(`${user.email ?? user.user_id} — ${role} ${has ? '회수' : '부여'}`)
      await load()
    } finally {
      setBusy(null)
    }
  }

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!users) return <LoadingBlock label="명단 불러오는 중" />

  if (users.length === 0) {
    return (
      <div className="empty-state">
        <i className="ti ti-users" />
        <h3>등록된 사용자가 없습니다</h3>
        <p>로그인한 적이 있는 계정만 여기 올라옵니다.</p>
      </div>
    )
  }

  const rolesOf = (uid: string | null) => roleRows.filter((r) => r.user_id === uid).map((r) => r.role)

  return (
    <table className="table table-vcenter card-table table-striped">
      <thead>
        <tr>
          <th>email</th>
          <th>provider</th>
          {BUILTIN_ROLES.map((r) => (
            <th key={r} style={{ width: 110, textAlign: 'center' }}>
              {r}
            </th>
          ))}
          <th>등록</th>
        </tr>
      </thead>
      <tbody>
        {users.map((u) => {
          const mine = rolesOf(u.user_id)
          const isMe = !!u.user_id && u.user_id === myUid
          return (
            <tr key={u.id}>
              <td>
                {u.email || <span className="text-muted">(이메일 없음)</span>}
                {isMe && <span className="badge bg-cyan-lt ms-2">me</span>}
                {mine.length === 0 && <span className="badge bg-yellow-lt ms-2">대기</span>}
              </td>
              <td className="text-muted">{u.provider ?? ''}</td>
              {BUILTIN_ROLES.map((role) => {
                const has = mine.includes(role)
                const key = `${u.user_id}:${role}`
                return (
                  <td key={role} style={{ textAlign: 'center' }}>
                    <label className="form-check form-switch d-inline-block m-0">
                      <input
                        className="form-check-input"
                        type="checkbox"
                        checked={has}
                        disabled={busy === key || !u.user_id}
                        onChange={() => void toggle(u, role, has)}
                      />
                    </label>
                  </td>
                )
              })}
              <td className="text-muted">
                {new Date(u.created_at).toLocaleDateString()} / {u.created_by ?? ''}
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}
