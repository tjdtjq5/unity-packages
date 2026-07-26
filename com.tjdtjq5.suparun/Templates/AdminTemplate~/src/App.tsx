import { useEffect, useState } from 'react'
import { onUnauthorized, type UnauthorizedInfo } from './shared/api'
import { whoAmI, type WhoAmI } from './shared/edgeFn'
import { isPreview } from './shared/env'
import { FullScreenLoader } from './shared/Spinner'
import { sb } from './shared/supabase'
import { LoginPage } from './features/auth/LoginPage'
import { useSession } from './features/auth/useSession'
import { Shell } from './features/shell/Shell'

/**
 * 앱 루트 — 누가 들어올 수 있는지 정한다.
 *
 * 판정 근거는 **관리자인가**(`/whoami`)다. 예전에는 "로그인 프로바이더가 켜져 있는가" 로
 * 판단했는데, 그건 사람의 권한과 아무 상관이 없다. 그래서 프로바이더를 끄면 아무나 화면
 * 전체를 볼 수 있었고, 승인 대기 중인 계정도 그대로 들어와졌다.
 *
 * 화면은 넷으로 갈린다:
 *   관리자            → 어드민
 *   셋업 구간         → 어드민. 설정에서 로그인 수단을 켤 수 있어야 하는데 아무도 로그인할
 *                       수 없는 상태라, 여기서 막으면 영원히 못 켠다
 *   로그인했는데 대기  → 승인 대기 안내
 *   로그인 안 함      → 로그인 화면
 */
export function App() {
  const { session, ready } = useSession()
  /** 401/403 으로 쫓겨난 상태. 세션 자체는 살아 있을 수 있다(권한 부족). */
  const [kickedOut, setKickedOut] = useState<UnauthorizedInfo | null>(null)

  useEffect(() => onUnauthorized(setKickedOut), [])

  // 거부 상태는 **새 토큰이 실제로 발급됐을 때만** 푼다.
  // "로그인 버튼을 눌렀을 때" 풀면 아직 이전 세션인 채로 어드민이 다시 떠서
  // 같은 403 을 한 번 더 맞고, 그 사이 도착한 정상 로그인까지 거부 상태로 덮인다.
  const token = session?.access_token
  useEffect(() => {
    if (token) setKickedOut(null)
  }, [token])

  const preview = isPreview()

  const [me, setMe] = useState<WhoAmI | null | undefined>(undefined)

  // 세션이 바뀌면 다시 묻는다 — 로그인 직후에 판정이 갱신돼야 한다.
  useEffect(() => {
    if (preview || !ready) return
    let alive = true
    setMe(undefined)
    void whoAmI().then((w) => alive && setMe(w))
    return () => {
      alive = false
    }
  }, [preview, ready, token])

  if (!preview && (!ready || me === undefined)) return <FullScreenLoader label="권한 확인 중" />

  // 함수가 아직 배포되지 않았으면 판정할 근거가 없다. 그 상태에서 잠그면 배포하러 갈
  // 방법도 없어지므로 열어 둔다 — 어차피 쓰기는 RLS 가 막는다.
  const undecidable = me === null

  // 로그인 수단이 하나도 없으면 **로그인 화면을 띄우지 않는다.** 누를 것이 없는 화면은
  // 언제나 막다른 길이다. 그 상태에서 사람이 가야 하는 곳은 설정이지 로그인이 아니다.
  const noProvider = (me?.providers.length ?? 0) === 0

  if (preview || me?.isAdmin || me?.setupOpen || undecidable || noProvider) {
    return (
      <div id="admin-page" className="page active">
        <Shell
          email={preview ? 'preview@mock.local' : (me?.email ?? '')}
          onLogout={() => void sb?.auth.signOut()}
          /** 관리자가 아닌 채로 들어와 있는 상태 — 껍데기가 로그아웃 버튼을 감춘다. */
          unlocked={!preview && !me?.isAdmin}
        />
      </div>
    )
  }

  // 로그인은 됐는데 아직 승인 전. 로그인 화면을 다시 띄우면 눌러도 같은 자리로 돌아와
  // 원인을 알 수 없다.
  if (me?.userId) {
    return (
      <div className="page page-center">
        <div className="terminal-window">
          <div className="terminal-titlebar">
            <span className="dot" />
            <span className="title">SUPARUN.ADMIN :: PENDING</span>
          </div>
          <div className="terminal-body">
            <div className="alert alert-warning">
              <b>승인 대기 중입니다.</b>
              <br />
              {me.email ?? '이 계정'} 으로 로그인했지만 아직 관리자로 승인되지 않았습니다.
              <br />
              기존 관리자에게 승인을 요청하세요.
            </div>
            <div className="action-line">
              <button className="btn-terminal" onClick={() => void sb?.auth.signOut()}>
                [LOGOUT]
              </button>
            </div>
          </div>
        </div>
      </div>
    )
  }

  return (
    <LoginPage
      notice={kickedOut}
      onDismissNotice={() => setKickedOut(null)}
      providers={me?.providers ?? []}
    />
  )
}
