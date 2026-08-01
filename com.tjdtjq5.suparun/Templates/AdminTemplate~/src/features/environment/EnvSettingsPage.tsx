import { useCallback, useEffect, useState } from 'react'
import { deleteProject, listProjects, type SupabaseProject } from '../../shared/projects'
import {
  EMPTY_ENV,
  loadEnvSettings,
  PLATFORM_AUTH,
  saveEnvSettings,
  type EnvSettings,
} from '../../shared/envSettings'
import { bridgeAvailable } from '../../shared/bridge'
import { env as suparunEnv } from '../../shared/env'
import { ops, type OpsState } from '../../shared/ops'
import { DeployBlock } from '../deploy/DeployBlock'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { sb } from '../../shared/supabase'
import { toast } from '../../shared/toast'

/**
 * **이 환경**의 설정 — 환경 안 화면이다.
 *
 * 한때 앱 레벨(환경을 고르기 전)에 있었다. 그런데 내용물이 전부 특정 프로젝트의 값이라
 * "고르기 전인데 어느 환경을 고치는가" 가 어긋나 있었다 — 그래서 환경 안으로 들어왔다.
 * 다른 환경은 그 환경에 들어가서 고친다(전환 입장이 가벼워져 이동 비용이 없다).
 *
 * **어드민이 진실이고 Unity 가 읽는다.** 값은 `suparun_env` 에 있고, Unity 는 대시보드를 열 때와
 * 배포·빌드 직전에 읽어 간다. 그래서 저장한 뒤에도 다음 배포까지는 반영되지 않는다.
 *
 * 예외가 **이름**이다. 이름의 진실은 슬롯(Unity 설정)이다 — 편집·빌드 환경 지정과 해시파일
 * 키가 이 문자열을 쓴다. 그래서 이름 변경은 브리지 op 로 가고(슬롯·해시파일·DB 동시 갱신),
 * 브리지가 없는 배포 어드민에서는 읽기 전용이다. DB 만 고치면 카드와 슬롯이 다른 이름을 말한다.
 *
 * 되돌릴 수 없는 조작은 맨 아래 위험 영역에 모은다. 대상은 **이 프로젝트뿐이다** —
 * 다른 프로젝트를 지우려면 거기 들어가서 지운다. dev 설정에서 prod 가 지워지는 배치를 없앤다.
 */
