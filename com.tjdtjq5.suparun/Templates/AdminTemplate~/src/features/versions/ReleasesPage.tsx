import { useCallback, useEffect, useState } from 'react'
import { opsVisible } from '../../shared/bridge'
import { isPreview } from '../../shared/env'
import { ops } from '../../shared/ops'
import { sb } from '../../shared/supabase'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'
import {
  listReleases,
  listVersions,
  type ConfigVersion,
  type Release,
} from '../../shared/versions'
import { fmtDateTime, timeAgo } from '../audit/format'
import { useAdmin } from '../shell/AdminContext'

/**
 * 릴리스 (#51, ADR-0010 결정 5·6) — 무엇이 함께 나갔는가.
 *
 * 목록은 매니페스트(suparun_release)를 그대로 보여주고, 행을 펼치면 오케스트레이션의
 * 단계별 기록(트래픽 전환 → 게시 → logic 게이트)이 나온다. 생성은 로컬 브리지 전용이다 —
 * gcloud 와 PAT 를 쥔 곳이 에디터뿐이라서다.
 */
export function ReleasesPage() {
  const { canWrite } = useAdmin()
  const [releases, setReleases] = useState<Release[] | null>(null)
  const [versions, setVersions] = useState<ConfigVersion[]>([])
  const [error, setError] = useState<string | null>(null)

  // 목록을 다시 읽고 그 결과를 돌려준다 — CreateForm 의 완료 폴링이 running 여부를 본다.
  const load = useCallback(async (): Promise<Release[] | null> => {
    if (isPreview()) {
      setReleases([])
      return []
    }
    try {
      setError(null)
      const [r, v] = await Promise.all([listReleases(), listVersions()])
      setReleases(r)
      setVersions(v)
      return r
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      return null
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>릴리스 목록을 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!releases) return <LoadingBlock label="릴리스 불러오는 중" />

  return (
    <div className="releases-page">
      <p className="text-muted m-3 mb-2">
        릴리스는 <b>무엇이 함께 나갔는가</b>의 기록입니다 — logic version(클라 게이트)·git·config
        버전·서버 리비전이 한 줄에 묶입니다. 실행은 순차이고 단계마다 기록이 남습니다.
      </p>

      {canWrite && opsVisible() && <CreateForm versions={versions} onDone={load} />}

      {releases.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-rocket" />
          <h3>릴리스가 없습니다</h3>
          <p>위 폼(로컬 어드민)에서 첫 릴리스를 만드세요.</p>
        </div>
      ) : (
        <div className="m-2">
          {releases.map((r) => (
            <ReleaseRow key={r.id} r={r} />
          ))}
        </div>
      )}
    </div>
  )
}

function ReleaseRow({ r }: { r: Release }) {
  const badge =
    r.status === 'done' ? 'bg-green' : r.status === 'failed' ? 'bg-red' : 'bg-yellow'
  return (
    <details className="compare-table">
      <summary>
        <span className={`badge ${badge} me-2`}>{r.status}</span>
        <b>logic {r.logic_min === r.logic_version ? r.logic_version : `${r.logic_min}~${r.logic_version}`}</b>
        {' · '}
        <code>{r.content_hash?.slice(0, 12) ?? '?'}</code>
        {r.revision_tag && (
          <>
            {' · '}
            <code>{r.revision_tag}</code>
          </>
        )}
        {r.memo && <span className="text-muted ms-2">{r.memo}</span>}
        <span className="text-muted ms-2">{timeAgo(r.created_at)}</span>
      </summary>
      <table className="table table-sm mt-2 mb-1" style={{ maxWidth: 640 }}>
        <tbody>
          <tr>
            <td className="text-muted" style={{ width: 120 }}>git</td>
            <td><code>{r.git_sha ? r.git_sha.slice(0, 7) : '-'}</code></td>
          </tr>
          <tr>
            <td className="text-muted">게시</td>
            <td>{r.published_at ? fmtDateTime(r.published_at) : '-'}</td>
          </tr>
          <tr>
            <td className="text-muted">행위자</td>
            <td>{r.published_by ?? r.created_by ?? '-'}</td>
          </tr>
        </tbody>
      </table>
      <ul className="audit-mini-list">
        {r.steps.map((s, i) => (
          <li key={i}>
            <i className={`ti ${s.ok ? 'ti-check text-green' : 'ti-x text-red'}`} /> {s.step}
            {s.detail && <span className="text-muted"> — {s.detail}</span>}
          </li>
        ))}
        {r.steps.length === 0 && <li className="text-muted">단계 기록이 아직 없습니다.</li>}
      </ul>
    </details>
  )
}

/** 릴리스 생성 — 로컬 브리지 전용. 완료는 목록의 status 가 말한다(running 이 끝날 때까지 폴링). */
function CreateForm({ versions, onDone }: { versions: ConfigVersion[]; onDone: () => Promise<Release[] | null> }) {
  const [schema, setSchema] = useState('')
  const [logicVersion, setLogicVersion] = useState(1)
  const [logicMin, setLogicMin] = useState(1)
  const [memo, setMemo] = useState('')
  const [tag, setTag] = useState('')
  const [busy, setBusy] = useState(false)

  async function run() {
    if (!schema) return toast('대상 버전을 고르세요.', 'error')
    setBusy(true)
    try {
      // 행위자 = 어드민 로그인 이메일 — 매니페스트의 created_by/published_by 가 된다.
      const session = sb ? (await sb.auth.getSession()).data.session : null
      const actor = session?.user?.email ?? ''
      await ops.createRelease({ logicVersion, logicMin, versionSchema: schema, memo, revisionTag: tag, actor })
      toast('릴리스를 시작했습니다 — 단계 기록은 목록에서 봅니다.')
      // 완료 감지는 매니페스트 폴링 — 트래픽 전환(gcloud)은 수십 초를 먹을 수 있어
      // 1회 재조회로는 running 으로 굳어 보인다. running 이 사라지거나 90초면 멈춘다.
      for (let i = 0; i < 30; i++) {
        await new Promise((r) => setTimeout(r, 3000))
        const list = await onDone()
        if (list && !list.some((x) => x.status === 'running')) break
      }
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="audit-search m-3 mt-0">
      <div className="row g-2 align-items-end">
        <div className="col-sm-4">
          <label className="form-label mb-1">config 버전</label>
          <select className="form-select form-select-sm" value={schema} onChange={(e) => setSchema(e.target.value)}>
            <option value="">선택…</option>
            {versions.map((v) => (
              <option key={v.schema_name} value={v.schema_name}>
                {v.content_hash?.slice(0, 12)} · {v.label}
              </option>
            ))}
          </select>
        </div>
        <div className="col-sm-2">
          <label className="form-label mb-1">logic version</label>
          <input
            type="number"
            className="form-control form-control-sm"
            min={1}
            value={logicVersion}
            onChange={(e) => setLogicVersion(Number(e.target.value))}
          />
        </div>
        <div className="col-sm-2">
          <label className="form-label mb-1">허용 최소</label>
          <input
            type="number"
            className="form-control form-control-sm"
            min={1}
            value={logicMin}
            onChange={(e) => setLogicMin(Number(e.target.value))}
          />
        </div>
        <div className="col-sm-2">
          <label className="form-label mb-1">리비전 태그 (선택)</label>
          <input
            type="text"
            className="form-control form-control-sm"
            placeholder="rel-abc1234"
            value={tag}
            onChange={(e) => setTag(e.target.value)}
          />
        </div>
        <div className="col-sm-2">
          <button className="btn btn-primary btn-sm w-100" disabled={busy} onClick={() => void run()}>
            {busy ? <Spinner size={12} /> : <i className="ti ti-rocket me-1" />}
            릴리스
          </button>
        </div>
      </div>
      <div className="mt-2">
        <input
          type="text"
          className="form-control form-control-sm"
          placeholder="메모 — 이 릴리스로 무엇이 나가는가"
          value={memo}
          onChange={(e) => setMemo(e.target.value)}
        />
      </div>
    </div>
  )
}
