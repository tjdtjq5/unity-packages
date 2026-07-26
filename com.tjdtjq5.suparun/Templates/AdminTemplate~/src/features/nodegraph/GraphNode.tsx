import { Handle, Position, useReactFlow, type NodeProps } from '@xyflow/react'
import type { ConfigField } from '../../shared/types'
import type { FlowNode, FlowNodeData } from './graphIO'
import { linkTarget } from './graphIO'

/**
 * 캔버스에 그려지는 노드 하나.
 *
 * 핸들 배치는 역할이 정한다:
 *   pure  — 실행 핸들 없음. 오른쪽에 값 출력 하나
 *   그 외 — 왼쪽에 실행 입력 하나, 오른쪽에 `[NodeOut]` 개수만큼
 * `NodeValue` 칸은 왼쪽에 값 입력 핸들을 하나씩 더 갖는다.
 */
export function GraphNode({ id, data, selected }: NodeProps<FlowNode>) {
  const { spec, values, isEntry } = data
  const { updateNodeData, setNodes } = useReactFlow<FlowNode>()

  const setValue = (name: string, v: unknown) =>
    updateNodeData(id, { values: { ...values, [name]: v } })

  // 진입점은 하나뿐이라 다른 노드의 표시는 함께 내린다.
  const markEntry = () =>
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, isEntry: n.id === id } })))

  const isPure = spec.role === 'pure'
  const inputs = spec.fields.filter((f) => !f.isHidden)

  return (
    <div className={`ng-node ng-role-${spec.role}${selected ? ' ng-selected' : ''}${isEntry ? ' ng-entry' : ''}`}>
      {!isPure && <Handle type="target" position={Position.Left} id="in" className="ng-h ng-h-exec" />}

      <div className="ng-head">
        <span className="ng-title">{spec.label}</span>
        <span className="ng-role">{spec.role}</span>
        {/*
          어느 실행 노드든 시작점이 될 수 있다. 시점(적중/발동/만료)은 노드가 아니라
          **컬럼**이 가르기로 했으므로(ADR-0002 결정 27) 진입 전용 노드를 두지 않는다.
          Pure 는 실행 흐름에 없어 제외한다.
        */}
        {!isPure &&
          (isEntry ? (
            <span className="ng-entry-mark" title="이 노드에서 시작합니다">시작</span>
          ) : (
            <button className="ng-entry-btn" onClick={markEntry} title="여기서 시작하도록 바꿉니다">
              시작으로
            </button>
          ))}
      </div>

      {inputs.length > 0 && (
        <div className="ng-fields">
          {inputs.map((f) => (
            <FieldRow
              key={f.name}
              field={f}
              value={values[f.name]}
              onChange={(v) => setValue(f.name, v)}
            />
          ))}
        </div>
      )}

      {isPure ? (
        <div className="ng-ports">
          <div className="ng-port ng-port-out">
            <span className="ng-port-label">{spec.outType ?? '값'}</span>
            <Handle type="source" position={Position.Right} id="out" className="ng-h ng-h-data" />
          </div>
        </div>
      ) : (
        <div className="ng-ports">
          {spec.outs.map((p) =>
            p.list ? (
              <ListPorts key={p.name} nodeId={id} name={p.name} label={p.label} data={data} />
            ) : (
              <div className="ng-port" key={p.name}>
                <span className="ng-port-label">{p.label}</span>
                <Handle type="source" position={Position.Right} id={p.name} className="ng-h ng-h-exec" />
              </div>
            ),
          )}
        </div>
      )}
    </div>
  )
}

/**
 * 가변 포트(SequenceNode.steps). 슬롯 개수를 값으로 들고 있다가 핸들을 그만큼 그린다.
 * 실제 연결은 엣지가 진실이라, 여기서는 "몇 칸을 보여줄지"만 관리한다.
 */
