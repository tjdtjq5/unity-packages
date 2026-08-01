/** 어드민 서버 응답 타입. 바닐라 쪽과 계약이 같아야 한다. */

/** 어드민 명단 한 줄 — 신원만 있다. 롤은 `AdminUserRole` 매핑(합집합)이다 (ADR-0009, #24). */
export interface AdminUser {
  id: string
  user_id: string | null
  email: string | null
  /**
   * 어느 프로바이더로 들어온 계정인가.
   * 같은 이메일이라도 프로바이더가 다르면 Supabase 에서 **다른 사용자**라 승인도 따로 받는다.
   * 옛 행은 비어 있을 수 있다(그 계정이 다시 로그인하면 채워진다).
   */
  provider?: string | null
  created_at: number
  created_by?: string | null
}

/** 빌트인 4롤 (ADR-0008 결정 7 — Metaplay 등가). */
export const BUILTIN_ROLES = ['game-admin', 'game-viewer', 'cs-senior', 'cs-agent'] as const
export type BuiltinRole = (typeof BUILTIN_ROLES)[number]

/** user↔role 매핑 한 줄. */
export interface AdminUserRole {
  user_id: string
  role: string
  granted_at: number
  granted_by?: string | null
}

/** 감사 이벤트 1건. action 값은 쓰는 쪽이 자유롭게 늘릴 수 있어 string 으로 둔다. */
export interface AuditLog {
  id: string
  /** epoch ms (BIGINT). */
  created_at: number
  action: string
  config_type?: string | null
  row_id?: string | null
  admin_id?: string | null
  before_json?: string | null
  after_json?: string | null
}

// ── Config (CRUD) ──────────────────────────────────────────

/** `[VisibleIf]` / `[HiddenIf]` 조건. values 가 비면 "truthy 여부"로 판정한다. */
export interface FieldCondition {
  field: string
  values?: string[]
}

export interface ConfigField {
  name: string
  type: string
  isPrimaryKey?: boolean
  isRequired?: boolean
  isHidden?: boolean
  isSortOrder?: boolean
  isEnum?: boolean
  enumValues?: string[]
  isJson?: boolean
  jsonSchema?: JsonSchemaField[]
  foreignKey?: string
  foreignKeyList?: string
  iconAtlas?: string
  componentType?: string
  visibleIf?: FieldCondition
  hiddenIf?: FieldCondition
  /** `[NodeGraph(typeof(TCtx))]` — 이 컬럼은 노드 캔버스로 연다. 값은 type_catalog 의 그룹 키. */
  nodeGraph?: string
  /** `[Polymorphic(typeof(TBase))]` — 타입 드롭다운 + 그 타입의 필드 폼으로 연다. 값은 type_catalog 의 그룹 키. */
  polymorphic?: string
  /**
   * 코드에 적힌 필드 초기값(`public float search_range = 10f;`).
   * 카탈로그 항목(노드·다형 타입)에만 실린다 — 새 값을 만들 때 이걸로 시작한다.
   */
  default?: unknown
  /** `NodeValue<T>` — 상수 대신 Pure 노드 출력을 꽂을 수 있는 칸(노드 안에서만 나온다). */
  isNodeValue?: boolean
}

/**
 * `[Json(typeof(T))]` 의 T 필드 메타. 서버가 `jsonSchema` 로 내려준다.
 * `isJson` + `jsonSchema` 가 또 있으면 중첩 진입이 가능하다 (depth 무제한).
 *
 * 카탈로그 항목(노드·다형 파생)의 필드도 이 모양으로 그려진다 —
 * `[Polymorphic]` 은 어느 깊이에나 올 수 있어 여기에도 있어야 한다
 * (예: PerkData.activation → tiers → pattern).
 */
export interface JsonSchemaField {
  name: string
  type: string
  isEnum?: boolean
  enumValues?: string[]
  isJson?: boolean
  jsonSchema?: JsonSchemaField[]
  foreignKey?: string
  iconAtlas?: string
  componentType?: string
  visibleIf?: FieldCondition
  hiddenIf?: FieldCondition
  /** `[Polymorphic(typeof(TBase))]` — 값은 type_catalog 의 그룹 키. */
  polymorphic?: string
  /** 코드에 적힌 필드 초기값. 새 값을 만들 때 이걸로 시작한다. */
  default?: unknown
}

