import { useState } from 'react'
import { env } from '../../shared/env'

/**
 * OAuth 클라이언트를 만드는 구간 안내.
 *
 * **여기가 전체 흐름에서 유일하게 자동화가 안 되는 자리다.** 어느 프로바이더든 콘솔에서
 * 사람이 앱을 등록해야 Client ID/Secret 이 나온다(Google 은 `gcloud iam oauth-clients` 가
 * Workforce Identity Federation 전용이고 IAP OAuth Admin API 는 2026-03-19 에 종료된다).
 * 그래서 **우리가 이미 아는 값을 사람이 손으로 옮기지 않게 하는 것**이 목적이다.
 *
 * 특히 콜백 URL 은 오타 하나로 실패하는데, 그 실패가 **로그인할 때까지 드러나지 않는다.**
 * 저장도 되고 화면도 정상이고, 나중에 `redirect_uri_mismatch` 만 뜬다.
 */

/** Supabase 가 OAuth 응답을 받는 고정 주소. 프로바이더 콘솔에 **그대로** 등록해야 한다. */
export function redirectUri(): string {
  const url = env().supabaseUrl ?? ''
  return url ? `${url.replace(/\/$/, '')}/auth/v1/callback` : ''
}

interface ProviderGuide {
  /** 콘솔 이름. 화면 문구에 그대로 들어간다. */
  console: string
  /** 앱을 만드는 화면. 프로바이더마다 이름이 달라 링크 문구도 같이 둔다. */
  links: { label: string; url: string }[]
  /** 그 콘솔이 콜백 칸을 뭐라고 부르는가. 이름이 다르면 사람이 못 찾는다. */
  callbackLabel: string
  /** 홈페이지/원본 주소를 요구하는 콘솔만. 이름도 콘솔이 부르는 그대로 쓴다. */
  siteLabel?: string
  /** 콜백 칸이 어느 메뉴에 있는가. 메뉴가 깊으면 이름만으로는 못 찾는다. */
  callbackWhere?: string
  /** 먼저 해야 하는 것이 있으면. */
  prerequisite?: string
  /**
   * 콘솔이 이 값을 뭐라고 부르는가. **입력칸 이름을 이걸로 바꾼다.**
   * 카카오는 REST API·네이티브·JavaScript·Admin 키를 함께 주는데 맞는 것은 REST API 키
   * 하나뿐이다. 칸 이름이 "Client ID" 면 어느 키인지 알 수 없고, 틀린 키를 넣어도 저장은
   * 되고 로그인만 실패한다 — 형식이 비슷해서 검사로도 안 걸린다.
   */
  idLabel?: string
  secretLabel?: string
  /** 값을 얻는 데 추가 조작이 필요하면. */
  secretNote?: string
  /** Client ID/Secret 형식. 틀린 값을 붙여넣었을 때 알려 주는 근거. */
  idSuffix?: string
  secretPrefix?: string
}

const GUIDES: Record<string, ProviderGuide> = {
  google: {
    console: 'Google Cloud Console',
    prerequisite:
      '동의 화면을 먼저 만들어야 클라이언트를 만들 수 있습니다. 범위에 openid 를 직접 추가하세요 — 기본에 없습니다.',
    links: [
      { label: '동의 화면 열기', url: 'https://console.cloud.google.com/apis/credentials/consent' },
      { label: '클라이언트 만들기', url: 'https://console.cloud.google.com/auth/clients/create' },
    ],
    callbackLabel: '승인된 리디렉션 URI',
    siteLabel: '승인된 JavaScript 원본',
    idSuffix: '.apps.googleusercontent.com',
    secretPrefix: 'GOCSPX-',
  },
  github: {
    console: 'GitHub Developer settings',
    links: [
      { label: 'OAuth App 만들기', url: 'https://github.com/settings/applications/new' },
      { label: '기존 App 목록', url: 'https://github.com/settings/developers' },
    ],
    callbackLabel: 'Authorization callback URL',
    siteLabel: 'Homepage URL',
  },
  discord: {
    console: 'Discord Developer Portal',
    // 브라우저 자동 번역이 이 사이트를 깨뜨린다. 번역기가 React 가 만든 텍스트 노드를
    // 바꿔치기해서 "removeChild ... not a child of this node" 로 터진다.
    // 저장 버튼을 누르는 순간처럼 DOM 이 바뀌는 시점에 정확히 난다.
    prerequisite:
      '이 사이트에서 브라우저 자동 번역을 끄세요. 켜져 있으면 저장할 때 ' +
      '"removeChild ... not a child of this node" 오류가 나고 등록이 실패합니다.',
    links: [{ label: '애플리케이션 열기', url: 'https://discord.com/developers/applications' }],
    callbackLabel: 'Redirects',
    callbackWhere: 'OAuth2 > Redirects > Add Redirect — 입력 후 하단 [Save Changes] 를 눌러야 반영됩니다',
  },
  kakao: {
    console: 'Kakao Developers',
    // 카카오는 켜는 것이 먼저다. 로그인을 활성화하지 않으면 Redirect URI 칸 자체가 없다.
    prerequisite:
      '제품 설정 > 카카오 로그인 > 일반 에서 활성화를 ON 으로 바꿔야 Redirect URI 칸이 생깁니다. ' +
      '그리고 이메일(account_email)은 비즈 앱에서만 열립니다 — 안 열면 Supabase 가 사용자를 ' +
      '만들지 못해 로그인 자체가 실패합니다. 사업자등록번호 없이도 본인인증 + 약관 동의로 ' +
      '전환할 수 있지만(계정 설정 > 본인인증), 어드민 로그인만 쓸 거라면 Google 이나 GitHub 가 훨씬 빠릅니다.',
    links: [{ label: '내 애플리케이션', url: 'https://developers.kakao.com/console/app' }],
    // 웹훅이 아니다. 로그인용 자리가 따로 있고, 사람들이 여기서 가장 많이 헤맨다.
    callbackLabel: 'Kakao Login Redirect URI',
    callbackWhere: '앱 설정 > 앱 > 플랫폼 키 > (REST API 키 클릭)',
    idLabel: 'REST API 키',
    secretLabel: '카카오 로그인 Client Secret 코드',
    secretNote: 'Client Secret 은 기본으로 꺼져 있습니다. 같은 화면에서 활성화해야 값이 생깁니다.',
  },
  apple: {
    console: 'Apple Developer',
    links: [
      {
        label: 'Service ID 만들기',
        url: 'https://developer.apple.com/account/resources/identifiers/list/serviceId',
      },
    ],
    callbackLabel: 'Return URLs',
  },
}

