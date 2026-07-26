import { useState } from 'react'
import { Modal } from '../../shared/Modal'
import type { NodeSpec } from '../../shared/types'
import { JsonCell } from './JsonEditor'

/**
 * `[Polymorphic]` 컬럼 편집기 — 타입을 고르고 그 타입의 필드만 채운다.
 *
 * 컬럼 하나가 행마다 다른 뜻을 갖던 구조를 대체한다. 예전에는 공용 컬럼에
 * `[VisibleIf]` 를 달아 가렸다면, 이제 타입마다 자기 이름의 필드를 갖는다.
 *
 * 저장 형태는 노드 하나와 같다: `{"type":"GunPatternData","range":10}`.
 * 실제로 다형 필드는 연결 없는 노드 하나라 카탈로그를 노드 그래프와 공유한다.
 */
export function PolymorphicEditor({
  title,
  specs,
  initialJson,
  onSave,
  onClose,
}: {
  title: string
  specs: NodeSpec[]
  initialJson: string
  onSave(json: string): void
  onClose(): void
}) {
  const initial = parseValue(initialJson)
  const [typeName, setTypeName] = useState(initial.type)
  const [values, setValues] = useState<Record<string, unknown>>(initial.values)

  const spec = specs.find((s) => s.type === typeName)

  // 표의 다른 셀(토글·드롭다운·FK)이 전부 즉시 저장이라 여기만 확인 버튼을 두면 어긋난다.
  const commit = (type: string, vals: Record<string, unknown>) =>
    onSave(type ? JSON.stringify({ type, ...vals }) : '')

  const changeType = (next: string) => {
    // 값은 이어받지 않는다 — 이름이 같아도 타입이 다르면 뜻이 다르다.
    const vals = next ? defaultsOf(specs.find((s) => s.type === next)) : {}
    setTypeName(next)
    setValues(vals)
    commit(next, vals)
  }

  const changeField = (name: string, v: unknown) => {
    const vals = { ...values, [name]: v }
    setValues(vals)
    commit(typeName, vals)
  }

  return (
    <Modal title={<span className="fw-bold px-2">{title}</span>} maxWidth={560} onClose={onClose}>
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

      {typeName && !spec && (
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
                  item={values}
                  field={f}
                  onChange={(v) => changeField(f.name, v)}
                  // 다형 값 안의 중첩 JSON 은 아직 열지 않는다 — 쓰이면 그때 붙인다.
                  onEnterNested={() => {}}
                />
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Modal>
  )
}

/** 셀에 보일 짧은 요약. 타입명이 없으면 비어 있는 것이다. */
export function describePolymorphic(json: string): string {
  const { type } = parseValue(json)
  return type || '(비어 있음)'
}

function parseValue(json: string): { type: string; values: Record<string, unknown> } {
  if (!json || !json.trim()) return { type: '', values: {} }
  try {
    const obj = JSON.parse(json) as Record<string, unknown>
    const { type, ...rest } = obj
    return { type: typeof type === 'string' ? type : '', values: rest }
  } catch {
    return { type: '', values: {} }
  }
}

/**
 * 새 타입을 고르면 그 타입의 기본값으로 시작한다.
 * 코드에 적힌 초기값(`default`)이 있으면 그걸 쓴다 — 없으면 타입별 빈 값이다.
 */
function defaultsOf(spec: NodeSpec | undefined): Record<string, unknown> {
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