export interface ConfigType {
  name: string
  tableName: string
  group?: string
  fields: ConfigField[]
}

export type ConfigRow = Record<string, unknown> & { id?: unknown }

// ── 타입 카탈로그 (ADR-0002 노드 그래프 / ADR-0005 다형 필드) ─────────
//
// 둘은 같은 것이다 — "base 의 파생 중 하나를 고르고 필드를 채운다".
// 노드 그래프는 거기에 연결을 얹은 것이라 `outs` 를 쓰고, 다형 필드는 `outs` 가 빈 배열이다.

/**
 * 노드가 캔버스에서 어떻게 그려지는지를 가르는 역할.
 * C# 상속 계층(`ActionNode<T>` 등)에서 그대로 뽑아낸 값이다.
 * 다형 필드에는 해당 계층이 없어 `node` 로 온다.
 */
export type NodeRole = 'entry' | 'action' | 'branch' | 'sequence' | 'loop' | 'pure' | 'flow' | 'node'

/** 나가는 실행 포트. `list` 면 개수가 가변이다(SequenceNode.steps). */
export interface NodePort {
  name: string
  label: string
  list?: boolean
}

/** 팔레트에 뜨는 노드 1종. `fields` 는 표 컬럼과 같은 메타라 렌더러를 공유한다. */
export interface NodeSpec {
  type: string
  label: string
  role: NodeRole
  /** PureNode 의 출력 타입. 이 값이 맞는 칸에만 꽂을 수 있다. */
  outType?: string
  fields: ConfigField[]
  outs: NodePort[]
}

/** 컨텍스트 이름 → 그 그래프에 놓을 수 있는 노드들. */
export type TypeCatalog = Record<string, NodeSpec[]>

/** 컬럼에 저장되는 그래프. 인덱스가 곧 연결이다. */
export interface GraphDoc {
  nodes: GraphNodeData[]
  entry: number
  /** 캔버스 좌표. 실행에 영향이 없어 Unity 는 무시한다. */
  layout?: { x: number; y: number }[]
}

/** 노드 1개. `type` 외의 키는 전부 그 노드의 필드·포트 값이다. */
export type GraphNodeData = Record<string, unknown> & { type: string }

/** `NodeValue<T>` 의 연결 형태. 상수면 이 모양이 아니라 값이 그대로 들어간다. */
export interface NodeValueLink {
  $node: number
}

/** FK 드롭다운 옵션. 서버 `_types` 응답의 fkSources 항목. */
export interface FkOption {
  id: string
  name: string
}

// ── Table (읽기 전용 + 분석) ────────────────────────────────

export interface TableField {
  name: string
  type: string
  isPrimaryKey?: boolean
  isHidden?: boolean
  isEnum?: boolean
}

export interface TableType {
  name: string
  tableName: string
  fields: TableField[]
}

export type TableRow = Record<string, unknown>

export interface TableData {
  rows: TableRow[]
  total: number
}

export type FilterOp = '=' | '>' | '>=' | '<' | '<=' | 'like'

export interface TableFilter {
  field: string
  op: FilterOp
  value: string
}

/**
 * 합계·평균은 없다 (ADR-0004 결정 8).
 * PostgREST 가 집계 함수를 막아(`PGRST123`) 전체 스캔 없이는 구할 수 없는데,
 * 그것 하나 때문에 SQL 함수를 만들거나 max_rows 를 건드릴 만한 가치가 없다.
 * 건수·최소·최대와 분포 차트는 소량 수신으로 **정확하게** 얻을 수 있다.
 */
export interface TableStats {
  max: number | null
  min: number | null
  count: number
}

export interface DistBucket {
  min: number
  max: number
  count: number
}

// ── 크로스 검색 ─────────────────────────────────────────────

export interface CrossCondition {
  table: string
  field: string
  op: string
  value: string
}

export interface CrossResult {
  count: number
  userIds?: string[]
  /** userId → tableName → { column: value } */
  details?: Record<string, Record<string, Record<string, unknown>>>
}

// ── 플레이어 관리 ───────────────────────────────────────────

export interface PlayerData {
  tables: Record<string, TableRow[]>
}
