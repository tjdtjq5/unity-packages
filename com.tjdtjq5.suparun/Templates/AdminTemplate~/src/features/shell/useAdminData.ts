import { useEffect, useState } from 'react'
import { configApi, tableApi } from '../../shared/api'
import type { ConfigField, ConfigType, FkOption, JsonSchemaField, TableType } from '../../shared/types'

/**
 * 어드민 진입 시 1회 로드되는 메타/참조 데이터.
 * 바닐라 showAdmin() + loadRewardSources() 를 대체한다.
 */
export interface AdminData {
  types: ConfigType[]
  tableTypes: TableType[]
  fkSources: Record<string, FkOption[]>
  rewardSources: Record<string, FkOption[]>
  /** 타입 목록 로드가 끝났는가. 라우트 복원은 이게 true 가 된 뒤라야 한다. */
  ready: boolean
}

const EMPTY: AdminData = { types: [], tableTypes: [], fkSources: {}, rewardSources: {}, ready: false }

/**
 * FK 참조 대상 수집 — 평면 컬럼과 nested jsonSchema 를 모두 훑는다.
 * nested 를 빠뜨리면 JSON 모달 안 FK 드롭다운이 빈 채로 뜬다.
 */
function collectFkTargets(fields: (ConfigField | JsonSchemaField)[] | undefined, out: Set<string>): void {
  for (const f of fields || []) {
    if (f.foreignKey) out.add(f.foreignKey)
    if ('foreignKeyList' in f && f.foreignKeyList) out.add(f.foreignKeyList)
    if (f.isJson && f.jsonSchema) collectFkTargets(f.jsonSchema, out)
  }
}

export function useAdminData(): AdminData {
  const [data, setData] = useState<AdminData>(EMPTY)

  useEffect(() => {
    let alive = true

    void (async () => {
      // 타입 목록 — 실패해도 빈 목록으로 진행한다(바닐라 동일).
      // 401/403 은 api() 안에서 로그인 화면 복귀로 처리되므로 여기서 따로 다루지 않는다.
      let types: ConfigType[] = []
      try {
        types = await configApi<ConfigType[]>('/_types')
      } catch {
        types = []
      }
      let tableTypes: TableType[] = []
      try {
        tableTypes = await tableApi<TableType[]>('/_types')
      } catch {
        tableTypes = []
      }
      if (!alive) return
      // 사이드바를 먼저 띄우고 참조 데이터는 뒤따라 채운다.
      setData((d) => ({ ...d, types, tableTypes, ready: true }))

      const rewardSources: Record<string, FkOption[]> = {}
      for (const t of types) {
        if (t.tableName === 'currency_def' || t.tableName === 'inventory_item_def') {
          try {
            rewardSources[t.tableName] = await configApi<FkOption[]>(`/${t.tableName}`)
          } catch {
            /* 없는 테이블이면 그냥 비워둔다 */
          }
        }
      }

      const targets = new Set<string>()
      for (const t of types) collectFkTargets(t.fields, targets)
      const fkSources: Record<string, FkOption[]> = {}
      await Promise.all(
        [...targets].map(async (name) => {
          const t = types.find((x) => x.name === name)
          if (!t) return
          try {
            fkSources[name] = await configApi<FkOption[]>(`/${t.tableName}`)
          } catch {
            /* 참조 대상이 사라졌을 수 있다 — 드롭다운만 비고 값은 보존된다 */
          }
        }),
      )

      if (alive) setData((d) => ({ ...d, fkSources, rewardSources }))
    })()

    return () => {
      alive = false
    }
  }, [])

  return data
}