export function EnvSettingsPage() {
  const local = bridgeAvailable()
  const here = projectRefOf(suparunEnv().supabaseUrl)

  const [saved, setSaved] = useState<EnvSettings | null>(null)
  const [form, setForm] = useState<EnvSettings>(EMPTY_ENV)
  const [loadError, setLoadError] = useState<string | null>(null)

  /** 브리지 상태(슬롯 이름·빌드 환경). 로컬에서만 채워진다. */
  const [st, setSt] = useState<OpsState | null>(null)
  const [envName, setEnvName] = useState('')

  const [projects, setProjects] = useState<SupabaseProject[] | null>(null)
  const [online, setOnline] = useState<boolean | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setLoadError(null)
    try {
      const s = await loadEnvSettings()
      setSaved(s)
      setForm(s)
    } catch (e) {
      setSaved(EMPTY_ENV)
      setForm(EMPTY_ENV)
      setLoadError(e instanceof Error ? e.message : String(e))
    }

    if (local) {
      try {
        const o = await ops.state()
        setSt(o)
        setEnvName(o.editorEnv)
      } catch {
        setSt(null)
      }
    }

    try {
      setProjects(await listProjects())
      setOnline(true)
    } catch {
      setProjects(null)
      setOnline(false)
    }
  }, [local])

  useEffect(() => {
    void reload()
  }, [reload])

  function patch(k: keyof EnvSettings, v: string) {
    setForm((f) => ({ ...f, [k]: v }))
  }

  /** 즉시 저장 — 어드민의 다른 화면(ConfigCell)이 이미 onBlur 로 쓴다. 저장 버튼을 두지 않는다. */
  async function commit(next: EnvSettings) {
    if (!saved) return
    try {
      const { data } = sb ? await sb.auth.getSession() : { data: { session: null } }
      const n = await saveEnvSettings(next, saved, data.session?.user?.email ?? 'admin')
      setSaved(next)
      if (n > 0) toast('저장됨', 'success')
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    }
  }

  /** 이름 변경 — 브리지가 슬롯·해시파일·DB 를 한 번에 바꾼다. */
  async function rename() {
    const to = envName.trim()
    if (!st || !to || to === st.editorEnv) return
    setBusy('rename')
    try {
      await ops.renameEnv(to)
      toast(`'${to}' 로 이름을 바꿨습니다`, 'success')
      await reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setEnvName(st.editorEnv)
    } finally {
      setBusy(null)
    }
  }

  /** 슬롯만 지운다. 프로젝트는 남는다 — 카드에서 미연결로 보이고 다시 셋업할 수 있다. */
  async function unlink() {
    if (!st) return
    if (
      !window.confirm(
        `'${st.editorEnv}' 슬롯을 목록에서 지웁니다.\n\n` +
          'Supabase 프로젝트와 그 안의 데이터는 그대로 남습니다.\n' +
          '편집 환경은 남은 환경으로 옮겨지고 어드민이 다시 열립니다.',
      )
    )
      return
    setBusy('unlink')
    try {
      await ops.removeEnv(st.editorEnv)
      // 편집 환경이 다른 슬롯으로 옮겨졌다 — 리로드해야 브리지가 그 환경 값을 꽂는다.
      window.location.reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setBusy(null)
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
      // 지금 들어와 있는 프로젝트가 사라졌다. 슬롯도 같이 걷어내고(실패해도 무방 — 마지막
      // 환경이면 남는다) 환경 선택부터 다시 시작한다. 유령 안에 남아 있는 것보다 낫다.
      if (local && st) await ops.removeEnv(st.editorEnv).catch(() => undefined)
      window.location.reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
      setBusy(null)
    }
  }

  if (saved === null || online === null) return <LoadingBlock label="설정 불러오는 중" />

  // 위험 영역의 대상은 이 프로젝트 하나다.
  const thisProject = projects?.find((p) => p.ref === here) ?? null
  const thisEnv = st?.environments.find((e) => e.name === st.editorEnv)
  const autoSchema = thisEnv?.autoSchemaSync ?? false
  const autoIds = thisEnv?.autoIdConstants ?? false

  return (
    <div className="appset">
      {loadError && (
        <section className="appset-block">
          <div className="gsetup-warn">{loadError}</div>
        </section>
      )}

      {/* ── 이 환경 ── */}
      <section className="appset-block">
        <h3 className="appset-title">
          이 환경
          {local && <span className="env-badge editor" style={{ marginLeft: 6 }}>편집 중</span>}
        </h3>
        <p className="appset-desc">
          지금 들어와 있는 Supabase 프로젝트의 설정입니다. <strong>Unity 가 이 값을 읽습니다.</strong>{' '}
          다른 환경은 그 환경에 들어가서 고칩니다.
        </p>

        <div className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-name">이름</div>
            <div className="appset-row-key">
              {local
                ? '대시보드와 로그에 표시됩니다 (dev, prod …)'
                : '이름은 로컬 어드민(Unity)에서 바꿉니다'}
            </div>
          </div>
          <div className="appset-row-fields">
            {local && st ? (
              <input
                className="form-control form-control-sm"
                placeholder="dev"
                value={envName}
                disabled={busy !== null}
                onChange={(e) => setEnvName(e.target.value)}
                onBlur={() => void rename()}
              />
            ) : (
              <input
                className="form-control form-control-sm"
                value={form.name}
                disabled
                readOnly
              />
            )}
          </div>
        </div>

        {local && st && (
          <div className="appset-row">
            <div className="appset-row-main">
              <div className="appset-row-name">컴파일 후 자동 스키마 반영</div>
              <div className="appset-row-key">
                {autoSchema ? (
                  '컴파일할 때마다 이 환경에 스키마가 반영됩니다. 팀 공유값입니다(Unity 설정 파일).'
                ) : (
                  <>
                    꺼져 있습니다 — 이 환경의 스키마는 <strong>배포할 때</strong> 반영됩니다.
                    처음 켜면 [UserData] 표에 RLS 정책이 생깁니다. 게임도 같은 anon key 를 쓰므로
                    여기서 연 문은 플레이어에게도 열립니다.
                  </>
                )}
              </div>
            </div>
            <div className="appset-row-fields">
              <button
                className={`btn btn-sm${autoSchema ? ' btn-primary' : ''}`}
                disabled={busy !== null}
                onClick={() => {
                  setBusy('auto')
                  void ops
                    .setAutoSchema(!autoSchema)
                    .then(reload)
                    .catch((e) => toast(e instanceof Error ? e.message : String(e), 'error'))
                    .finally(() => setBusy(null))
                }}
              >
                {busy === 'auto' ? <Spinner size={11} /> : autoSchema ? '켜짐' : '꺼짐'}
              </button>
            </div>
          </div>
        )}

        {local && st && (
          <div className="appset-row">
            <div className="appset-row-main">
              <div className="appset-row-name">행 편집 시 Id 상수 자동 생성</div>
              <div className="appset-row-key">
                {autoIds
                  ? '행이 늘거나 줄면(추가·삭제·복사·스냅샷 복원) {Name}Ids 상수를 자동으로 다시 만듭니다. PK 집합이 실제로 바뀐 경우에만 Unity 가 재컴파일됩니다.'
                  : '꺼져 있습니다 — 이 환경에서의 행 편집이 Unity 코드 생성을 유발하지 않습니다. dev 만 켜는 것이 의도입니다.'}
              </div>
            </div>
            <div className="appset-row-fields">
              <button
                className={`btn btn-sm${autoIds ? ' btn-primary' : ''}`}
                disabled={busy !== null}
                onClick={() => {
                  setBusy('autoIds')
                  void ops
                    .setAutoIds(!autoIds)
                    .then(reload)
                    .catch((e) => toast(e instanceof Error ? e.message : String(e), 'error'))
                    .finally(() => setBusy(null))
                }}
              >
                {busy === 'autoIds' ? <Spinner size={11} /> : autoIds ? '켜짐' : '꺼짐'}
              </button>
            </div>
          </div>
        )}
      </section>

      {/* ── 배포 ──
          체크리스트가 상태·값·자동화를 모두 가진다. 여기서는 자리만 내준다. */}
      <DeployBlock />

      {/* ── 게임 로그인 ──
          어드민 로그인(웹 프로바이더) 블록은 없다 — 어드민은 이메일+비밀번호 전용이라
          (ADR-0009 — 매직링크·OAuth 기각) 켜고 끌 프로바이더가 없다. 여기 남은 것은 플레이어 쪽뿐이다. */}
      <section className="appset-block">
        <h3 className="appset-title">게임 로그인</h3>

        <div className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-name">게임 로그인</div>
            <div className="appset-row-key">
              플레이어가 게임에서 로그인하는 방법입니다. 고른 것만 서버 인증 코드가 생성되므로{' '}
              <strong>다음 배포부터</strong> 반영됩니다.
            </div>
          </div>
          <div className="appset-row-fields">
            {PLATFORM_AUTH.map((p) => {
              const on = form.platformAuth.split(',').filter(Boolean).includes(p.id)
              return (
                <button
                  key={p.id}
                  className={`btn btn-sm${on ? ' btn-primary' : ''}`}
                  title={p.hint}
                  onClick={() => {
                    const cur = form.platformAuth.split(',').filter(Boolean)
                    const next = on ? cur.filter((x) => x !== p.id) : [...cur, p.id]
                    const value = next.join(',')
                    patch('platformAuth', value)
                    void commit({ ...form, platformAuth: value })
                  }}
                >
                  {p.label}
                </button>
              )
            })}
          </div>
        </div>
      </section>

      {/* ── 위험 영역 ── */}
      <section className="appset-block danger">
        <h3 className="appset-title danger">위험 영역</h3>
        <p className="appset-desc">
          아래 조작은 <strong>되돌릴 수 없습니다.</strong> 대상은 이 환경뿐입니다 — 다른
          프로젝트는 거기 들어가서 지웁니다.
        </p>

        {local && st && (
          <div className="appset-danger-row">
            <div>
              <div className="appset-danger-name">연결 해제</div>
              <div className="appset-row-key">
                &apos;{st.editorEnv}&apos; 슬롯만 지웁니다. 프로젝트와 데이터는 남습니다.
              </div>
            </div>
            <button
              className="btn btn-sm btn-danger"
              disabled={busy !== null || st.environments.length <= 1}
              title={st.environments.length <= 1 ? '마지막 환경은 지울 수 없습니다' : ''}
              onClick={() => void unlink()}
            >
              {busy === 'unlink' ? <Spinner size={11} /> : '연결 해제'}
            </button>
          </div>
        )}

        {!online ? (
          <div className="appset-empty">프로젝트 목록을 받지 못해 조작할 수 없습니다.</div>
        ) : !thisProject ? (
          <div className="appset-empty">이 프로젝트를 목록에서 찾지 못했습니다.</div>
        ) : (
          <div className="appset-danger-row">
            <div>
              <div className="appset-danger-name">{thisProject.name} 삭제</div>
              <div className="appset-row-key">{thisProject.ref}</div>
            </div>
            <button
              className="btn btn-sm btn-danger"
              disabled={busy !== null}
              onClick={() => void remove(thisProject)}
            >
              {busy === thisProject.ref ? <Spinner size={11} /> : '삭제'}
            </button>
          </div>
        )}
      </section>
    </div>
  )
}

/** `https://xxx.supabase.co` → `xxx`. 위험 영역이 "이 프로젝트" 를 집는 기준이다. */
function projectRefOf(url: string): string {
  try {
    return new URL(url).hostname.split('.')[0]
  } catch {
    return ''
  }
}