function CopyRow({ label, value, hint }: { label: string; value: string; hint?: string }) {
  const [copied, setCopied] = useState(false)

  async function copy() {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // 클립보드 권한이 없으면 선택이라도 되게 둔다 — 값은 화면에 그대로 있다.
      setCopied(false)
    }
  }

  return (
    <div className="gsetup-copy">
      <div className="gsetup-copy-label">
        {label}
        {hint && <span className="gsetup-copy-hint"> {hint}</span>}
      </div>
      <div className="gsetup-copy-row">
        <code className="gsetup-copy-value">{value || '(설정 안 됨)'}</code>
        <button className="btn btn-sm" disabled={!value} onClick={() => void copy()}>
          {copied ? '복사됨' : '복사'}
        </button>
      </div>
    </div>
  )
}

/** 입력칸 이름. 콘솔이 부르는 이름을 그대로 쓴다 — 자세한 근거는 ProviderGuide.idLabel 참조. */
export function fieldLabels(provider: string): { id: string; secret: string } {
  const g = GUIDES[provider]
  return { id: g?.idLabel ?? 'Client ID', secret: g?.secretLabel ?? 'Client Secret' }
}

export function ProviderSetupGuide({ provider }: { provider: string }) {
  const g = GUIDES[provider]
  if (!g) return null

  return (
    <div className="gsetup">
      {g.prerequisite && <div className="gsetup-pre">{g.prerequisite}</div>}

      <ol className="gsetup-steps">
        <li>
          <b>{g.console}</b> 에서 앱을 등록합니다.
          <div className="gsetup-actions">
            {g.links.map((l) => (
              <a key={l.url} className="btn btn-sm me-2" href={l.url} target="_blank" rel="noreferrer">
                {l.label} <i className="ti ti-external-link ms-1" />
              </a>
            ))}
          </div>
        </li>

        <li>
          <b>{g.callbackLabel}</b> 에 아래 주소를 그대로 붙여넣습니다.
          {g.callbackWhere && <div className="gsetup-where">{g.callbackWhere}</div>}
          <CopyRow label={g.callbackLabel} value={redirectUri()} />
          {/* 홈페이지 주소는 동의 화면에 보여주는 용도라 인증에 관여하지 않는다 —
              콜백과 성격이 달라서, 틀려도 로그인이 깨지지 않는다는 것을 같이 적는다. */}
          {g.siteLabel && (
            <CopyRow
              label={g.siteLabel}
              value={window.location.origin}
              hint="— 표시용입니다. 로그인 동작에는 영향이 없습니다"
            />
          )}
        </li>

        <li>
          발급된 값을 아래 칸에 붙여넣고 저장합니다.
          {g.secretNote && <div className="gsetup-where">{g.secretNote}</div>}
        </li>
      </ol>
    </div>
  )
}

/**
 * 붙여넣은 값이 형식에 맞는가. **막지 않고 알리기만 한다.**
 *
 * 틀린 값을 넣어도 저장은 성공하고 화면도 정상이며, 실제 로그인에서만 실패한다.
 * 그 조합이 가장 찾기 어려운 실패라 붙여넣는 순간 짚어 주는 값어치가 크다.
 * 다만 옛날에 발급한 것은 형식이 다를 수 있으므로 경고에 그친다.
 */
export function formatWarning(provider: string, clientId: string, secret: string): string | null {
  const id = clientId.trim()
  const sec = secret.trim()

  // 브라우저 자동완성이 이메일을 Client ID 칸에 넣는 일이 실제로 있었다.
  // 어느 프로바이더든 Client ID 가 이메일인 경우는 없다.
  if (id.includes('@'))
    return 'Client ID 에 이메일이 들어가 있습니다. 브라우저 자동완성이 채웠을 수 있으니 지우고 콘솔에서 받은 값을 넣으세요.'

  const g = GUIDES[provider]
  if (!g) return null

  if (id && g.idSuffix && !id.endsWith(g.idSuffix))
    return `Client ID 는 보통 ${g.idSuffix} 로 끝납니다. 다른 값을 붙여넣지 않았는지 확인하세요.`

  if (sec && g.secretPrefix && !sec.startsWith(g.secretPrefix))
    return `Client Secret 은 보통 ${g.secretPrefix} 로 시작합니다. 예전에 발급한 것이라면 그대로 두셔도 됩니다.`

  return null
}
