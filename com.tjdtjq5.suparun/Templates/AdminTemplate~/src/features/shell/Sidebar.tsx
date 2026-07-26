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
 * ASCII tree 사이드바. 바닐라 renderSidebar / renderGroup 을 대체한다.
 * 그룹별 `[SECTION]` 헤더 + `├─`/`└─` 분기(마지막 항목만 `└─`).
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
          onNavigate={onNavigate}
        />
      ))}

      {/* [UserData] 테이블 — 읽기 전용 조회·통계.
          예전에는 여기에 player_search / cross_search 만 있고 개별 테이블 진입점이 없어서
          Table 화면에 아예 닿을 수 없었다. 두 화면을 걷어내면서 제자리를 찾았다. */}
      {tableTypes.length > 0 && (
        <>
          <div className="tree-section">[TABLES]</div>
          <div className="tree-list">
            {tableTypes.map((t, i) => (
              <TreeItem
                key={t.tableName}
                branch={i === tableTypes.length - 1 ? '└─' : '├─'}
                label={t.name}
                dataType={t.tableName}
                active={route.kind === 'table' && route.tableName === t.tableName}
                onClick={() => onNavigate({ kind: 'table', tableName: t.tableName })}
              />
            ))}
          </div>
        </>
      )}
    </>
  )
}

function ConfigGroup({
  label,
  items,
  active,
  onNavigate,
}: {
  label: string
  items: ConfigType[]
  active: string | null
  onNavigate: (r: Route) => void
}) {
  return (
    <>
      <div className="tree-section">[{label.toUpperCase()}]</div>
      <div className="tree-list">
        {items.map((t, i) => (
          <TreeItem
            key={t.tableName}
            branch={i === items.length - 1 ? '└─' : '├─'}
            label={t.name}
            dataType={t.tableName}
            active={active === t.tableName}
            onClick={() => onNavigate({ kind: 'config', tableName: t.tableName })}
          />
        ))}
      </div>
    </>
  )
}

/** `data-type` 은 `[` `]` 단축키(cycleConfig)가 순서를 읽는 데 쓴다. */
function TreeItem({
  branch,
  label,
  dataType,
  active,
  onClick,
}: {
  branch: string
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
      <span className="branch">{branch}</span>
      <span className="label">{label}</span>
    </a>
  )
}
