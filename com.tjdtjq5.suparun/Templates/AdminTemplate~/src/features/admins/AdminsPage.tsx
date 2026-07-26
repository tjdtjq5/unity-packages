import { useEffect, useRef } from 'react'
import { enableColResize } from '../../shared/colResize'
import { LoadingBlock } from '../../shared/Spinner'
import { useAdmins } from './useAdmins'

/** 프로바이더 표시 이름. 한 계정에 여러 신원이 묶이면 `email+github` 처럼 온다. */
const PROVIDER_LABEL: Record<string, string> = {
  email: '이메일',
  google: 'Google',
  github: 'GitHub',
  discord: 'Discord',
  kakao: 'Kakao',
  apple: 'Apple',
}

/**
 * 관리자 관리 화면.
 *
 * **계정 단위로 승인한다.** 같은 이메일이라도 프로바이더가 다르면 Supabase 에서 다른
 * 사용자이고(자동으로 묶일 때도 있고 아닐 때도 있다 — 우리가 정하는 규칙이 아니다),
 * RLS 의 `is_admin()` 도 uid 로 판정한다. 그래서 각각 승인해야 실제로 동작한다.
 *
 * 그 사실이 화면에 드러나야 한다. 안 그러면 같은 이메일이 두 줄 떠 있는데 왜 하나만
 * 관리자인지 알 수 없다.
 */
export function AdminsPage() {
  const { admins, error, changeRole, remove } = useAdmins()
  const hostRef = useRef<HTMLDivElement>(null)

  // 바닐라 컬럼 너비 조정을 표가 그려진 뒤 붙인다 (기존 동작 유지).
  // enableColResize 는 container.querySelector('table') 을 찾으므로 이 div 를 넘기면 된다.
  useEffect(() => {
    if (!admins || !hostRef.current) return
    enableColResize(hostRef.current, 'admins', {
      fields: [
        { name: 'email', type: 'string' },
        { name: 'provider', type: 'string', isEnum: true },
        { name: 'role', type: 'string', isEnum: true },
        { name: 'created_at', type: 'string' },
        null,
      ],
      data: admins,
    })
  }, [admins])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>관리자 목록을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!admins) {
    return (
      <LoadingBlock label="관리자 목록 불러오는 중" />
    )
  }

  // 같은 이메일이 여러 줄인 경우를 표시하기 위해 미리 센다.
  const emailCount = new Map<string, number>()
  for (const a of admins) {
    const k = (a.email ?? '').toLowerCase()
    if (k) emailCount.set(k, (emailCount.get(k) ?? 0) + 1)
  }

  return (
    <div ref={hostRef}>
      <table className="table table-vcenter card-table table-striped">
        <thead>
          <tr>
            <th>이메일</th>
            <th>로그인 수단</th>
            <th>상태</th>
            <th>등록일</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {admins.map((a) => (
            <tr key={a.id}>
              <td>
                <i className="ti ti-mail text-muted me-1" />
                {a.email || <span className="text-muted">(이메일 없음)</span>}
                {emailCount.get((a.email ?? '').toLowerCase())! > 1 && (
                  <span className="badge bg-blue-lt ms-2" title="같은 이메일이지만 다른 계정입니다. 각각 승인해야 합니다.">
                    같은 이메일 · 다른 계정
                  </span>
                )}
              </td>
              <td className="text-muted">
                {a.provider
                  ? a.provider.split('+').map((x) => PROVIDER_LABEL[x] ?? x).join(' · ')
                  : '—'}
              </td>
              <td>
                {a.role === 'admin' ? (
                  <span className="badge bg-green">
                    <i className="ti ti-check me-1" />
                    관리자
                  </span>
                ) : (
                  <span className="badge bg-yellow">
                    <i className="ti ti-clock me-1" />
                    승인 대기
                  </span>
                )}
              </td>
              <td className="text-muted">{new Date(a.created_at).toLocaleDateString('ko-KR')}</td>
              <td>
                <div className="btn-list flex-nowrap">
                  {a.role === 'pending' ? (
                    <button
                      className="btn btn-ghost-success btn-sm"
                      onClick={() => void changeRole(a.id, 'admin')}
                    >
                      <i className="ti ti-check me-1" />
                      승인
                    </button>
                  ) : (
                    <button
                      className="btn btn-ghost-warning btn-sm"
                      onClick={() => void changeRole(a.id, 'pending')}
                    >
                      <i className="ti ti-lock me-1" />
                      해제
                    </button>
                  )}
                  <button
                    className="btn btn-ghost-danger btn-icon btn-sm"
                    title="삭제"
                    onClick={() => void remove(a.id)}
                  >
                    <i className="ti ti-trash" />
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="p-3 border-top">
        <div className="text-muted small mb-2">
          로그인한 계정은 &quot;승인 대기&quot; 로 등록됩니다. 위에서 승인하세요.
          <br />
          같은 이메일이라도 <strong>로그인 수단이 다르면 다른 계정</strong>입니다 — 각각 승인해야 합니다.
        </div>
      </div>
    </div>
  )
}
