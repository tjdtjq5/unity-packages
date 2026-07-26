import {
  addEdge,
  Background,
  ReactFlow,
  ReactFlowProvider,
  SelectionMode,
  useEdgesState,
  useNodesState,
  useReactFlow,
  type Connection,
  type Edge,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import type { ConfigField, NodeSpec } from '../../shared/types'
import { GraphNode } from './GraphNode'
import { parseDoc, toDoc, toFlow, type FlowNode } from './graphIO'
import './nodegraph.css'
import { canConnect, validateGraph } from './validate'

const nodeTypes = { suparunNode: GraphNode }

/**
 * `[NodeGraph]` 컬럼을 여는 캔버스.
 *
 * 표 셀 안에서 열리므로 `position: fixed` 오버레이가 `<td>` 에 갇히지 않도록
 * `createPortal` 로 body 에 붙인다 — JSON 모달에서 이미 밟은 함정이다.
 */
export function NodeGraphModal({
  title,
  specs,
  initialJson,
  onSave,
  onClose,
}: {
  title: string
  specs: NodeSpec[]
  initialJson: string | null | undefined
  onSave(json: string): void
  onClose(): void
}) {
  return createPortal(
    <div className="ng-overlay" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="ng-panel">
        <ReactFlowProvider>
          <Canvas title={title} specs={specs} initialJson={initialJson} onSave={onSave} onClose={onClose} />
        </ReactFlowProvider>
      </div>
    </div>,
    document.body,
  )
}

function Canvas({
  title,
  specs,
  initialJson,
  onSave,
  onClose,
}: {
  title: string
  specs: NodeSpec[]
  initialJson: string | null | undefined
  onSave(json: string): void
  onClose(): void
}) {
  const initial = useMemo(() => toFlow(parseDoc(initialJson), specs), [initialJson, specs])

  const [nodes, setNodes, onNodesChange] = useNodesState<FlowNode>(initial.nodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>(initial.edges)
  const [showIssues, setShowIssues] = useState(false)
  const [showKeys, setShowKeys] = useState(false)
  const { fitView } = useReactFlow()

  const issues = useMemo(() => validateGraph(nodes, edges), [nodes, edges])
  const errors = issues.filter((i) => i.level === 'error')

  // 확대/축소 버튼을 없앤 대신 키로 맞춘다. 입력칸에 타이핑 중일 때는 가로챈다.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const t = e.target as HTMLElement | null
      if (t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.isContentEditable)) return

      if (e.key === 'f' || e.key === 'F') {
        fitView({ duration: 200 })
      } else if ((e.ctrlKey || e.metaKey) && (e.key === 'a' || e.key === 'A')) {
        e.preventDefault()
        setNodes((ns) => ns.map((n) => ({ ...n, selected: true })))
      } else if (e.key === 'Escape') {
        setNodes((ns) => ns.map((n) => (n.selected ? { ...n, selected: false } : n)))
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [fitView, setNodes])

  const onConnect = useCallback(
    (c: Connection) => {
      if (!canConnect(c, nodes)) return
      const isData = c.sourceHandle === 'out'
      // id 접두사가 실행선/값선을 가른다 — 검증과 저장이 이걸로 구분한다.
      const id = isData ? `d:${c.target}:${c.targetHandle}` : `e:${c.source}:${c.sourceHandle}`
      setEdges((es) => {
        // 포트당 연결 1개 — 새로 꽂으면 기존 것이 빠진다.
        const kept = es.filter((e) =>
          isData
            ? !(e.target === c.target && e.targetHandle === c.targetHandle)
            : !(e.source === c.source && e.sourceHandle === c.sourceHandle),
        )
        return addEdge({ ...c, id, className: isData ? 'ng-edge-data' : undefined }, kept)
      })
    },
    [nodes, setEdges],
  )

  const addNode = useCallback(
    (spec: NodeSpec) => {
      setNodes((ns) => {
        const nextId = String(ns.reduce((max, n) => Math.max(max, Number(n.id) + 1), 0))
        return [
          ...ns,
          {
            id: nextId,
            type: 'suparunNode',
            position: { x: 60 + (ns.length % 5) * 40, y: 60 + (ns.length % 7) * 40 },
            data: {
              spec,
              values: defaultValues(spec),
              // 처음 놓는 실행 노드가 곧 시작점이 된다 — 따로 지정하게 하면 잊기만 쉽다.
              isEntry: spec.role !== 'pure' && !ns.some((n) => n.data.isEntry),
            },
          } satisfies FlowNode,
        ]
      })
    },
    [setNodes],
  )

  const save = () => {
    if (errors.length > 0) {
      setShowIssues(true)
      return
    }
    onSave(JSON.stringify(toDoc(nodes, edges)))
    onClose()
  }

  return (
    <>
      <div className="ng-bar">
        <span className="ng-bar-title">{title}</span>
        <span className="ng-bar-meta">
          노드 {nodes.length} · 연결 {edges.length}
        </span>
        <button
          className={`ng-bar-issues${errors.length ? ' has-error' : ''}`}
          onClick={() => setShowIssues((v) => !v)}
        >
          {errors.length > 0 ? `오류 ${errors.length}` : issues.length > 0 ? `경고 ${issues.length}` : '문제 없음'}
        </button>
        <button className="ng-bar-issues" onClick={() => setShowKeys((v) => !v)}>
          단축키
        </button>
        <div className="ng-bar-spacer" />
        <button className="btn btn-sm" onClick={onClose}>
          취소
        </button>
        <button className="btn btn-sm btn-primary" onClick={save} disabled={errors.length > 0}>
          저장
        </button>
      </div>

      <div className="ng-body-row">
        <aside className="ng-palette">
          <div className="ng-palette-head">팔레트</div>
          {specs.length === 0 && <div className="ng-palette-empty">노드가 없습니다.</div>}
          {groupByRole(specs).map(([role, list]) => (
            <div key={role} className="ng-palette-group">
              <div className="ng-palette-role">{ROLE_LABEL[role] ?? role}</div>
              {list.map((s) => (
                <button key={s.type} className={`ng-palette-item ng-role-${s.role}`} onClick={() => addNode(s)}>
                  {s.label}
                </button>
              ))}
            </div>
          ))}

          {/* 네모와 동그라미가 뭔지 모르면 캔버스를 읽을 수 없다. */}
          <div className="ng-legend">
            <div className="ng-legend-row">
              <span className="ng-legend-dot ng-h-exec" />
              실행 순서
            </div>
            <div className="ng-legend-row">
              <span className="ng-legend-dot ng-h-data" />
              값 (타입이 같아야 꽂힘)
            </div>
          </div>
        </aside>

        <div className="ng-canvas">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            nodeTypes={nodeTypes}
            isValidConnection={(c) => canConnect(c as Connection, nodes)}
            fitView
            deleteKeyCode={['Backspace', 'Delete']}
            proOptions={{ hideAttribution: true }}
            // 어드민이 다크 테마라 캔버스도 맞춰야 한다 — 기본값(light)이면
            // 미니맵이 흰 바탕으로 떠서 배경과 따로 논다.
            colorMode="dark"
            // 좌드래그를 박스 선택에 내주고 화면 이동은 우클릭·휠 버튼·Space 로 뺀다.
            // 노드 에디터에서는 다중 선택이 화면 이동보다 잦다.
            selectionOnDrag
            panOnDrag={[1, 2]}
            panActivationKeyCode="Space"
            selectionMode={SelectionMode.Partial}
          >
            <Background gap={16} />
          </ReactFlow>

          {showKeys && (
            <div className="ng-keys">
              {KEYMAP.map(([k, desc]) => (
                <div className="ng-key" key={k}>
                  <kbd>{k}</kbd>
                  <span>{desc}</span>
                </div>
              ))}
            </div>
          )}

          {showIssues && issues.length > 0 && (
            <div className="ng-issues">
              {issues.map((it, i) => (
                <div key={i} className={`ng-issue ng-issue-${it.level}`}>
                  {it.message}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </>
  )
}

const KEYMAP: [string, string][] = [
  ['좌드래그', '박스 선택 (걸치기만 해도 잡힘)'],
  ['우드래그 · 휠드래그', '화면 이동'],
  ['Space + 드래그', '화면 이동'],
  ['스크롤', '확대 / 축소'],
  ['Shift + 스크롤', '가로 이동'],
  ['F', '전체가 보이게 맞춤'],
  ['Ctrl + A', '전체 선택'],
  ['Delete', '선택 삭제'],
  ['Esc', '선택 해제'],
]

const ROLE_LABEL: Record<string, string> = {
  entry: '시작',
  action: '동작',
  branch: '분기',
  sequence: '순차',
  loop: '반복',
  pure: '값',
  flow: '흐름',
  node: '기타',
}

const ROLE_ORDER = ['entry', 'action', 'branch', 'sequence', 'loop', 'flow', 'pure', 'node']

function groupByRole(specs: NodeSpec[]): [string, NodeSpec[]][] {
  const map = new Map<string, NodeSpec[]>()
  for (const s of specs) {
    const list = map.get(s.role) ?? []
    list.push(s)
    map.set(s.role, list)
  }
  return [...map.entries()].sort(
    (a, b) => ROLE_ORDER.indexOf(a[0]) - ROLE_ORDER.indexOf(b[0]),
  )
}

/** 새 노드의 초기값. 포트는 끊긴 상태(-1)로 시작한다. */
function defaultValues(spec: NodeSpec): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const f of spec.fields) out[f.name] = defaultOf(f)
  for (const p of spec.outs) out[p.name] = p.list ? [] : -1
  return out
}

function defaultOf(f: ConfigField): unknown {
  if (f.type === 'bool') return false
  if (f.type === 'int' || f.type === 'long' || f.type === 'number') return 0
  if (f.isEnum && f.enumValues?.length) return f.enumValues[0]
  return ''
}
