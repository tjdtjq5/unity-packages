import { bridge } from './bridge'

/**
 * **Unity 가 실행하는 것들.** 스키마 반영·배포·승격·환경 전환.
 *
 * 왜 브라우저가 직접 못 하나: 전부 로컬 파일과 CLI 를 만진다. gcloud·gh·dotnet 은 이 컴퓨터의
 * 명령이고, 생성된 Id 상수는 이 Unity 프로젝트에 쓰인다. 그래서 버튼은 여기, 손은 저쪽이다.
 *
 * 이 화면들은 원래 Unity 대시보드의 Deploy 탭이었다. 옮기면서 상태의 주인도 바뀌었다 —
 * 예전에는 탭이 들고 OnGUI 가 매 프레임 그렸고, 지금은 브리지가 들고 여기가 폴링한다.
 */

export interface OpsEnv {
  name: string
  /** Supabase 연결이 채워졌는가. 값 자체는 내려오지 않는다. */
  configured: boolean
  projectRef: string
  cloudRunUrl: string
  /** 컴파일 후 이 환경에 스키마 자동 반영. 팀 공유값(Unity 설정 파일, git). */
  autoSchemaSync: boolean
  /** 행 편집 시 Id 상수 자동 생성. 같은 성격의 팀 공유값. */
  autoIdConstants: boolean
}

/** 배포 진행. `phase` 하나로 화면이 갈린다. */
export type DeployPhase =
  | 'idle'
  | 'verifying'
  | 'deploying'
  | 'tracking'
  | 'success'
  | 'failed'
  | 'skipped'

export interface OpsState {
  editorEnv: string
  environments: OpsEnv[]
  /** .NET SDK 유무. 없으면 빌드 검증을 건너뛸지 물어봐야 한다. */
  dotnet: boolean
  deployConfigured: boolean
  schema: { running: boolean; label: string | null; error: string | null }
  deploy: {
    phase: DeployPhase
    message: string | null
    error: string | null
    url: string | null
    /** tracking 중일 때만 채워진다(초). */
    elapsed: number
    actionsUrl: string | null
  }
}

export interface IdGenResult {
  ok: boolean
  fileCount: number
  outputDir: string
  generated: string[]
  errors: string[]
  /** 편집 환경의 자동 생성 토글이 꺼져 있어 건너뛰었다(브리지가 판정). */
  skipped?: boolean
}

export const ops = {
  state: () => bridge.get<OpsState>('/ops/state'),

  // ── 스키마 ──
  // 수동 반영·요약은 없다. 반영 경로는 둘: 자동 켠 환경은 컴파일, 끈 환경은 배포(선반영).
  /** 편집 환경의 컴파일 후 자동 반영 토글. 팀 공유값이라 Unity 설정 파일에 git diff 가 생긴다. */
  setAutoSchema: (enabled: boolean) =>
    bridge.post<{ enabled: boolean }>('/ops/env-auto-schema', { enabled }),

  // ── Id 상수 ──
  // 수동 버튼은 없다 — shared/idsync.ts 의 자동 트리거만 부른다. 토글이 꺼져 있으면
  // 브리지가 skipped 로 답한다.
  idConstants: () => bridge.post<IdGenResult>('/ops/id-constants'),
  setAutoIds: (enabled: boolean) =>
    bridge.post<{ enabled: boolean }>('/ops/env-auto-ids', { enabled }),

  // ── 배포 ──
  /**
   * `skipVerify` 는 **두 번째 클릭에서만** 켠다. .NET SDK 가 없으면 브리지가 412 로 한 번 막고,
   * 사람이 그 뜻을 읽고 다시 누를 때 넘긴다 — 검증을 조용히 건너뛰면 서버 빌드가 GitHub 에서
   * 깨진 뒤에야 알게 된다.
   */
  deploy: (skipVerify = false) => bridge.post<{ started: boolean }>('/ops/deploy', { skipVerify }),
  deployReset: () => bridge.post<{ ok: boolean }>('/ops/deploy-reset'),

  // ── 환경 ──
  // 빌드 환경 지정은 없다 — 빌드 = 편집 환경.
  selectEnv: (name: string) => bridge.post<{ name: string }>('/ops/env-select', { name }),
  addEnv: (name: string) => bridge.post<{ name: string }>('/ops/env-add', { name }),
  removeEnv: (name: string) => bridge.post<{ name: string }>('/ops/env-remove', { name }),
  /**
   * 편집 환경의 이름 변경. 이름의 진실은 **슬롯(Unity 설정)** 이라 브리지가 바꾼다 —
   * 슬롯·해시파일 키·DB(`suparun_env.name`)가 한 번에 갱신된다. DB 만 고치면
   * 카드(DB 이름)와 슬롯(Unity 이름)이 서로 다른 이름을 말하게 된다.
   */
  renameEnv: (to: string) => bridge.post<{ name: string }>('/ops/env-rename', { to }),

  // ── 승격 ──
  promoteSchema: (target: string) =>
    bridge.post<{ started: boolean }>('/ops/promote-schema', { target }),
  promoteData: (target: string) =>
    bridge.post<{ started: boolean }>('/ops/promote-data', { target }),
}

/** 아직 도는 중인가. 폴링을 계속할지 정한다. */
export function opsBusy(s: OpsState): boolean {
  return (
    s.schema.running ||
    s.deploy.phase === 'verifying' ||
    s.deploy.phase === 'deploying' ||
    s.deploy.phase === 'tracking'
  )
}

export function formatElapsed(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = Math.floor(sec % 60)
  return `${m}:${String(s).padStart(2, '0')}`
}
