import { createContext, useContext } from 'react'
import type { ConfigType, FkOption, TypeCatalog, TableType } from '../../shared/types'
import type { Route } from './route'

/**
 * 껍데기가 소유한 상태를 화면들에게 내려주는 통로.
 * 바닐라 시절 전역이던 `types` / `tableTypes` / `fkSources` / `rewardSources` 와
 * 브릿지의 `setPageSubtitle` / `showPlayer` / `getTableTypes` 가 전부 여기로 모였다.
 */

/**
 * Config 화면이 마운트되어 있는 동안 툴바 버튼이 호출할 액션.
 * 가져오기는 없다 — ADR-0004 결정 9 로 제거했다(스냅샷으로 대체 예정).
 * addRow 가 없으면 추가 버튼 자체가 안 그려진다 — game-viewer 의 쓰기 UI 거부 (#24).
 */
export interface ToolbarActions {
  addRow?: () => Promise<void>
  exportData(): Promise<void>
}

export interface AdminContextValue {
  types: ConfigType[]
  tableTypes: TableType[]
  /**
   * 쓰기 조작을 그릴 것인가 = game-admin 롤 보유 (#24).
   * UI 겹일 뿐이다 — 진짜 거부는 RLS(is_admin)가 한다. UI 만 뚫어도 저장은 조용히 실패한다.
   */
  canWrite: boolean
  /**
   * 승격 전용 환경인가 (#50, ADR-0010 결정 7 — 이름 규약 prod). 참이면 config 행 편집 UI 를
   * 걷어내고 승격 경로를 안내한다. RLS(admin_write 의 suparun_is_promote_only)가 2겹째다.
   */
  promoteOnly: boolean
  /**
   * 로그인 신원의 롤 전체 (#38 — CS 액션 게이트가 cs 계열을 본다).
   * canWrite 는 이 목록의 요약(game-admin 보유)일 뿐이다. UI 겹 — 진짜 거부는 서버가 한다.
   */
  roles: string[]
  /** `[ForeignKey]` 드롭다운 옵션 (참조 대상 Config 이름 → 행 목록). */
  fkSources: Record<string, FkOption[]>
  /** Rewards 모달용 재화/아이템 목록 (`currency_def`, `inventory_item_def`). */
  rewardSources: Record<string, FkOption[]>
  /** `[NodeGraph]` 컬럼이 여는 캔버스의 팔레트 (컨텍스트 이름 → 노드 목록). */
  typeCatalog: TypeCatalog
  /** 페이지 제목 옆 부제. 화면이 건수 같은 자기 상태를 알릴 때 쓴다. */
  setPageSubtitle(text: string): void
  navigate(route: Route): void
  /**
   * 툴바 액션 등록/해제. 툴바는 껍데기(page-header)에 있는데 동작은 Config 화면이
   * 알고 있어서 생기는 통로다 — 바닐라의 `window.__suparunConfigActions` 와 같은 역할.
   */
  setToolbarActions(actions: ToolbarActions | null): void
}

const Ctx = createContext<AdminContextValue | null>(null)

export const AdminProvider = Ctx.Provider

export function useAdmin(): AdminContextValue {
  const v = useContext(Ctx)
  if (!v) throw new Error('[suparun-admin] useAdmin 은 Shell 안에서만 쓸 수 있습니다.')
  return v
}
