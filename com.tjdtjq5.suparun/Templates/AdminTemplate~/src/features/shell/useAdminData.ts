import { useEffect, useState } from 'react'
import { selectAll } from '../../shared/db'
import { loadMeta } from '../../shared/meta'
import type {
  ConfigField,
  ConfigType,
  FkOption,
  JsonSchemaField,
  TypeCatalog,
  TableType,
} from '../../shared/types'

/**
 * 어드민 진입 시 1회 로드되는 메타/참조 데이터.
 * 바닐라 showAdmin() + loadRewardSources() 를 대체한다.
 */
export interface AdminData {
  types: ConfigType[]
  tableTypes: TableType[]
  fkSources: Record<string, FkOption[]>
  rewardSources: Record<string, FkOption[]>
  /** 컨텍스트별 노드 팔레트. `[NodeGraph]` 컬럼이 없으면 빈 객체다. */
  typeCatalog: TypeCatalog
  /** 타입 목록 로드가 끝났는가. 라우트 복원은 이게 true 가 된 뒤라야 한다. */
  ready: boolean
}

const EMPTY: AdminData = {
  types: [],
  tableTypes: [],
  fkSources: {},
  rewardSources: {},
  typeCatalog: {},
  ready: false,
}

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
      // 타입 메타 — 서버 /_types 가 아니라 suparun_meta 에서 읽는다 (ADR-0004).
      // Unity 가 컴파일할 때 밀어 넣은 것이라 서버 재배포 없이 최신이다.
      const meta = await loadMeta(['config_types', 'table_types', 'type_catalog'])
      const types = (meta.config_types as ConfigType[] | undefined) ?? []
      const tableTypes = (meta.table_types as TableType[] | undefined) ?? []
      const typeCatalog = (meta.type_catalog as TypeCatalog | undefined) ?? {}
      if (!alive) return
      // 사이드바를 먼저 띄우고 참조 데이터는 뒤따라 채운다.
      setData((d) => ({ ...d, types, tableTypes, typeCatalog, ready: true }))

      const rewardSources: Record<string, FkOption[]> = {}
      for (const t of types) {
        if (t.tableName === 'currency_def' || t.tableName === 'inventory_item_def') {
          try {
            rewardSources[t.tableName] = await selectAll<FkOption>(t.tableName)
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
            fkSources[name] = await selectAll<FkOption>(t.tableName)
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
