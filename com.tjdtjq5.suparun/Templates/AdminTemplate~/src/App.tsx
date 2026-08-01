import { useEffect, useRef, useState } from 'react'
import { auth as bridgeAuth, bridgeAvailable, needsSetup, opsVisible, setup, type SetupState } from './shared/bridge'
import { isPreview } from './shared/env'
import { FullScreenLoader } from './shared/Spinner'
import { sb } from './shared/supabase'
import { LoginPage } from './features/auth/LoginPage'
import { useSession } from './features/auth/useSession'
import { OnboardingPage } from './features/setup/OnboardingPage'
import { Shell } from './features/shell/Shell'

/**
 * 앱 루트 — **사람 로그인이 신원이다** (ADR-0009, #23).
 *
 * 기계 계정 자동 로그인을 걷어냈다. 그 논거("로컬 전용이라 로그인이 보안을 더하지 않는다")는
 * 원격 접근자가 생기는 순간 무너지고, 감사 로그의 "누가" 도 행위자 식별 없이는 무의미하다.
 * 감사 트리거(suparun_audit)는 auth.uid() 를 남기므로, 로그인만 복원하면 행위자는 저절로
 * 로그인 계정이 된다.
 *
 * 화면은 이렇게 갈린다:
 *   셋업 구간(PAT·프로젝트·스키마 빈칸) → 온보딩 (세션 무관 — 브리지+PAT 의 일이고,
 *                                        프로젝트가 없으면 로그인할 곳도 없다)
 *   세션 없음                          → 로그인
 *   세션 있음 + 관리자                  → 어드민
 *   세션 있음 + 관리자 아님             → 승인 대기 안내
 *
 * 관리자 판정은 `admin_user` 의 자기 행(self_read RLS)이다. 로컬(브리지)에서는 판정 전에
 * `/auth/claim-admin` 이 등록까지 해 주므로 대기 화면을 볼 일이 사실상 없다.
 */
