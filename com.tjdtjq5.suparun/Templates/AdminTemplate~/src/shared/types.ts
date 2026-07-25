/** 어드민 서버 응답 타입. 바닐라 쪽과 계약이 같아야 한다. */

export interface AdminUser {
  id: string
  email: string
  /** 'admin' = 승인됨, 'pending' = 가입했으나 승인 대기 */
  role: 'admin' | 'pending'
  created_at: string
}

/** 변경 이력 1건. action 값은 서버가 자유롭게 늘릴 수 있어 string 으로 둔다. */
export interface AuditLog {
  created_at: string
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
}

/**
 * `[Json(typeof(T))]` 의 T 필드 메타. 서버가 `jsonSchema` 로 내려준다.
 * `isJson` + `jsonSchema` 가 또 있으면 중첩 진입이 가능하다 (depth 무제한).
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
}

export interface ConfigType {
  name: string
  tableName: string
  group?: string
  fields: ConfigField[]
}

export type ConfigRow = Record<string, unknown> & { id?: unknown }

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
  /** `[Owner]` 로 선언된 소유자 컬럼(소문자). 없으면 "관리자만" 정책밖에 못 쓴다. */
  owner?: string
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

export interface TableStats {
  sum: number
  avg: number
  max: number
  min: number
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
