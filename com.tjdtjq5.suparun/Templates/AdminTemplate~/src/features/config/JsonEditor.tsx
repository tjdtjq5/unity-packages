import { useEffect, useRef, useState } from 'react'
import { Modal } from '../../shared/Modal'
import { enableColResize } from '../../shared/colResize'
import { toast } from '../../shared/toast'
import { useAdmin } from '../shell/AdminContext'
import type { JsonSchemaField } from '../../shared/types'
import { IconCell } from './IconPicker'
import { SearchSelect } from './SearchSelect'
import { isConditionDisabled } from './fieldVisibility'
import { useComponentMap } from './useLazyMaps'

type Item = Record<string, unknown>

interface Layer {
  schema: JsonSchemaField[]
  items: Item[]
  label: string
  /** 자식 layer 를 부모 item 의 어느 필드에 되돌려 쓸지 */
  parentIndex?: number
  parentField?: string
  /**
   * 다형 layer 면 base 이름. 이때 items 는 항상 1개이고 표 대신 타입 드롭다운 + 세로 폼으로 그린다.
   * 접을 때도 배열이 아니라 객체 하나로 접힌다.
   */
  polyBase?: string
}

/** 스키마가 없을 때 첫 항목에서 컬럼을 추론한다 (바닐라 detectSchema). */
function detectSchema(obj: Item): JsonSchemaField[] {
  return Object.keys(obj).map((name) => {
    const v = obj[name]
    let type = 'string'
    if (typeof v === 'number') type = Number.isInteger(v) ? 'int' : 'float'
    else if (typeof v === 'boolean') type = 'bool'
    return { name, type }
  })
}

function parseArray(raw: unknown): Item[] {
  if (raw == null || raw === '') return []
  try {
    const parsed: unknown = typeof raw === 'string' ? JSON.parse(raw) : raw
    return Array.isArray(parsed) ? (parsed as Item[]) : []
  } catch {
    return []
  }
}

export function countJsonItems(raw: unknown): number {
  return parseArray(raw).length
}

/** 다형 값 `{"type":"X",…}` 에서 타입명만. 비어 있으면 그렇게 표시한다. */
export function describePolyValue(json: string): string {
  if (!json || !json.trim()) return '(비어 있음)'
  try {
    const obj = JSON.parse(json) as Record<string, unknown>
    return typeof obj.type === 'string' && obj.type ? obj.type : '(비어 있음)'
  } catch {
    return '(비어 있음)'
  }
}

/** 다형 값을 타입명과 나머지 필드로 가른다. */
export function splitPolyValue(json: string): { type: string; values: Record<string, unknown> } {
  if (!json || !json.trim()) return { type: '', values: {} }
  try {
    const obj = JSON.parse(json) as Record<string, unknown>
    const { type, ...rest } = obj
    return { type: typeof type === 'string' ? type : '', values: rest }
  } catch {
    return { type: '', values: {} }
  }
}

/** 셀 배지 문구. 바닐라 formatJsonArray 와 동일. */
export function formatJsonArray(json: unknown): string {
  const s = String(json ?? '')
  if (!s) return '(비어있음)'
  try {
    const a: unknown = JSON.parse(s)
    if (!Array.isArray(a) || a.length === 0) return '(비어있음)'
    return `${a.length}개 항목`
  } catch {
    return s.slice(0, 30) + '...'
  }
}

/**
 * 범용 JSON 배열 에디터. 바닐라의 jsonEditorStack 기반 모달을 옮긴 것이다.
 *
 * 스택 구조는 그대로 유지한다 — 중첩 depth 가 무제한이라 재귀 컴포넌트보다
 * "현재 layer 하나만 그리고 breadcrumb 로 오간다"는 원래 UX 가 맞다.
 *
 * 바닐라는 `collectJsonEditorRows()` 로 매번 DOM 에서 값을 다시 긁어모았다.
 * 여기서는 items 가 곧 진실이라 그 단계가 사라진다.
 */