export function App() {
  const preview = isPreview()
  // 호스팅본(#48) = 브리지도 미리보기도 아니다(opsVisible 의 부정 — 게이트 판정은 한 곳).
  // 셋업·온보딩은 로컬(브리지+PAT)의 일이라 통째로 건너뛴다 — 호스팅본이 서빙된다는 것
  // 자체가 셋업이 끝난 환경이라는 뜻이다.
  const hosted = !opsVisible()

  // 셋업이 어디까지 됐는가. 세션보다 먼저 본다 — 프로젝트가 없으면 세션도 없다.
  const [setupState, setSetupState] = useState<SetupState | null | undefined>(undefined)
  useEffect(() => {
    if (preview || hosted) return
    void setup.state().then(setSetupState).catch(() => setSetupState(null))
  }, [preview, hosted])

  const { session, ready } = useSession()
  const token = session?.access_token

  // 어떤 롤을 가졌는가 (#24 — 빌트인 4롤, 합집합). 무롤 = 승인 대기.
  // 세션이 바뀔 때마다 다시 판정한다 — 로그인 직후 갱신돼야 한다.
  const [roles, setRoles] = useState<string[] | 'boot'>('boot')
  // claim 을 이미 마친 uid. 토큰은 ~1시간마다 갱신되는데(TOKEN_REFRESHED),
  // 그때마다 등록 SQL 을 다시 쏘지 않기 위한 가드다 — 실패 시에는 남겨서 다음에 재시도한다.
  const claimedUid = useRef<string | null>(null)
  useEffect(() => {
    if (preview || !token) return
    let alive = true
    setRoles('boot')
    void (async () => {
      const uid = session?.user?.id ?? ''

      // 로컬이면 판정 전에 등록부터 — 첫 관리자 매듭은 브리지의 PAT 가 끊는다.
      // 실패해도 계속 간다(이미 등록된 관리자는 아래 판정이 그대로 통과한다).
      // 대기 화면이 떴다면 원인은 이 경고와 Unity Console 에 있다.
      if (bridgeAvailable() && claimedUid.current !== uid) {
        const ok = await bridgeAuth.claimAdmin(token).then(
          () => true,
          (e) => {
            console.warn('claim-admin 실패:', e)
            return false
          },
        )
        if (ok) claimedUid.current = uid
      }

      if (!sb) {
        if (alive) setRoles([])
        return
      }

      // 자기 신원을 명단(admin_user)에 올린다 — 이 행이 있어야 User Roles 화면(#24)에
      // 대기자로 보인다. self_insert RLS 라 자기 행만 가능하고, 롤은 매핑 테이블이라
      // 여기서 self-grant 는 안 된다. claim(로컬)이 이미 만들었으면 건너뛴다.
      const me = await sb.from('admin_user').select<{ id: string }[]>('id').eq('user_id', uid)
      if (!me.error && (me.data?.length ?? 0) === 0) {
        await sb.from('admin_user').insert({
          id: uid,
          user_id: uid,
          email: session?.user?.email ?? '',
          provider: 'email',
          created_at: Date.now(),
          created_by: 'self',
        })
      }

      const r = await sb.from('admin_user_role').select<{ role: string }[]>('role').eq('user_id', uid)
      if (!alive) return
      setRoles((r.data ?? []).map((x) => x.role))
    })()
    return () => {
      alive = false
    }
    // session 은 token 과 같이 움직인다 — 의존성은 token 하나로 충분하다.
  }, [preview, token])

  if (preview) {
    return (
      <div id="admin-page" className="page active">
        <Shell email="preview@mock.local" />
      </div>
    )
  }

  if (!hosted) {
    if (setupState === undefined) return <FullScreenLoader label="설정 확인 중" />

    // 브리지가 안 잡히면 Unity 가 꺼진 것이다. 이 페이지 자체를 브리지가 내보내므로
    // 열려 있다면 살아 있었다는 뜻이고, 지금 안 잡히면 그 사이에 닫힌 것이다.
    if (setupState === null) {
      return (
        <Notice tone="warning">
          <b>Unity 가 응답하지 않습니다.</b>
          <br />
          어드민은 Unity 안의 로컬 서버가 내보냅니다. Unity 를 켠 뒤 새로고침하세요.
        </Notice>
      )
    }

    // 빈칸이 하나라도 있으면 온보딩. 플래그가 아니라 사실로 판정하므로,
    // 중단했다 돌아와도 그 지점에서 이어진다.
    if (needsSetup(setupState)) return <OnboardingPage />
  }

  if (!ready) return <FullScreenLoader label="세션 확인 중" />

  if (!session) return <LoginPage />

  if (roles === 'boot') return <FullScreenLoader label="권한 확인 중" />

  // 로그인은 됐는데 롤이 하나도 없다(승인 대기). 로그인 화면으로 돌리면 눌러도 같은
  // 자리로 돌아와 원인을 알 수 없다 — 대기임을 말하고 로그아웃만 준다.
  if (roles.length === 0) {
    return (
      <Notice tone="warning">
        <b>승인 대기 중입니다.</b>
        <br />
        {session.user?.email ?? '이 계정'} 으로 로그인했지만 아직 롤이 부여되지 않았습니다.
        <br />
        game-admin 에게 User Roles 화면에서 롤 부여를 요청하세요.
        <div className="action-line" style={{ marginTop: 12 }}>
          <button className="btn-terminal" onClick={() => void sb?.auth.signOut()}>
            [LOGOUT]
          </button>
        </div>
      </Notice>
    )
  }

  return (
    <div id="admin-page" className="page active">
      <Shell
        email={session.user?.email ?? ''}
        roles={roles}
        onLogout={() => void sb?.auth.signOut()}
      />
    </div>
  )
}

function Notice({ tone, children }: { tone: 'warning' | 'danger'; children: React.ReactNode }) {
  return (
    <div className="page page-center">
      <div className="terminal-window">
        <div className="terminal-body">
          <div className={`alert alert-${tone}`}>{children}</div>
        </div>
      </div>
    </div>
  )
}
