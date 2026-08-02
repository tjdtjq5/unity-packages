import { useState } from 'react'
import { Spinner } from '../../shared/Spinner'
import type { ConfigType, TableType } from '../../shared/types'
import type { Route } from './route'

/**
 * Config 를 사이드바 표시 순서대로 그룹핑한다.
 * `[` `]` 단축키가 이 순서를 따라야 해서 컴포넌트 밖으로 뺐다 —
 * 그룹이 있으면 types 배열 순서와 화면 순서가 어긋난다.
 */
export function groupConfigTypes(types: ConfigType[]): { label: string; items: ConfigType[] }[] {
  const groups = new Map<string, ConfigType[]>()
  const ungrouped: ConfigType[] = []
  for (const t of types) {
    if (t.group) {
      const list = groups.get(t.group)
      if (list) list.push(t)
      else groups.set(t.group, [t])
    } else {
      ungrouped.push(t)
    }
  }
  // Map 이라 삽입 순서가 유지된다(바닐라의 Object.entries 와 동일).
  const out = [...groups.entries()].map(([label, items]) => ({ label, items }))
  if (ungrouped.length) out.push({ label: 'CONFIGS', items: ungrouped })
  return out
}

/**
 * 그룹 접힘 상태. **localStorage 에 남긴다** — 테이블이 18개라 매번 접는 것이 일이 된다.
 * 화면을 옮길 때마다 초기화되면 접는 기능 자체가 쓸모없어진다.
 */
const COLLAPSE_KEY = 'suparun_sidebar_collapsed'

function loadCollapsed(): Set<string> {
  try {
    const raw = localStorage.getItem(COLLAPSE_KEY)
    return new Set(raw ? (JSON.parse(raw) as string[]) : [])
  } catch {
    return new Set()
  }
}

function saveCollapsed(s: Set<string>): void {
  try {
    localStorage.setItem(COLLAPSE_KEY, JSON.stringify([...s]))
  } catch {
    /* 사파리 프라이빗 모드 등 — 접힘이 기억되지 않을 뿐이다 */
  }
}

/** 접을 수 있는 그룹 머리. 열림/닫힘을 삼각형으로 표시한다. */
function GroupHeader({
  label,
  count,
  collapsed,
  onToggle,
}: {
  label: string
  count: number
  collapsed: boolean
  onToggle: () => void
}) {
  return (
    <button className="tree-section toggle" onClick={onToggle} type="button">
      <span className="caret">{collapsed ? '▸' : '▾'}</span>
      <span>{label}</span>
      <span className="tree-count">{count}</span>
    </button>
  )
}

/**
 * Config·Table 사이드바 목록. 바닐라 renderSidebar / renderGroup 을 대체한다.
 * 그룹별 접이식 소제목 + 아이콘 항목 (Metaplay 내비 동형).
 */
export function Sidebar({
  types,
  tableTypes,
  route,
  onNavigate,
  ready,
}: {
  types: ConfigType[]
  tableTypes: TableType[]
  route: Route
  onNavigate: (r: Route) => void
  /** 타입 메타가 도착했는가. 도착 전 빈 트리는 "Config 가 없다"로 읽혀서 로딩을 대신 세운다. */
  ready: boolean
}) {
  const activeConfig = route.kind === 'config' ? route.tableName : null
  const [collapsed, setCollapsed] = useState<Set<string>>(loadCollapsed)

  const toggle = (key: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      saveCollapsed(next)
      return next
    })

  if (!ready) {
    return (
      <div className="tree-loading">
        <Spinner size={14} />
        <span>목록 불러오는 중…</span>
      </div>
    )
  }

  return (
    <>
      {groupConfigTypes(types).map(({ label, items }) => (
        <ConfigGroup
          key={label}
          label={label}
          items={items}
          active={activeConfig}
          collapsed={collapsed.has(label)}
          onToggle={() => toggle(label)}
          onNavigate={onNavigate}
        />
      ))}

      {/* [UserData] 테이블 — 읽기 전용 조회·통계.
          예전에는 여기에 player_search / cross_search 만 있고 개별 테이블 진입점이 없어서
          Table 화면에 아예 닿을 수 없었다. 두 화면을 걷어내면서 제자리를 찾았다. */}
      {tableTypes.length > 0 && (
        <>
          <GroupHeader
            label="TABLES"
            count={tableTypes.length}
            collapsed={collapsed.has('__tables')}
            onToggle={() => toggle('__tables')}
          />
          {!collapsed.has('__tables') && (
            <div className="tree-list">
              {tableTypes.map((t) => (
                <TreeItem
                  key={t.tableName}
                  icon="fa-database"
                  label={t.name}
                  dataType={t.tableName}
                  active={route.kind === 'table' && route.tableName === t.tableName}
                  onClick={() => onNavigate({ kind: 'table', tableName: t.tableName })}
                />
              ))}
            </div>
          )}
        </>
      )}
    </>
  )
}

function ConfigGroup({
  label,
  items,
  active,
  collapsed,
  onToggle,
  onNavigate,
}: {
  label: string
  items: ConfigType[]
  active: string | null
  collapsed: boolean
  onToggle: () => void
  onNavigate: (r: Route) => void
}) {
  // 접혀 있어도 그 안에 현재 화면이 있으면 펼쳐 둔다 — 어디 있는지 모르게 되면 안 된다.
  const hasActive = items.some((t) => t.tableName === active)
  const hidden = collapsed && !hasActive

  return (
    <>
      <GroupHeader
        label={label.toUpperCase()}
        count={items.length}
        collapsed={hidden}
        onToggle={onToggle}
      />
      {!hidden && (
        <div className="tree-list">
          {items.map((t) => (
            <TreeItem
              key={t.tableName}
              icon="fa-table-cells-large"
              label={t.name}
              dataType={t.tableName}
              active={active === t.tableName}
              onClick={() => onNavigate({ kind: 'config', tableName: t.tableName })}
            />
          ))}
        </div>
      )}
    </>
  )
}

/** `data-type` 은 `[` `]` 단축키(cycleConfig)가 순서를 읽는 데 쓴다. */
function TreeItem({
  icon,
  label,
  dataType,
  active,
  onClick,
}: {
  icon: string
  label: string
  dataType?: string
  active: boolean
  onClick: () => void
}) {
  return (
    <a
      className={`tree-item${active ? ' active' : ''}`}
      href="#"
      data-type={dataType}
      onClick={(e) => {
        e.preventDefault()
        onClick()
      }}
    >
      <i className={`fa-solid ${icon}`} />
      <span className="label">{label}</span>
    </a>
  )
}
