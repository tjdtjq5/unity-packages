import type { Connection, Edge } from '@xyflow/react'
import type { FlowNode } from './graphIO'

/**
 * 저장 전 검증. 잘못된 그래프가 DB 에 들어가면 Unity 쪽에서 조용히 이상 동작한다 —
 * 캔버스에서 막는 편이 훨씬 싸다.
 *
 * 사이클을 막는 이유는 결정론 시뮬레이션에서 무한 루프가 **프레임을 멈추기** 때문이다.
 * 실행기에 총 횟수 상한이 있긴 하지만 그건 최후의 방어선이고, 여기서 걸러야 한다.
 */

export interface GraphIssue {
  level: 'error' | 'warn'
  message: string
  nodeId?: string
}

export function validateGraph(nodes: FlowNode[], edges: Edge[]): GraphIssue[] {
  const issues: GraphIssue[] = []
  if (nodes.length === 0) return issues

  // ── 진입점 ──
  // 진입점이 될 수 있는 건 EntryNode 파생뿐이다. 그러라고 만든 계층이라
  // 아무 노드나 시작점이 되면 역할 구분이 무의미해진다.
  const entries = nodes.filter((n) => n.data.isEntry)
  const entryRoleNodes = nodes.filter((n) => n.data.spec.role === 'entry')

  if (entryRoleNodes.length === 0)
    issues.push({ level: 'error', message: '시작 노드가 없습니다. 팔레트의 "시작" 에서 하나 놓으세요.' })
  else if (entries.length === 0)
    issues.push({ level: 'error', message: '어느 노드에서 시작할지 지정되지 않았습니다.' })
  else if (entries.length > 1)
    issues.push({ level: 'error', message: `진입점이 ${entries.length}개입니다. 하나만 둘 수 있습니다.` })

  for (const n of entries)
    if (n.data.spec.role !== 'entry')
      issues.push({
        level: 'error',
        message: `${n.data.spec.label} 은 시작 노드가 아닙니다.`,
        nodeId: n.id,
      })

  // ── 포트당 연결 1개 ──
  const seen = new Set<string>()
  for (const e of edges) {
    const key = `${e.source}:${e.sourceHandle}`
    if (seen.has(key)) {
      const node = nodes.find((n) => n.id === e.source)
      issues.push({
        level: 'error',
        message: `${node?.data.spec.label ?? e.source} 의 "${e.sourceHandle}" 포트에 연결이 둘 이상입니다.`,
        nodeId: e.source,
      })
    }
    seen.add(key)
  }

  // ── 사이클 ──
  const execCycle = findCycle(nodes, edges.filter((e) => e.id.startsWith('e:')))
  if (execCycle) issues.push({ level: 'error', message: `실행 흐름이 순환합니다: ${execCycle}` })

  const dataCycle = findCycle(nodes, edges.filter((e) => e.id.startsWith('d:')))
  if (dataCycle) issues.push({ level: 'error', message: `값 계산이 순환합니다: ${dataCycle}` })

  // ── 도달 불가 ──
  if (entries.length === 1) {
    const reached = reachable(entries[0].id, edges)
    for (const n of nodes) {
      if (reached.has(n.id) || n.data.spec.role === 'pure') continue
      issues.push({
        level: 'warn',
        message: `${n.data.spec.label} 에 도달할 수 없습니다.`,
        nodeId: n.id,
      })
    }
  }

  return issues
}

/** 순환이 있으면 사람이 읽을 수 있는 경로 문자열을, 없으면 null 을 돌려준다. */
function findCycle(nodes: FlowNode[], edges: Edge[]): string | null {
  const next = new Map<string, string[]>()
  for (const e of edges) {
    const list = next.get(e.source) ?? []
    list.push(e.target)
    next.set(e.source, list)
  }

  const label = (id: string) => nodes.find((n) => n.id === id)?.data.spec.label ?? id
  const state = new Map<string, 0 | 1 | 2>() // 0 미방문 / 1 방문 중 / 2 완료
  const path: string[] = []
  let found: string | null = null

  const walk = (id: string): boolean => {
    state.set(id, 1)
    path.push(id)
    for (const t of next.get(id) ?? []) {
      const s = state.get(t) ?? 0
      if (s === 1) {
        found = [...path.slice(path.indexOf(t)), t].map(label).join(' → ')
        return true
      }
      if (s === 0 && walk(t)) return true
    }
    path.pop()
    state.set(id, 2)
    return false
  }

  for (const n of nodes) if ((state.get(n.id) ?? 0) === 0 && walk(n.id)) break
  return found
}

function reachable(entry: string, edges: Edge[]): Set<string> {
  const next = new Map<string, string[]>()
  for (const e of edges) {
    if (!e.id.startsWith('e:')) continue
    const list = next.get(e.source) ?? []
    list.push(e.target)
    next.set(e.source, list)
  }

  const out = new Set<string>([entry])
  const stack = [entry]
  while (stack.length) {
    const cur = stack.pop()!
    for (const t of next.get(cur) ?? [])
      if (!out.has(t)) {
        out.add(t)
        stack.push(t)
      }
  }
  return out
}

/**
 * 연결을 만들 수 있는지. 실행선은 실행 입력으로만, 값선은 타입이 맞는 칸으로만 간다.
 * 여기서 막으면 잘못된 엣지가 애초에 생기지 않아 검증이 할 일이 줄어든다.
 */
export function canConnect(conn: Connection, nodes: FlowNode[]): boolean {
  const source = nodes.find((n) => n.id === conn.source)
  const target = nodes.find((n) => n.id === conn.target)
  if (!source || !target || source.id === target.id) return false

  const isDataSource = conn.sourceHandle === 'out'
  const isExecTarget = conn.targetHandle === 'in'

  if (isDataSource) {
    if (source.data.spec.role !== 'pure' || isExecTarget) return false
    const field = target.data.spec.fields.find((f) => f.name === conn.targetHandle)
    if (!field?.isNodeValue) return false
    // outType 이 없으면(구 카탈로그) 타입 검사를 건너뛴다 — 막기보다 통과가 낫다.
    return !source.data.spec.outType || compatible(source.data.spec.outType, field.type)
  }

  // 실행선 — Pure 는 실행 흐름에 놓이지 않는다.
  if (target.data.spec.role === 'pure') return false
  return isExecTarget
}

/** 숫자끼리는 서로 통한다 — C# 쪽 int/float 구분을 캔버스에서까지 강제할 필요는 없다. */
function compatible(outType: string, fieldType: string): boolean {
  if (outType === fieldType) return true
  const numeric = new Set(['int', 'long', 'number', 'float'])
  return numeric.has(outType) && numeric.has(fieldType)
}
