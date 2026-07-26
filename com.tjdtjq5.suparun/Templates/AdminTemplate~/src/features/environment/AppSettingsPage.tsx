import { useCallback, useEffect, useState } from 'react'
import { deleteProject, listProjects, type SupabaseProject } from '../../shared/projects'
import { AuthProvidersBlock } from './AuthProvidersBlock'
import {
  loadEnvRoles,
  saveEnvRoles,
  setExclusive,
  type EnvRoleMap,
} from '../../shared/envRoles'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { toast } from '../../shared/toast'

/**
 * 앱 설정 — 환경 역할과 위험 영역.
 *
 * **되돌릴 수 없는 조작을 여기 모은다.** 카드 목록은 진입점이어야 하고, 거기에 삭제가 섞여 있으면
 * 들어가려다 지우는 사고가 난다.
 *
 * 역할 정의는 `suparun_meta.env_roles` 에 저장되고 **Unity 가 읽는다**.
 */
export function AppSettingsPage() {
  const [projects, setProjects] = useState<SupabaseProject[] | null>(null)
  const [online, setOnline] = useState<boolean | null>(null)
  const [roles, setRoles] = useState<EnvRoleMap | null>(null)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setRoles(await loadEnvRoles())
    try {
      setProjects(await listProjects())
      setOnline(true)
    } catch {
      setProjects(null)
      setOnline(false)
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  function patch(ref: string, v: Partial<{ name: string }>) {
    setRoles((r) => ({ ...(r ?? {}), [ref]: { ...(r?.[ref] ?? { name: '' }), ...v } }))
    setDirty(true)
  }

  function toggle(ref: string, field: 'editor' | 'build', on: boolean) {
    setRoles((r) => setExclusive(r ?? {}, ref, field, on))
    setDirty(true)
  }

  /** 지금 그 역할을 맡고 있는 project_ref. 없으면 빈 문자열 — select 가 첫 항목을 보여준다. */
  function pick(map: EnvRoleMap | null, field: 'editor' | 'build'): string {
    if (!map) return ''
    return Object.keys(map).find((k) => map[k]?.[field]) ?? ''
  }

  async function save() {
    if (!roles) return
    setSaving(true)
    try {
      await saveEnvRoles(roles)
      setDirty(false)
      toast('환경 설정 저장됨 — Unity 가 어드민을 열 때 반영합니다', 'success')
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setSaving(false)
    }
  }

  async function remove(p: SupabaseProject) {
    if (!window.confirm(`'${p.name}' 을 삭제합니다.\n데이터·백업이 함께 사라지며 되돌릴 수 없습니다.`))
      return
    const typed = window.prompt(`확인을 위해 '${p.name}' 을 입력하세요`, '')
    if (typed === null) return
    if (typed.trim() !== p.name) {
      toast('이름이 일치하지 않아 취소했습니다', 'info')
      return
    }
    setBusy(p.ref)
    try {
      await deleteProject(p.ref)
      toast(`'${p.name}' 삭제됨`, 'success')
      await reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(null)
    }
  }

  if (roles === null || online === null) return <LoadingBlock label="설정 불러오는 중" />

  // 역할을 붙일 대상은 계정의 프로젝트다. 목록을 못 받았으면 이미 정해둔 것만 보여준다.
  const rows: { ref: string; name: string; region?: string; status?: string }[] = projects
    ? projects.map((p) => ({ ref: p.ref, name: p.name, region: p.region, status: p.status }))
    : Object.keys(roles).map((ref) => ({ ref, name: roles[ref]?.name || ref }))

  return (
    <div className="appset">
      {/* ── 환경 ── */}
      <section className="appset-block">
        <h3 className="appset-title">환경</h3>
        <p className="appset-desc">
          프로젝트마다 이름과 역할을 정합니다. <strong>Unity 가 이 설정을 읽습니다.</strong>
        </p>

        {rows.length === 0 ? (
          <div className="appset-empty">
            {online ? '프로젝트가 없습니다.' : '프로젝트 목록을 받지 못했습니다. 로그인 상태를 확인하세요.'}
          </div>
        ) : (
          <>
            {rows.map((row) => {
              const r = roles[row.ref] ?? { name: '' }
              return (
                <div key={row.ref} className="appset-row">
                  <div className="appset-row-main">
                    <div className="appset-row-name">{row.name}</div>
                    <div className="appset-row-key">{row.ref}</div>
                    {row.region && (
                      <div className="appset-row-key">
                        {row.region} · {row.status}
                      </div>
                    )}
                  </div>

                  <div className="appset-row-fields">
                    <input
                      className="form-control form-control-sm"
                      placeholder="환경 이름 (dev, prod …)"
                      value={r.name}
                      onChange={(e) => patch(row.ref, { name: e.target.value })}
                    />
                  </div>
                </div>
              )
            })}

            {/* 편집·빌드는 프로젝트 하나의 속성이 아니라 **프로젝트들 사이의 선택**이다.
                줄마다 체크박스로 두면 A 를 켤 때 B 가 꺼지는 것이 안 보여서, 지금 어디가
                편집인지 한눈에 확인할 수 없다. 그래서 한 자리에 모아 고르게 한다. */}
            <div className="appset-row">
              <div className="appset-row-main">
                <div className="appset-row-name">편집 환경</div>
                <div className="appset-row-key">
                  Unity 를 컴파일할 때 스키마가 여기로 반영되고, 어드민·에디터 플레이도 여기를 봅니다
                </div>
              </div>
              <div className="appset-row-fields">
                <select
                  className="form-select form-select-sm"
                  value={pick(roles, 'editor')}
                  onChange={(e) => toggle(e.target.value, 'editor', true)}
                >
                  {rows.map((row) => (
                    <option key={row.ref} value={row.ref}>
                      {roles[row.ref]?.name || row.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="appset-row">
              <div className="appset-row-main">
                <div className="appset-row-name">빌드 환경</div>
                <div className="appset-row-key">
                  게임 빌드에 이 프로젝트의 주소가 구워집니다 — 편집과 <strong>달라도 됩니다</strong>
                </div>
              </div>
              <div className="appset-row-fields">
                <select
                  className="form-select form-select-sm"
                  value={pick(roles, 'build')}
                  onChange={(e) => toggle(e.target.value, 'build', true)}
                >
                  {rows.map((row) => (
                    <option key={row.ref} value={row.ref}>
                      {roles[row.ref]?.name || row.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <div className="appset-actions">
              <button
                className="btn btn-primary btn-sm"
                disabled={!dirty || saving}
                onClick={() => void save()}
              >
                {saving ? (
                  <>
                    <Spinner size={12} />
                    저장 중…
                  </>
                ) : (
                  '저장'
                )}
              </button>
              {dirty && <span className="appset-dirty">저장하지 않은 변경이 있습니다</span>}
            </div>
          </>
        )}
      </section>

      {/* ── 로그인 ── */}
      <section className="appset-block">
        <h3 className="appset-title">로그인</h3>

        <AuthProvidersBlock />
      </section>

      {/* ── 위험 영역 ── */}
      <section className="appset-block danger">
        <h3 className="appset-title danger">위험 영역</h3>
        <p className="appset-desc">
          아래 조작은 <strong>되돌릴 수 없습니다.</strong> 데이터·백업·스냅샷이 함께 사라집니다.
        </p>

        {!online ? (
          <div className="appset-empty">프로젝트 목록을 받지 못해 조작할 수 없습니다.</div>
        ) : !projects || projects.length === 0 ? (
          <div className="appset-empty">삭제할 프로젝트가 없습니다.</div>
        ) : (
          projects.map((p) => (
            <div key={p.ref} className="appset-danger-row">
              <div>
                <div className="appset-danger-name">{p.name} 삭제</div>
                <div className="appset-row-key">{p.ref}</div>
              </div>
              <button
                className="btn btn-sm btn-danger"
                disabled={busy !== null}
                onClick={() => void remove(p)}
              >
                {busy === p.ref ? <Spinner size={11} /> : '삭제'}
              </button>
            </div>
          ))
        )}
      </section>
    </div>
  )
}
