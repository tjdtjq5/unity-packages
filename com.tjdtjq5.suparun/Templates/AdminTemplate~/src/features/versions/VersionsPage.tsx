import { useCallback, useEffect, useState } from 'react'
import { isPreview } from '../../shared/env'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import {
  activeVersion,
  listVersions,
  publishVersion,
  type ActiveVersion,
  type ConfigVersion,
} from '../../shared/versions'
import { AuditCard } from '../audit/AuditCard'
import { timeAgo } from '../audit/format'
import { useAdmin } from '../shell/AdminContext'

/**
 * Game Configs — 버전 목록·게시 (#30·#31·#34, Metaplay Manage Game Configs 동형: 60-game-configs.png).
 *
 * 업로드는 여기 없다 — dev 어드민의 ops(브리지, PAT 필요)가 이 환경에 미게시 버전을 만든다.
 * 이 화면은 그 버전들을 검토(비교)하고 **게시**한다. 롤백도 같은 버튼이다: 과거 버전을
 * 다시 게시하면 된다(#34). 이력은 지워지지 않고 감사 카드가 게시 역사를 말한다.
 */
export function VersionsPage() {
  const { navigate, canWrite } = useAdmin()
  const [versions, setVersions] = useState<ConfigVersion[] | null>(null)
  const [active, setActive] = useState<ActiveVersion | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (isPreview()) {
      setVersions([])
      return
    }
    try {
      setError(null)
      const [v, a] = await Promise.all([listVersions(), activeVersion()])
      setVersions(v)
      setActive(a)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function publish(v: ConfigVersion) {
    if (
      !window.confirm(
        `버전 ${short(v.content_hash)} (${v.label}) 을 이 환경의 라이브에 게시합니다.\n\n` +
          '현재 활성 데이터는 자동 백업 스냅샷으로 남고, 클라 조회가 이 버전으로 바뀝니다.',
      )
    )
      return
    setBusy(v.schema_name)
    try {
      await publishVersion(v.schema_name)
      toast(`게시 완료 — ${short(v.content_hash)}`)
      await load()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(null)
    }
  }

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>버전 목록을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!versions) return <LoadingBlock label="버전 목록 불러오는 중" />

  return (
    <div className="versions-page">
      <p className="text-muted m-3 mb-2">
        게임 데이터의 버전입니다. dev 에서 업로드하면 여기 <b>미게시 버전</b>이 생기고, 검토(비교) 후
        게시해야 라이브에 반영됩니다. 과거 버전을 다시 게시하면 롤백입니다.
      </p>

      <div className="m-2">
        {/* key — 게시로 활성이 바뀌면 카드를 리마운트해 방금의 publish 가 이력에 보이게 한다. */}
        <AuditCard key={active?.content_hash ?? ''} configType="suparun_config_version" />
      </div>

      {versions.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-versions" />
          <h3>버전이 없습니다</h3>
          <p>
            dev 는 컴파일 즉시 반영 모델이라 버전을 쓰지 않습니다.
            <br />
            버전은 dev 어드민의 ops &gt; 승격 &gt; [버전 업로드] 가 대상 환경에 만듭니다.
          </p>
        </div>
      ) : (
        <table className="table table-vcenter card-table table-striped">
          <thead>
            <tr>
              <th>버전</th>
              <th>라벨</th>
              <th>git</th>
              <th>업로드</th>
              <th>마지막 게시</th>
              <th style={{ width: 220 }} />
            </tr>
          </thead>
          <tbody>
            {versions.map((v) => {
              const isActive = !!active && active.content_hash === v.content_hash
              return (
                <tr key={v.schema_name}>
                  <td>
                    <code>{short(v.content_hash)}</code>{' '}
                    {isActive && <span className="badge bg-green ms-1">Active</span>}
                  </td>
                  <td>{v.label}</td>
                  <td className="text-muted">
                    <code>{v.git_sha ? v.git_sha.slice(0, 7) : '-'}</code>
                  </td>
                  <td className="text-muted">{timeAgo(v.created_at)}</td>
                  <td className="text-muted">
                    {v.published_at ? timeAgo(v.published_at) : <span className="badge bg-yellow-lt">미게시</span>}
                  </td>
                  <td>
                    <div className="btn-list flex-nowrap">
                      <button
                        className="btn btn-sm"
                        onClick={() =>
                          navigate({ kind: 'compare', base: 'public', next: v.schema_name })
                        }
                      >
                        <i className="ti ti-git-compare me-1" />
                        활성과 비교
                      </button>
                      {canWrite && (
                        <button
                          className="btn btn-primary btn-sm"
                          disabled={busy !== null || isActive}
                          title={isActive ? '이미 활성 버전입니다' : v.published_at ? '재게시(롤백)' : '게시'}
                          onClick={() => void publish(v)}
                        >
                          {busy === v.schema_name ? (
                            <Spinner size={12} />
                          ) : (
                            <i className="ti ti-rocket me-1" />
                          )}
                          {v.published_at && !isActive ? '재게시 (롤백)' : '게시'}
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      {versions.length > 1 && (
        <div className="m-3">
          <button className="btn btn-sm" onClick={() => navigate({ kind: 'compare' })}>
            <i className="ti ti-git-compare me-1" />
            임의 두 버전 비교
          </button>
        </div>
      )}
    </div>
  )
}

function short(hash: string | null): string {
  return hash ? hash.slice(0, 12) : '?'
}
