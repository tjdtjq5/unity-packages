import { env } from '../../shared/env'
import type { ToolbarActions } from './AdminContext'

/**
 * 페이지 제목 + Supabase 링크 + 부제 + 툴바(검색/내보내기/가져오기/추가).
 * 바닐라 page-header HTML 과 showToolbar / hideToolbar / updateSupabaseLink 를 대체한다.
 *
 * 툴바는 Config 화면에서만 보인다. 동작은 화면 쪽이 알고 있어서 `actions` 로 받는다.
 */
export function PageHeader({
  title,
  subtitle,
  supabaseTable,
  search,
  onSearch,
  actions,
}: {
  title: string
  subtitle: string
  /** Supabase Table Editor 링크 대상. null 이면 링크를 숨긴다. */
  supabaseTable: string | null
  search: string
  onSearch: (q: string) => void
  /** null 이면 툴바 전체를 숨긴다(Config 이외 화면). */
  actions: ToolbarActions | null
}) {
  const ref = env().projectRef
  const supabaseUrl =
    ref && supabaseTable
      ? `https://supabase.com/dashboard/project/${ref}/editor/${encodeURIComponent(supabaseTable)}`
      : null

  return (
    <div className="page-header d-print-none">
      <div className="container-xl">
        <div className="row align-items-center">
          <div className="col-auto d-flex align-items-center gap-2">
            <h2 id="page-title" className="page-title mb-0">
              {title}
            </h2>
            {supabaseUrl && (
              <a
                href={supabaseUrl}
                target="_blank"
                rel="noreferrer"
                className="btn btn-ghost-secondary btn-icon"
                title="Supabase Table Editor에서 열기"
              >
                <i className="ti ti-external-link" style={{ fontSize: '1.1rem' }} />
              </a>
            )}
            <div className="text-muted small">{subtitle}</div>
          </div>
          {actions && (
            <div className="col-auto ms-auto">
              <div className="d-flex gap-2">
                {/* id 는 `/` 단축키가 포커스를 잡는 데 쓴다 */}
                <div className="input-icon">
                  <span className="input-icon-addon">
                    <i className="ti ti-search" />
                  </span>
                  <input
                    id="search-input"
                    type="text"
                    className="form-control"
                    placeholder="검색..."
                    style={{ width: 200 }}
                    value={search}
                    onChange={(e) => onSearch(e.target.value)}
                  />
                </div>
                <button
                  className="btn btn-outline-secondary"
                  onClick={() => void actions.exportData()}
                >
                  <i className="ti ti-download me-1" />
                  내보내기
                </button>
                <button className="btn btn-primary" onClick={() => void actions.addRow()}>
                  <i className="ti ti-plus me-1" />
                  추가
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