export function JsonEditorModal({
  title,
  rootLabel,
  schema,
  initialJson,
  polyBase,
  onSave,
  onClose,
}: {
  title: string
  rootLabel: string
  schema: JsonSchemaField[]
  initialJson: unknown
  /** 주면 첫 layer 가 배열 표가 아니라 다형 폼이 된다. */
  polyBase?: string
  onSave: (json: string) => Promise<void> | void
  onClose: () => void
}) {
  const [layers, setLayers] = useState<Layer[]>(() => {
    if (polyBase) {
      const { type, values } = splitPolyValue(String(initialJson ?? ''))
      return [{ schema: [], items: [{ ...values, type }], label: rootLabel, polyBase }]
    }
    const items = parseArray(initialJson)
    return [
      {
        schema: schema.length > 0 ? schema : items.length > 0 ? detectSchema(items[0]) : [],
        items,
        label: rootLabel,
      },
    ]
  })
  const [saving, setSaving] = useState(false)
  const hostRef = useRef<HTMLDivElement>(null)

  const cur = layers[layers.length - 1]

  // 컬럼 폭·wrap — 표 화면에만. 폭은 렌더 결과를 재야 정해져 DOM 유틸로 붙인다 (Config 표와 같은 방식).
  // 저장 키를 스키마의 필드 이름으로 잡는 이유는 layer 깊이가 아니라 "무슨 표인지" 가 기준이어야
  // 같은 표를 어디서 열든 폭이 유지되기 때문이다.
  const schemaKey = cur.schema.map((f) => f.name).join(',')
  useEffect(() => {
    if (cur.polyBase || !hostRef.current || cur.schema.length === 0) return
    // DOM 컬럼 순서는 [...schema, 삭제버튼] 이라 끝에 null 패딩을 둬야 버튼 칸이 폭을 유지한다.
    enableColResize(hostRef.current, 'json_' + schemaKey, {
      fields: [...cur.schema, null],
      data: cur.items,
    })
  }, [cur.polyBase, schemaKey, cur.items, cur.schema])

  function patchItem(i: number, key: string, value: unknown) {
    setLayers((prev) => {
      const next = [...prev]
      const top = { ...next[next.length - 1] }
      top.items = top.items.map((it, idx) => (idx === i ? { ...it, [key]: value } : it))
      next[next.length - 1] = top
      return next
    })
  }

  function addRow() {
    setLayers((prev) => {
      const next = [...prev]
      const top = { ...next[next.length - 1] }
      const blank: Item = {}
      for (const s of top.schema) {
        blank[s.name] = s.type === 'bool' ? false : s.type === 'int' || s.type === 'float' ? 0 : ''
      }
      top.items = [...top.items, blank]
      next[next.length - 1] = top
      return next
    })
  }

  function removeRow(i: number) {
    setLayers((prev) => {
      const next = [...prev]
      const top = { ...next[next.length - 1] }
      top.items = top.items.filter((_, idx) => idx !== i)
      next[next.length - 1] = top
      return next
    })
  }

  /** 중첩 자식 layer 진입 — 다형이면 폼 layer, 아니면 배열 표 layer. */
  function enterNested(rowIndex: number, field: JsonSchemaField) {
    const raw = cur.items[rowIndex]?.[field.name]
    const label = `${cur.label} > ${cur.polyBase ? '' : `[${rowIndex + 1}].`}${field.name}`

    if (field.polymorphic) {
      const { type, values } = splitPolyValue(String(raw ?? ''))
      setLayers((prev) => [
        ...prev,
        {
          schema: [],                       // 타입을 고르면 그때 채운다
          items: [{ ...values, type }],
          label,
          parentIndex: rowIndex,
          parentField: field.name,
          polyBase: field.polymorphic,
        },
      ])
      return
    }

    const childItems = parseArray(raw)
    const childSchema =
      field.jsonSchema && field.jsonSchema.length > 0
        ? field.jsonSchema
        : childItems.length > 0
          ? detectSchema(childItems[0])
          : []
    setLayers((prev) => [
      ...prev,
      {
        schema: childSchema,
        items: childItems,
        label,
        parentIndex: rowIndex,
        parentField: field.name,
      },
    ])
  }

  /**
   * 자식 layer 를 부모 item 에 문자열로 접어 넣는다 (서버 왕복 형식과 동일).
   * 배열 layer 는 배열로, 다형 layer 는 객체 하나로 접는다.
   */
  function foldTop(stack: Layer[]): Layer[] {
    if (stack.length <= 1) return stack
    const child = stack[stack.length - 1]
    const parent = { ...stack[stack.length - 2] }
    if (child.parentIndex != null && child.parentField) {
      const folded = child.polyBase ? foldPoly(child) : JSON.stringify(child.items)
      parent.items = parent.items.map((it, idx) =>
        idx === child.parentIndex ? { ...it, [child.parentField!]: folded } : it,
      )
    }
    return [...stack.slice(0, -2), parent]
  }

  /** 다형 layer → `{"type":"X",…}`. 타입이 비었으면 값 자체를 비운다. */
  function foldPoly(layer: Layer): string {
    const v = layer.items[0] ?? {}
    const type = String(v.type ?? '')
    if (!type) return ''
    const { type: _drop, ...rest } = v
    return JSON.stringify({ type, ...rest })
  }

  function goBack() {
    setLayers((prev) => foldTop(prev))
  }

  async function save() {
    setSaving(true)
    try {
      let stack = layers
      while (stack.length > 1) stack = foldTop(stack)
      await onSave(stack[0].polyBase ? foldPoly(stack[0]) : JSON.stringify(stack[0].items))
      onClose()
    } catch (e) {
      toast('저장 실패: ' + (e instanceof Error ? e.message : String(e)), 'error')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      onClose={onClose}
      maxWidth={900}
      title={<span className="fw-bold px-2">{title}</span>}
      footer={
        <div className="d-flex justify-content-end gap-2 p-3 border-top">
          <button className="btn btn-outline-secondary" onClick={onClose}>
            취소
          </button>
          <button className="btn btn-primary" disabled={saving} onClick={() => void save()}>
            {saving ? '저장 중…' : '저장'}
          </button>
        </div>
      }
    >
      <div ref={hostRef} style={{ padding: 12, maxHeight: '70vh', overflow: 'auto' }}>
          {/* breadcrumb — 중첩 진입 시에만 뒤로가기가 의미 있다 */}
          <div className="d-flex align-items-center gap-2 mb-2">
            {layers.length > 1 && (
              <button className="btn btn-sm btn-outline-secondary" onClick={goBack}>
                <i className="ti ti-arrow-left me-1" />
                뒤로
              </button>
            )}
            <span className="text-muted small">{cur.label}</span>
          </div>

          {cur.polyBase ? (
            <PolymorphicForm
              base={cur.polyBase}
              value={cur.items[0] ?? {}}
              onChange={(next) =>
                setLayers((prev) => {
                  const stack = [...prev]
                  stack[stack.length - 1] = { ...stack[stack.length - 1], items: [next] }
                  return stack
                })
              }
              onEnterNested={(field) => enterNested(0, field)}
            />
          ) : cur.schema.length === 0 ? (
            <div className="empty-state">
              <i className="ti ti-code-off" />
              <h3>스키마가 없습니다</h3>
              <p>서버 메타데이터에 jsonSchema 가 없고 기존 항목도 비어 있어 컬럼을 추론할 수 없습니다.</p>
            </div>
          ) : (
            <table className="table table-vcenter card-table table-striped">
              <thead>
                <tr>
                  {cur.schema.map((s) => (
                    <th key={s.name}>{s.name}</th>
                  ))}
                  <th style={{ width: 40 }} />
                </tr>
              </thead>
              <tbody>
                {cur.items.map((item, i) => (
                  <tr key={i}>
                    {cur.schema.map((s) => (
                      <JsonCell
                        key={s.name}
                        item={item}
                        field={s}
                        onChange={(v) => patchItem(i, s.name, v)}
                        onEnterNested={() => enterNested(i, s)}
                      />
                    ))}
                    <td>
                      <button
                        className="btn btn-ghost-danger btn-icon btn-sm"
                        title="삭제"
                        onClick={() => removeRow(i)}
                      >
                        <i className="ti ti-trash" />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {!cur.polyBase && (
            <button className="btn btn-sm btn-outline-primary mt-2" onClick={addRow}>
              <i className="ti ti-plus me-1" />행 추가
            </button>
          )}
      </div>
    </Modal>
  )
}

/**
 * 다형 값 하나를 그린다 — 타입 드롭다운 + 그 타입의 필드 폼.
 *
 * 모달을 갖지 않는다. 진입점이 둘이기 때문이다 —
 * 표의 셀에서 바로 열리기도 하고(PolymorphicEditor), 중첩 layer 로 들어오기도 한다(JsonEditorModal).
 *
 * `value` 는 `{ type, ...필드 }` 한 덩어리다. type 이 곧 어떤 파생인지다.
 */
export function PolymorphicForm({
  base,
  value,
  onChange,
  onEnterNested,
}: {
  base: string
  value: Record<string, unknown>
  onChange: (next: Record<string, unknown>) => void
  onEnterNested: (field: JsonSchemaField) => void
}) {
  const specs = useAdmin().typeCatalog[base] ?? []
  const typeName = String(value.type ?? '')
  const spec = specs.find((s) => s.type === typeName)

  const changeType = (next: string) => {
    // 값은 이어받지 않는다 — 이름이 같아도 타입이 다르면 뜻이 다르다.
    onChange(next ? { type: next, ...defaultsOf(specs.find((s) => s.type === next)) } : {})
  }

  return (
    <>
      <div className="poly-type">
        <label>종류</label>
        <select
          className="form-select form-select-sm"
          value={typeName}
          onChange={(e) => changeType(e.target.value)}
        >
          <option value="">(비어 있음)</option>
          {specs.map((s) => (
            <option key={s.type} value={s.type}>
              {s.label || s.type}
            </option>
          ))}
        </select>
      </div>

      {specs.length === 0 && (
        <div className="poly-unknown">
          `{base}` 가 카탈로그에 없습니다. 클래스가 지워졌거나 이름이 바뀌었을 수 있습니다.
        </div>
      )}

      {typeName && !spec && specs.length > 0 && (
        <div className="poly-unknown">
          `{typeName}` 은 카탈로그에 없습니다. 클래스가 지워졌거나 이름이 바뀌었을 수 있습니다 —
          저장하면 이 값이 그대로 유지됩니다.
        </div>
      )}

      {spec && spec.fields.length === 0 && <div className="poly-empty">채울 값이 없습니다.</div>}

      {spec && spec.fields.length > 0 && (
        <table className="table table-sm poly-fields">
          <tbody>
            {spec.fields.map((f) => (
              <tr key={f.name}>
                <th title={`${f.name} · ${f.type}`}>{f.name}</th>
                <JsonCell
                  item={value}
                  field={f}
                  onChange={(v) => onChange({ ...value, [f.name]: v })}
                  onEnterNested={() => onEnterNested(f)}
                />
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </>
  )
}

/**
 * 새 타입을 고르면 그 타입의 기본값으로 시작한다.
 * 코드에 적힌 초기값(`default`)이 있으면 그걸 쓴다 — 없으면 타입별 빈 값이다.
 */
function defaultsOf(spec: { fields: JsonSchemaField[] } | undefined): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const f of spec?.fields ?? []) {
    if (f.default !== undefined) out[f.name] = f.default
    else if (f.type === 'bool') out[f.name] = false
    else if (f.type === 'int' || f.type === 'long' || f.type === 'number') out[f.name] = 0
    else if (f.isEnum && f.enumValues?.length) out[f.name] = f.enumValues[0]
    else out[f.name] = ''
  }
  return out
}

/**
 * 스키마 필드 하나를 `<td>` 로 그린다. FK·아이콘·컴포넌트·enum·bool·숫자를 전부 다룬다.
 *
 * `[Polymorphic]` 편집기도 이걸 쓴다 — 거기서는 행 하나짜리 세로 표라
 * `<tr><th>이름</th><JsonCell/></tr>` 형태가 된다.
 */
export function JsonCell({
  item,
  field,
  onChange,
  onEnterNested,
}: {
  item: Item
  field: JsonSchemaField
  onChange: (v: unknown) => void
  onEnterNested: () => void
}) {
  const fkSources = useAdmin().fkSources
  const componentMap = useComponentMap(Boolean(field.componentType))
  const val = item[field.name]
  const shown = String(val ?? '')

  if (isConditionDisabled(item, field.visibleIf, field.hiddenIf)) {
    return (
      <td>
        <span className="cell-na">—</span>
      </td>
    )
  }

  if (field.foreignKey && fkSources[field.foreignKey]) {
    return (
      <td>
        <SearchSelect options={fkSources[field.foreignKey]} value={shown} onChange={onChange} />
      </td>
    )
  }

  // 다형 필드 — 중첩 JSON 과 같은 신호를 보낸다. 어느 쪽으로 들어갈지는 부모가 안다.
  if (field.polymorphic) {
    return (
      <td>
        <span className="badge bg-orange-lt json-badge" onClick={onEnterNested}>
          <i className="ti ti-category me-1" />
          {describePolyValue(shown)}
        </span>
      </td>
    )
  }

  if (field.iconAtlas) {
    return (
      <td>
        <IconCell atlas={field.iconAtlas} value={shown} onChange={onChange} />
      </td>
    )
  }

  if (field.componentType) {
    return (
      <td>
        <SearchSelect
          options={(componentMap?.[field.componentType] ?? []).map((a) => ({ id: a }))}
          value={shown}
          onChange={onChange}
        />
      </td>
    )
  }

  if (field.isEnum && field.enumValues) {
    const known = field.enumValues.includes(shown)
    return (
      <td>
        <select
          className="form-select form-select-sm"
          value={shown}
          onChange={(e) => onChange(e.target.value)}
        >
          {!known && <option value={shown}>{shown || '(없음)'}</option>}
          {field.enumValues.map((v) => (
            <option key={v} value={v}>
              {v}
            </option>
          ))}
        </select>
      </td>
    )
  }

  // 중첩 JSON — 클릭하면 자식 layer 로 들어간다
  if (field.isJson) {
    return (
      <td>
        <span className="badge bg-cyan-lt json-badge" onClick={onEnterNested}>
          <i className="ti ti-code me-1" />
          {countJsonItems(val)}개 항목
        </span>
      </td>
    )
  }

  if (field.type === 'bool') {
    return (
      <td>
        <label className="form-check form-switch mb-0">
          <input
            type="checkbox"
            className="form-check-input"
            checked={Boolean(val)}
            onChange={(e) => onChange(e.target.checked)}
          />
        </label>
      </td>
    )
  }

  // 'number' 는 C# float/double 이 실려 오는 이름이다 — 이게 빠져 있어서
  // float 필드가 숫자 입력이 아니라 텍스트로 떨어졌다.
  if (field.type === 'int' || field.type === 'long' || field.type === 'float' || field.type === 'number') {
    const isInt = field.type === 'int' || field.type === 'long'
    return (
      <td>
        <input
          type="number"
          className="form-control form-control-sm"
          step={isInt ? 1 : 0.01}
          value={shown}
          onChange={(e) =>
            onChange(isInt ? parseInt(e.target.value) || 0 : parseFloat(e.target.value) || 0)
          }
        />
      </td>
    )
  }

  return (
    <td>
      <input
        type="text"
        className="form-control form-control-sm"
        value={shown}
        onChange={(e) => onChange(e.target.value)}
      />
    </td>
  )
}
