import { createContext, useContext } from 'react'
import type { ConfigType, FkOption, TableType } from '../../shared/types'
import type { Route } from './route'

/**
 * 껍데기가 소유한 상태를 화면들에게 내려주는 통로.
 * 바닐라 시절 전역이던 `types` / `tableTypes` / `fkSources` / `rewardSources` 와
 * 브릿지의 `setPageSubtitle` / `showPlayer` / `getTableTypes` 가 전부 여기로 모였다.
 */

/** Config 화면이 마운트되어 있는 동안 툴바 버튼이 호출할 액션. */
export interface ToolbarActions {
  addRow(): Promise<void>
  exportData(): Promise<void>
  importData(file: File): Promise<void>
}

export interface AdminContextValue {
  types: ConfigType[]
  tableTypes: TableType[]
  /** `[ForeignKey]` 드롭다운 옵션 (참조 대상 Config 이름 → 행 목록). */
  fkSources: Record<string, FkOption[]>
  /** Rewards 모달용 재화/아이템 목록 (`currency_def`, `inventory_item_def`). */
  rewardSources: Record<string, FkOption[]>
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
