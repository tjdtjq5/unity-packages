import { useState } from 'react'
import { useAdmin } from '../shell/AdminContext'
import { castValue } from '../../shared/castValue'
import type { ConfigField, ConfigRow } from '../../shared/types'
import { FkListCell } from './FkListEditor'
import { IconCell } from './IconPicker'
import { JsonEditorModal, formatJsonArray } from './JsonEditor'
import { RewardsCell } from './RewardsEditor'
import { SearchSelect } from './SearchSelect'
import { isFieldDisabled } from './fieldVisibility'
import { useComponentMap } from './useLazyMaps'

const NUMERIC = ['int', 'long', 'number']

/** `[Component]` 셀 — 어드레서블 주소 목록을 lazy 로드해 검색 드롭다운으로 보여준다. */
function ComponentCell({
  componentType,
  value,
  onChange,
}: {
  componentType: string
  value: string
  onChange: (v: string) => void
}) {
  const map = useComponentMap(true)
  const addrs = map?.[componentType] ?? []
  return (
    <SearchSelect
      options={addrs.map((a) => ({ id: a }))}
      value={value}
      onChange={onChange}
    />
  )
}

interface CellProps {
  row: ConfigRow
  field: ConfigField
  saved: boolean
  /**
   * 값 변경. rowId 는 부모가 클로저로 이미 묶었으므로 여기서 알 필요가 없다.
   * immediate=true 면 디바운스 없이 바로 저장한다 (토글·드롭다운).
   */
  onChange: (fieldName: string, value: unknown, immediate?: boolean) => void
}

/**
 * Config 셀 렌더. 바닐라 renderCell() 을 옮긴 것이다.
 *
 * 바닐라는 셀 종류마다 HTML 문자열을 만들고 `onclick="onFieldChange('id','field',this.value)"` 로
 * 전역 함수를 문자열에 박았다. 여기서는 전부 클로저다 — 전역도 escHtml 도 필요 없다.
 *
 * TODO(4단계 턴2~3): foreignKey / foreignKeyList / iconAtlas / componentType /
 *   isJson / rewards 셀은 아직 읽기 표시만 한다. 각각 검색 셀렉트·아이콘 그리드·
 *   FK 리스트 모달·JSON 중첩 에디터가 붙어야 완성된다.
 */
export function ConfigCell({ row, field, saved, onChange }: CellProps) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState('')
  const [jsonOpen, setJsonOpen] = useState(false)
  const fkSources = useAdmin().fkSources

  if (isFieldDisabled(row, field)) {
    return (
      <td data-field={field.name}>
        <span className="cell-na">—</span>
      </td>
    )
  }

  const val = row[field.name]
  const shown = String(val ?? '')

  // ── bool: 스위치 토글 (즉시 저장) ──
  if (field.type === 'bool') {
    return (
      <td data-field={field.name}>
        <label className="form-check form-switch mb-0">
          <input
            type="checkbox"
            className="form-check-input"
            checked={Boolean(val)}
            onChange={(e) => onChange(field.name, e.target.checked, true)}
          />
        </label>
      </td>
    )
  }

  // ── enum: 드롭다운 (즉시 저장) ──
  if (field.isEnum && field.enumValues) {
    const known = field.enumValues.includes(shown)
    return (
      <td data-field={field.name}>
        <select
          className="form-select form-select-sm"
          value={shown}
          onChange={(e) => onChange(field.name, e.target.value, true)}
        >
          {/* 서버 값이 enum 목록에 없으면 그 값을 맨 앞에 끼워 보존한다 (바닐라와 동일) */}
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

  // ── FK 드롭다운 ──
  if (field.foreignKey && fkSources[field.foreignKey]) {
    return (
      <td data-field={field.name}>
        <SearchSelect
          options={fkSources[field.foreignKey]}
          value={shown}
          onChange={(v) => onChange(field.name, v, true)}
        />
      </td>
    )
  }

  // ── FK 리스트 (TEXT 컬럼에 id JSON 배열) ──
  if (field.foreignKeyList) {
    return (
      <td data-field={field.name}>
        <FkListCell
          fieldName={field.name}
          target={field.foreignKeyList}
          value={val}
          onChange={(v) => onChange(field.name, v, true)}
        />
      </td>
    )
  }

  // ── 아이콘 (아틀라스 썸네일 그리드) ──
  if (field.iconAtlas) {
    return (
      <td data-field={field.name}>
        <IconCell
          atlas={field.iconAtlas}
          value={shown}
          onChange={(v) => onChange(field.name, v, true)}
        />
      </td>
    )
  }

  // ── 컴포넌트 (어드레서블 주소) ──
  if (field.componentType) {
    return (
      <td data-field={field.name}>
        <ComponentCell
          componentType={field.componentType}
          value={shown}
          onChange={(v) => onChange(field.name, v, true)}
        />
      </td>
    )
  }

  // ── Rewards 전용 모달 (rewards / *_rewards) ──
  if (field.isJson && (field.name === 'rewards' || field.name.endsWith('_rewards'))) {
    return (
      <td data-field={field.name}>
        <RewardsCell value={val} onChange={(json) => onChange(field.name, json, true)} />
      </td>
    )
  }

  // ── JSON 배열 (중첩 무제한) ──
  if (field.isJson) {
    return (
      <td data-field={field.name}>
        <span
          className="badge bg-cyan-lt json-badge"
          title={shown || '[]'}
          onClick={() => setJsonOpen(true)}
        >
          <i className="ti ti-code me-1" />
          {formatJsonArray(val)}
        </span>
        {jsonOpen && (
          <JsonEditorModal
            title={field.name.replace(/_/g, ' ')}
            rootLabel={field.name}
            schema={field.jsonSchema ?? []}
            initialJson={val}
            onSave={(json) => onChange(field.name, json, true)}
            onClose={() => setJsonOpen(false)}
          />
        )}
      </td>
    )
  }

  // ── 일반 텍스트 / PK: 인라인 편집 ──
  function commit() {
    setEditing(false)
    const next = castValue(draft, field.type)
    if (next !== val) onChange(field.name, next)
  }

  const inner = editing ? (
    <input
      className="cell-input"
      type={NUMERIC.includes(field.type) ? 'number' : 'text'}
      value={draft}
      autoFocus
      onChange={(e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') e.currentTarget.blur()
        if (e.key === 'Escape') setEditing(false)
      }}
    />
  ) : (
    shown
  )

  const cls = `cell-edit${saved ? ' cell-saved' : ''}`
  const start = () => {
    setDraft(shown)
    setEditing(true)
  }

  return (
    <td data-field={field.name}>
      {field.isPrimaryKey ? (
        <code className={`${cls} text-muted`} onClick={start}>
          {inner}
        </code>
      ) : (
        <span className={cls} onClick={start}>
          {inner}
        </span>
      )}
    </td>
  )
}
