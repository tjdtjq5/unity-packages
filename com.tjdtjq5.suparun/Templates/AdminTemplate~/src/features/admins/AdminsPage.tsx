import { useEffect, useRef } from 'react'
import { enableColResize } from '../../shared/colResize'
import { LoadingBlock } from '../../shared/Spinner'
import { useAdmins } from './useAdmins'

/**
 * 관리자 관리 화면. 바닐라 showAdmins() 의 콘텐츠 부분을 그대로 옮긴 것이다.
 *
 * 껍데기(page-title, 사이드바 active, hideToolbar, setViewHash)는 바닐라가 계속 담당한다.
 * Tabler 클래스는 그대로 유지한다 — 이행 중 룩이 바뀌면 "예전과 같은가"를 판단할 수 없다.
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

  return (
    <div ref={hostRef}>
      <table className="table table-vcenter card-table table-striped">
        <thead>
          <tr>
            <th>이메일</th>
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
                {a.email}
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
          직접 회원가입한 유저는 &quot;승인 대기&quot; 상태로 등록됩니다. 위에서 승인하세요.
        </div>
      </div>
    </div>
  )
}
