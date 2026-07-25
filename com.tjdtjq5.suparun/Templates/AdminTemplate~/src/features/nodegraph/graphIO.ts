import type { Edge, Node } from '@xyflow/react'
import type { GraphDoc, GraphNodeData, NodeSpec } from '../../shared/types'

/**
 * 컬럼 JSON ↔ React Flow 상태 변환.
 *
 * 저장 형식은 **배열 인덱스가 곧 연결**이다(`{"onTrue":3}`). React Flow 는 엣지 목록을
 * 따로 들고 있으므로 두 표현 사이를 오간다. 인덱스를 그대로 노드 id 로 쓰면
 * 왕복이 단순해지지만, 노드를 지울 때 뒤쪽 인덱스가 전부 밀린다 —
 * 그래서 **저장 시점에만** 인덱스를 다시 매긴다.
 *
 * 엣지는 두 종류다:
 *   실행(`e:`) — `[NodeOut]` 필드. source 노드가 다음으로 넘어갈 곳
 *   데이터(`d:`) — `NodeValue` 연결. Pure 노드가 소비 노드의 입력칸으로 값을 보낸다
 */

export interface FlowNodeData extends Record<string, unknown> {
  spec: NodeSpec
  /** 필드·포트 값. 포트 값은 엣지로도 표현되므로 저장 때 엣지가 우선한다. */
  values: Record<string, unknown>
  /** 이 노드가 그래프의 진입점인가. */
  isEntry: boolean
}

export type FlowNode = Node<FlowNodeData>

const NODE_TYPE = 'suparunNode'
const DEFAULT_GAP = 260

/** 값이 `{"$node":n}` 형태면 n 을, 아니면 null 을 돌려준다. */
export function linkTarget(value: unknown): number | null {
  if (value && typeof value === 'object' && '$node' in (value as object)) {
    const n = (value as { $node: unknown }).$node
    return typeof n === 'number' ? n : null
  }
  return null
}

export function parseDoc(json: string | null | undefined): GraphDoc {
  if (!json || !json.trim()) return { nodes: [], entry: -1 }
  try {
    const parsed = JSON.parse(json) as Partial<GraphDoc>
    return {
      nodes: Array.isArray(parsed.nodes) ? parsed.nodes : [],
      entry: typeof parsed.entry === 'number' ? parsed.entry : -1,
      layout: Array.isArray(parsed.layout) ? parsed.layout : undefined,
    }
  } catch {
    return { nodes: [], entry: -1 }
  }
}

/** 저장 문서를 캔버스 상태로 편다. 카탈로그에 없는 타입은 건너뛴다(원본은 보존되지 않는다). */
export function toFlow(doc: GraphDoc, specs: NodeSpec[]): { nodes: FlowNode[]; edges: Edge[] } {
  const byType = new Map(specs.map((s) => [s.type, s]))
  const nodes: FlowNode[] = []
  const edges: Edge[] = []

  doc.nodes.forEach((raw, i) => {
    const spec = byType.get(raw?.type)
    if (!spec) return

    const pos = doc.layout?.[i] ?? { x: (i % 4) * DEFAULT_GAP, y: Math.floor(i / 4) * 180 }
    const values: Record<string, unknown> = {}
    for (const [k, v] of Object.entries(raw)) if (k !== 'type') values[k] = v

    nodes.push({
      id: String(i),
      type: NODE_TYPE,
      position: pos,
      data: { spec, values, isEntry: i === doc.entry },
    })

    // 실행 엣지 — [NodeOut] 필드에서 뽑는다.
    for (const port of spec.outs) {
      const v = raw[port.name]
      if (port.list) {
        const arr = Array.isArray(v) ? (v as number[]) : []
        arr.forEach((target, slot) => {
          if (target >= 0) edges.push(execEdge(i, `${port.name}.${slot}`, target))
        })
      } else if (typeof v === 'number' && v >= 0) {
        edges.push(execEdge(i, port.name, v))
      }
    }

    // 데이터 엣지 — NodeValue 칸에 Pure 노드가 꽂힌 경우.
    for (const f of spec.fields) {
      if (!f.isNodeValue) continue
      const src = linkTarget(raw[f.name])
      if (src !== null && src >= 0) edges.push(dataEdge(src, i, f.name))
    }
  })

  return { nodes, edges }
}

