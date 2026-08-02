import { useCallback, useEffect, useState } from 'react'
import {
  generateSecret,
  KNOWN_SECRETS,
  loadSecretMeta,
  saveSecret,
  type KnownSecret,
  type SecretMeta,
} from '../../shared/secrets'
import { env } from '../../shared/env'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { sb } from '../../shared/supabase'
import { toast } from '../../shared/toast'
import { recordViewed } from '../audit/viewed'

/**
 * 공유 비밀.
 *
 * **설정과 화면을 나눈 이유**: 드나드는 빈도가 다르다. 설정은 자주 보고 비밀은 드물게 만진다.
 * 섞어 두면 설정을 보러 갔다가 비밀이 눈에 들어오고, 나중에 "설정은 보되 비밀은 못 보는"
 * 역할이 필요해졌을 때 화면부터 갈라야 한다.
 *
 * 값은 표시하지 않는다 — 표에 SELECT 정책이 없어서 **읽어 올 수도 없다**(shared/secrets.ts).
 */
export function SecretsPage() {
  // 민감 화면(비밀 목록 — 값은 안 보여도 무엇이 있는지가 정보다) — 진입을 자기기록한다 (#27).
  useEffect(() => recordViewed('suparun_secret'), [])

  const [meta, setMeta] = useState<SecretMeta[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [draft, setDraft] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setError(null)
    try {
      setMeta(await loadSecretMeta())
    } catch (e) {
      setMeta([])
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  async function save(key: string, override?: string) {
    const value = (override ?? draft[key] ?? '').trim()
    if (!value) return

    setBusy(key)
    try {
      const { data } = sb ? await sb.auth.getSession() : { data: { session: null } }
      await saveSecret(key, value, data.session?.user?.email ?? 'admin')
      setDraft((d) => ({ ...d, [key]: '' }))
      toast('저장했습니다', 'success')
      await reload()
    } catch (e) {
      toast(e instanceof Error ? e.message : String(e), 'error')
    } finally {
      setBusy(null)
    }
  }

  if (meta === null) return <LoadingBlock label="비밀 목록 불러오는 중" />

  const byKey = new Map(meta.map((m) => [m.key, m]))

  // DB 에만 있고 목록에 없는 키도 보여준다 — 모르는 값이 조용히 쌓이는 것이 더 나쁘다.
  const extra: KnownSecret[] = meta
    .filter((m) => !KNOWN_SECRETS.some((k) => k.key === m.key))
    .map((m) => ({ key: m.key, label: m.key, hint: 'SupaRun 이 모르는 키입니다.' }))

  const projectRef = env().projectRef

  return (
    <div className="appset">
      <section className="appset-block">
        <h3 className="appset-title">
          공유 비밀{' '}
          <i
            className="fa-solid fa-circle-info hint-i"
            title="값은 저장만 되고 다시 보이지 않습니다 — 읽는 경로(SELECT 정책)가 없어 관리자라도 꺼낼 수 없습니다. 빈칸이 정상이고, 채워 넣으면 덮어씁니다."
          />
        </h3>
        <p className="appset-desc">팀이 공유하는 값입니다. 저장한 값은 다시 보이지 않습니다.</p>

        {error && <div className="gsetup-warn">{error}</div>}

        {[...KNOWN_SECRETS, ...extra].map((s) => {
          const m = byKey.get(s.key)
          return (
            <div key={s.key} className="appset-row">
              <div className="appset-row-main">
                <div className="appset-row-name">
                  {s.label}
                  {/* 상태는 칩 하나 — 언제·누가 채웠는지는 툴팁이 말한다 */}
                  <span
                    className={`stat-chip ${m ? 'on' : 'off'}`}
                    title={
                      m
                        ? `${m.updatedAt ? new Date(m.updatedAt).toLocaleString() : '시각 모름'}${
                            m.updatedBy ? ` · ${m.updatedBy}` : ''
                          }`
                        : undefined
                    }
                  >
                    {m ? '설정됨' : '비어 있음'}
                  </span>
                  {s.shared && (
                    <span className="stat-chip off" title="모든 환경이 같은 값을 씁니다">
                      공통
                    </span>
                  )}
                </div>
                <div className="appset-row-key">{s.hint}</div>
              </div>

              <div className="appset-row-fields">
                {/* 사람이 정할 값이 아니면 만들어 준다 — 입력칸을 띄울 이유가 없다. */}
                {s.generate ? (
                  <button
                    className="btn btn-primary btn-sm"
                    disabled={busy !== null}
                    onClick={() => void save(s.key, generateSecret())}
                  >
                    {busy === s.key ? <Spinner size={12} /> : m ? '새로 만들기' : '만들기'}
                  </button>
                ) : (
                  <>
                    <input
                      type="password"
                      autoComplete="new-password"
                      className="form-control form-control-sm"
                      placeholder={m ? '바꾸려면 새 값을 입력' : '값을 입력'}
                      value={draft[s.key] ?? ''}
                      onChange={(e) => setDraft((d) => ({ ...d, [s.key]: e.target.value }))}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void save(s.key)
                      }}
                    />
                    <button
                      className="btn btn-primary btn-sm"
                      disabled={busy !== null || !(draft[s.key] ?? '').trim()}
                      onClick={() => void save(s.key)}
                    >
                      {busy === s.key ? <Spinner size={12} /> : '저장'}
                    </button>
                  </>
                )}
                {/* 값을 얻으러 갈 곳 — 링크 한 줄 대신 아이콘 버튼 하나 */}
                {s.link && (
                  <a
                    className="btn btn-sm btn-icon"
                    href={s.link(projectRef)}
                    target="_blank"
                    rel="noreferrer"
                    title={s.linkLabel}
                  >
                    <i className="fa-solid fa-arrow-up-right-from-square" />
                  </a>
                )}
              </div>
            </div>
          )
        })}

        {/* PAT 정책 — 산문 카드 하나를 차지하던 설명의 요약. 세부는 툴팁. */}
        <p
          className="appset-foot"
          title="계정 전체의 마스터키라 팀이 하나를 돌려쓰면 감사 추적이 사라지고, 그 사람이 나가면 전부 끊깁니다. DB 에는 한 번도 올라가지 않습니다."
        >
          Supabase Access Token(PAT)은 여기 없습니다 — 각자 발급해 자기 Unity 에만 둡니다.
        </p>
      </section>
    </div>
  )
}