function ListPorts({
  nodeId,
  name,
  label,
  data,
}: {
  nodeId: string
  name: string
  label: string
  data: FlowNodeData
}) {
  const { updateNodeData } = useReactFlow<FlowNode>()
  const raw = data.values[name]
  const count = Math.max(Array.isArray(raw) ? raw.length : 0, 1)

  const resize = (next: number) =>
    updateNodeData(nodeId, {
      values: { ...data.values, [name]: Array.from({ length: next }, (_, i) => (Array.isArray(raw) ? raw[i] ?? -1 : -1)) },
    })

  return (
    <>
      {Array.from({ length: count }, (_, i) => (
        <div className="ng-port" key={`${name}.${i}`}>
          <span className="ng-port-label">{`${label} ${i + 1}`}</span>
          <Handle type="source" position={Position.Right} id={`${name}.${i}`} className="ng-h ng-h-exec" />
        </div>
      ))}
      <div className="ng-port ng-port-tools">
        <button className="ng-mini" onClick={() => resize(count + 1)}>+</button>
        <button className="ng-mini" onClick={() => resize(Math.max(1, count - 1))} disabled={count <= 1}>
          −
        </button>
      </div>
    </>
  )
}

/** 입력칸 한 줄. `NodeValue` 면 왼쪽에 값 입력 핸들이 붙고, 연결되면 입력창이 잠긴다. */
function FieldRow({
  field,
  value,
  onChange,
}: {
  field: ConfigField
  value: unknown
  onChange: (v: unknown) => void
}) {
  const linked = linkTarget(value) !== null

  return (
    <div className="ng-field">
      {field.isNodeValue && (
        <Handle type="target" position={Position.Left} id={field.name} className="ng-h ng-h-data" />
      )}
      <label className="ng-label" title={`${field.name} · ${field.type}`}>
        {field.name}
        {/* 값 칸은 타입이 맞아야만 꽂힌다 — 안 보이면 왜 연결이 안 되는지 알 수 없다. */}
        {field.isNodeValue && <span className="ng-type">{field.type}</span>}
      </label>
      {linked ? (
        <span className="ng-linked">연결됨</span>
      ) : (
        <FieldInput field={field} value={value} onChange={onChange} />
      )}
    </div>
  )
}

/**
 * 값 편집기. 표 셀 렌더러(`ConfigCell`)와 같은 메타를 쓰지만 `<td>` 가 아니라
 * 노드 안에 들어가야 해서 별도로 둔다. 분기 순서는 표 쪽과 맞춰 두는 편이 헷갈리지 않는다.
 */
function FieldInput({
  field,
  value,
  onChange,
}: {
  field: ConfigField
  value: unknown
  onChange: (v: unknown) => void
}) {
  const shown = value == null ? '' : String(value)

  if (field.isEnum && field.enumValues) {
    const known = field.enumValues.includes(shown)
    return (
      <select className="ng-input" value={shown} onChange={(e) => onChange(e.target.value)}>
        {!known && <option value={shown}>{shown || '(없음)'}</option>}
        {field.enumValues.map((v) => (
          <option key={v} value={v}>
            {v}
          </option>
        ))}
      </select>
    )
  }

  if (field.type === 'bool') {
    return (
      <input
        type="checkbox"
        className="ng-check"
        checked={Boolean(value)}
        onChange={(e) => onChange(e.target.checked)}
      />
    )
  }

  if (field.type === 'int' || field.type === 'long' || field.type === 'number') {
    const step = field.type === 'number' ? 0.01 : 1
    return (
      <input
        type="number"
        className="ng-input"
        step={step}
        value={shown}
        onChange={(e) => {
          // DOM input 은 언제나 문자열이라 여기서 숫자로 못 박아야 한다 —
          // "0.3" 이 그대로 DB 에 들어가면 C# 역직렬화가 실패하거나 조용히 0 이 된다.
          const n = field.type === 'number' ? parseFloat(e.target.value) : parseInt(e.target.value, 10)
          onChange(Number.isFinite(n) ? n : 0)
        }}
      />
    )
  }

  return (
    <input
      type="text"
      className="ng-input"
      value={shown}
      onChange={(e) => onChange(e.target.value)}
    />
  )
}