function execEdge(from: number, handle: string, to: number): Edge {
  return {
    id: `e:${from}:${handle}`,
    source: String(from),
    sourceHandle: handle,
    target: String(to),
    targetHandle: 'in',
  }
}

function dataEdge(pureNode: number, consumer: number, field: string): Edge {
  return {
    id: `d:${consumer}:${field}`,
    source: String(pureNode),
    sourceHandle: 'out',
    target: String(consumer),
    targetHandle: field,
    className: 'ng-edge-data',
  }
}

/**
 * 캔버스 상태를 저장 문서로 되돌린다.
 *
 * 노드 id 는 편집 중에 구멍이 생길 수 있으므로(삭제) 여기서 **0부터 다시 매긴다**.
 * 연결도 새 인덱스로 옮겨진다.
 */
export function toDoc(nodes: FlowNode[], edges: Edge[]): GraphDoc {
  const order = new Map<string, number>()
  nodes.forEach((n, i) => order.set(n.id, i))
  const idx = (id: string | null | undefined) => (id != null && order.has(id) ? order.get(id)! : -1)

  const out: GraphNodeData[] = nodes.map((n) => {
    const row: GraphNodeData = { type: n.data.spec.type }

    // 포트가 아닌 값만 먼저 싣는다 — 포트는 엣지가 진실이다.
    const portNames = new Set(n.data.spec.outs.map((p) => p.name))
    for (const [k, v] of Object.entries(n.data.values)) {
      if (portNames.has(k)) continue
      row[k] = v
    }

    // 실행 포트 — 기본은 끊긴 상태(-1)이고 엣지가 있으면 덮어쓴다.
    for (const port of n.data.spec.outs) {
      if (port.list) {
        const slots: number[] = []
        for (const e of edges) {
          if (e.source !== n.id || !e.sourceHandle?.startsWith(`${port.name}.`)) continue
          const slot = Number(e.sourceHandle.slice(port.name.length + 1))
          if (Number.isInteger(slot)) slots[slot] = idx(e.target)
        }
        row[port.name] = Array.from(slots, (v) => (typeof v === 'number' ? v : -1))
      } else {
        const e = edges.find((x) => x.source === n.id && x.sourceHandle === port.name)
        row[port.name] = e ? idx(e.target) : -1
      }
    }

    // 데이터 입력 — 꽂혀 있으면 링크로, 아니면 상수 값 그대로.
    for (const f of n.data.spec.fields) {
      if (!f.isNodeValue) continue
      const e = edges.find((x) => x.target === n.id && x.targetHandle === f.name)
      if (e) row[f.name] = { $node: idx(e.source) }
      else row[f.name] = stripLink(n.data.values[f.name])
    }

    return row
  })

  // 진입점이 지정되지 않았으면 -1 이다. 아무 노드나 시작점으로 넘겨버리면
  // 게임이 엉뚱한 데서 도는데 그게 저장 시점엔 드러나지 않는다.
  const entryNode = nodes.find((n) => n.data.isEntry)
  return {
    nodes: out,
    entry: entryNode ? idx(entryNode.id) : -1,
    layout: nodes.map((n) => ({ x: Math.round(n.position.x), y: Math.round(n.position.y) })),
  }
}

/** 엣지가 끊긴 입력칸이 `{"$node":n}` 를 그대로 들고 있으면 안 되므로 비운다. */
function stripLink(value: unknown): unknown {
  return linkTarget(value) !== null ? null : value
}
