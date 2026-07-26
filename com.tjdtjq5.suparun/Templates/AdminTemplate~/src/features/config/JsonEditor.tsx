import { useState } from 'react'
import { Modal } from '../../shared/Modal'
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
  onSave,
  onClose,
}: {
  title: string
  rootLabel: string
  schema: JsonSchemaField[]
  initialJson: unknown
  onSave: (json: string) => Promise<void> | void
  onClose: () => void
}) {
  const [layers, setLayers] = useState<Layer[]>(() => {
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

  const cur = layers[layers.length - 1]

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

  /** 중첩 자식 layer 진입 */
  function enterNested(rowIndex: number, field: JsonSchemaField) {
    const childItems = parseArray(cur.items[rowIndex]?.[field.name])
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
        label: `${cur.label} > [${rowIndex + 1}].${field.name}`,
        parentIndex: rowIndex,
        parentField: field.name,
      },
    ])
  }

  /** 자식 layer 를 부모 item 에 문자열로 접어 넣는다 (서버 왕복 형식과 동일). */
  function foldTop(stack: Layer[]): Layer[] {
    if (stack.length <= 1) return stack
    const child = stack[stack.length - 1]
    const parent = { ...stack[stack.length - 2] }
    if (child.parentIndex != null && child.parentField) {
      parent.items = parent.items.map((it, idx) =>
        idx === child.parentIndex ? { ...it, [child.parentField!]: JSON.stringify(child.items) } : it,
      )
    }
    return [...stack.slice(0, -2), parent]
  }

  function goBack() {
    setLayers((prev) => foldTop(prev))
  }

  async function save() {
    setSaving(true)
    try {
      let stack = layers
      while (stack.length > 1) stack = foldTop(stack)
      await onSave(JSON.stringify(stack[0].items))
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
      <div style={{ padding: 12, maxHeight: '70vh', overflow: 'auto' }}>
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

          {cur.schema.length === 0 ? (
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

          <button className="btn btn-sm btn-outline-primary mt-2" onClick={addRow}>
            <i className="ti ti-plus me-1" />행 추가
          </button>
      </div>
    </Modal>
  )
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
