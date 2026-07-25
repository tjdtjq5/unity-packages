/**
 * 배포 시 치환되는 값들.
 *
 * **플레이스홀더(`{{SUPABASE_URL}}` 등)는 index.html 의 인라인 `<script>` 에만 둘 수 있다.**
 * vite 는 `type="module"` 이 아닌 인라인 스크립트를 번들 대상으로 잡지 않으므로 그 자리에서만
 * 치환이 통과한다. React 번들 안에 쓰면 문자열이 그대로 남아 배포가 조용히 깨진다.
 * 그래서 바닐라가 `window.__SUPARUN_ENV` 로 한 번 내보내고 여기서 읽는다.
 */

export interface SuparunEnv {
  supabaseUrl: string
  supabaseAnonKey: string
  /** OAuth 프로바이더 목록 (`["google","kakao"]`). 비어 있으면 OAuth 섹션을 숨긴다. */
  authProviders: string[]
  /** Supabase 프로젝트 ref (`https://xxx.supabase.co` → `xxx`). 대시보드 링크에 쓴다. */
  projectRef: string
}

const EMPTY: SuparunEnv = {
  supabaseUrl: '',
  supabaseAnonKey: '',
  authProviders: [],
  projectRef: '',
}

declare global {
  interface Window {
    __SUPARUN_ENV?: SuparunEnv
    /** `file://` 또는 `?mock=1` 일 때 켜지는 디자인 미리보기 모드. */
    __SUPARUN_PREVIEW__?: boolean
  }
}

export function env(): SuparunEnv {
  return window.__SUPARUN_ENV ?? EMPTY
}

export function isPreview(): boolean {
  return window.__SUPARUN_PREVIEW__ === true
}
