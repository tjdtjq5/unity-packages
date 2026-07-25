import { toast } from './toast'

/**
 * 표 컬럼 너비 조정 / 자동 너비 / Wrap 토글 / 헤더 우클릭 메뉴.
 * 바닐라 index.html 의 동명 함수들을 그대로 옮긴 것이다 (동작 변경 없음).
 *
 * React 컴포넌트가 아니라 **DOM 유틸**이다. 표를 그린 뒤 그 컨테이너를 넘겨 호출한다 —
 * 컬럼 폭은 렌더 결과(offsetWidth)를 재야 정해지므로 선언적으로 표현할 수가 없다.
 * 적용 대상 7곳: Config / JSON 모달 / Admin / Audit / Table / Cross / Player.
 *
 * localStorage 형식 (`col_w_<key>`):
 *   신규: { widths: { 0: 120, 2: 80 }, wraps: { 0: true } }
 *   레거시: [w1, w2, ...]  → loadColPrefs 에서 자동 마이그레이션
 */

/**
 * 자동 너비/wrap 계산에 쓰는 필드 메타의 최소 형태.
 * `ConfigField` / `TableField` / JSON 스키마 필드가 모두 이 형태를 만족한다.
 */
export interface ColResizeField {
  name?: string
  key?: string
  type?: string
  isEnum?: boolean
  isJson?: boolean
  foreignKey?: string
}

interface ColPrefs {
  widths: Record<number, number>
  wraps: Record<number, boolean>
}

const TYPE_LIMITS: Record<string, { min: number; max: number }> = {
  bool: { min: 50, max: 70 },
  int: { min: 70, max: 140 },
  float: { min: 70, max: 140 },
  long: { min: 70, max: 140 },
  number: { min: 70, max: 140 },
  string: { min: 100, max: 280 },
  isJson: { min: 140, max: 200 },
  isEnum: { min: 80, max: 200 },
  fk: { min: 140, max: 280 },
}

const _measureCanvas = document.createElement('canvas').getContext('2d')

function measureText(text: string, font = '11px JetBrains Mono'): number {
  if (!_measureCanvas) return 0
  _measureCanvas.font = font
  return _measureCanvas.measureText(text || '').width
}

function autoColWidth(headerText: string, sampleData: unknown[], type: string): number {
  const padding = 24
  const charW = 7.2
  const headerW = measureText(headerText || '', '11px JetBrains Mono')
  const dataMaxChars = (sampleData || [])
    .slice(0, 50)
    .reduce<number>((m, v) => Math.max(m, String(v ?? '').length), 0)
  const dataW = dataMaxChars * charW
  const limits = TYPE_LIMITS[type] || TYPE_LIMITS.string
  return Math.max(limits.min, Math.min(limits.max, Math.max(headerW, dataW) + padding))
}

const WRAP_KEYWORDS = [
  'description',
  'desc',
  'comment',
  'memo',
  'note',
  'message',
  'reason',
  'detail',
]

function shouldAutoWrap(field: ColResizeField | null, sampleData: unknown[]): boolean {
  const fieldName = (field?.name || field?.key || '').toLowerCase()
  if (WRAP_KEYWORDS.some((k) => fieldName.includes(k))) return true
  const sample = (sampleData || []).slice(0, 30).filter((v) => v != null)
  if (sample.length === 0) return false
  const avgLen = sample.reduce<number>((s, v) => s + String(v).length, 0) / sample.length
  return avgLen >= 60
}

function applyWrapMode(table: HTMLTableElement, colIdx: number, wrap: boolean): void {
  const cells = table.querySelectorAll<HTMLTableCellElement>(`tbody td:nth-child(${colIdx + 1})`)
  cells.forEach((td) => {
    if (wrap) {
      td.style.whiteSpace = 'normal'
      td.style.wordBreak = 'break-word'
      td.style.overflow = 'visible'
      td.style.textOverflow = ''
      td.removeAttribute('title')
    } else {
      td.style.whiteSpace = 'nowrap'
      td.style.wordBreak = 'normal'
      td.style.overflow = 'hidden'
      td.style.textOverflow = 'ellipsis'
      td.title = td.textContent?.trim() ?? ''
    }
  })
}

function loadColPrefs(key: string | null): ColPrefs | null {
  if (!key) return null
  const raw = localStorage.getItem('col_w_' + key)
  if (!raw) return null
  try {
    const parsed: unknown = JSON.parse(raw)
    if (Array.isArray(parsed)) {
      // 레거시 배열 → object 마이그레이션 (이 자리에서 다시 저장하지는 않음 — 첫 드래그 시 자연 갱신)
      const widths: Record<number, number> = {}
      parsed.forEach((w, i) => {
        if (w) widths[i] = Number(w)
      })
      return { widths, wraps: {} }
    }
    const o = parsed as Partial<ColPrefs>
    return { widths: o.widths || {}, wraps: o.wraps || {} }
  } catch {
    return null
  }
}

function saveColPrefs(key: string | null, prefs: ColPrefs): void {
  if (!key) return
  localStorage.setItem(
    'col_w_' + key,
    JSON.stringify({ widths: prefs.widths || {}, wraps: prefs.wraps || {} }),
  )
}

function fieldTypeKey(field: ColResizeField | null): string {
  if (!field) return 'string'
  if (field.isJson) return 'isJson'
  if (field.isEnum) return 'isEnum'
  if (field.foreignKey) return 'fk'
  return field.type || 'string'
}

function fieldDataKey(field: ColResizeField | null): string | null {
  return field?.name || field?.key || null
}

interface ColMenuContext {
  table: HTMLTableElement
  storageKey: string | null
  colIdx: number
  wrap: boolean
  prefs: ColPrefs
}

/** 헤더 우클릭 메뉴 — Wrap 토글 / Reset 폭 / Reset 전체. */
function showColMenu(event: MouseEvent, ctx: ColMenuContext): void {
  document.querySelectorAll('.col-menu').forEach((m) => m.remove())
  const menu = document.createElement('div')
  menu.className = 'col-menu'
  menu.style.top = event.clientY + 'px'
  menu.style.left = event.clientX + 'px'
  menu.innerHTML = `
    <div class="col-menu-item" data-action="wrap">${ctx.wrap ? '[x]' : '[ ]'} Wrap Text</div>
    <div class="col-menu-divider"></div>
    <div class="col-menu-item" data-action="reset">Reset This Width</div>
    <div class="col-menu-item" data-action="reset-all">Reset All Cols</div>
  `
  menu.addEventListener('click', (e) => {
    const action = (e.target as HTMLElement | null)?.dataset.action
    if (!action) return

    if (action === 'wrap') {
      const newWrap = !ctx.wrap
      ctx.prefs.wraps[ctx.colIdx] = newWrap
      applyWrapMode(ctx.table, ctx.colIdx, newWrap)
      saveColPrefs(ctx.storageKey, ctx.prefs)
    } else if (action === 'reset') {
      delete ctx.prefs.widths[ctx.colIdx]
      delete ctx.prefs.wraps[ctx.colIdx]
      saveColPrefs(ctx.storageKey, ctx.prefs)
      toast('컬럼 리셋됨. 새로고침하면 적용됩니다.', 'info')
    } else if (action === 'reset-all') {
      if (ctx.storageKey) localStorage.removeItem('col_w_' + ctx.storageKey)
      toast('모든 컬럼 리셋됨. 새로고침하면 적용됩니다.', 'info')
    }
    menu.remove()
  })
  document.body.appendChild(menu)
  setTimeout(() => {
    document.addEventListener('click', () => menu.remove(), { once: true })
  })
}

/**
 * `container` 안의 첫 `<table>` 에 컬럼 폭/wrap/리사이즈 핸들을 적용한다.
 *
 * @param storageKey localStorage 키(`col_w_` 접두사가 붙는다). null 이면 저장하지 않는다.
 * @param opts.fields 컬럼 순서와 1:1 대응하는 필드 메타. 없으면 자동 너비 대신 현재 폭을 고정한다.
 * @param opts.data   자동 너비 계산용 샘플 행(앞 50건만 본다).
 */
export function enableColResize(
  container: HTMLElement,
  storageKey: string | null,
  opts: { fields?: (ColResizeField | null)[]; data?: unknown[] } = {},
): void {
  const table = container.querySelector('table')
  if (!table) return
  table.style.tableLayout = 'fixed'
  const ths = table.querySelectorAll<HTMLTableCellElement>('thead th')
  if (!ths.length) return

  const prefs = loadColPrefs(storageKey) || { widths: {}, wraps: {} }
  const fields = opts.fields || null
  const data = opts.data || []

  ths.forEach((th, i) => {
    const field = fields ? fields[i] : null
    const headerText = th.textContent?.trim() ?? ''
    const dataKey = field ? fieldDataKey(field) : null
    const sampleData: unknown[] =
      field && dataKey
        ? data.map((row) =>
            row && typeof row === 'object' ? (row as Record<string, unknown>)[dataKey] : undefined,
          )
        : []

    // 1) 너비 결정 — 저장값(수동) 우선 → 자동 계산 → 균등 분할 폴백
    if (prefs.widths[i]) {
      th.style.width = prefs.widths[i] + 'px'
    } else if (field) {
      th.style.width = autoColWidth(headerText, sampleData, fieldTypeKey(field)) + 'px'
    } else {
      th.style.width = th.offsetWidth + 'px'
    }

    // 2) wrap 결정 — 저장값 우선 → 자동 감지 → false
    let wrap: boolean
    if (prefs.wraps[i] !== undefined) wrap = prefs.wraps[i]
    else if (field) wrap = shouldAutoWrap(field, sampleData)
    else wrap = false
    applyWrapMode(table, i, wrap)

    // 3) 헤더 ellipsis + tooltip
    th.style.overflow = 'hidden'
    th.style.textOverflow = 'ellipsis'
    th.style.whiteSpace = 'nowrap'
    th.title = headerText

    // 4) 우클릭 컨텍스트 메뉴 — wrap은 메뉴 호출 시점의 최신값을 prefs에서 다시 읽음
    th.addEventListener('contextmenu', (e) => {
      e.preventDefault()
      const currentWrap = prefs.wraps[i] !== undefined ? prefs.wraps[i] : wrap
      showColMenu(e, { table, storageKey, colIdx: i, wrap: currentWrap, prefs })
    })

    // 5) resize handle
    const handle = document.createElement('div')
    handle.className = 'th-resize'
    th.appendChild(handle)
    handle.addEventListener('mousedown', (e) => {
      e.preventDefault()
      const startX = e.pageX
      const startW = th.offsetWidth
      handle.classList.add('active')
      document.body.classList.add('col-resizing')
      const onMove = (ev: MouseEvent) => {
        th.style.width = Math.max(40, startW + ev.pageX - startX) + 'px'
      }
      const onUp = () => {
        handle.classList.remove('active')
        document.body.classList.remove('col-resizing')
        document.removeEventListener('mousemove', onMove)
        document.removeEventListener('mouseup', onUp)
        if (storageKey) {
          const widths: Record<number, number> = {}
          table.querySelectorAll<HTMLTableCellElement>('thead th').forEach((t, k) => {
            widths[k] = t.offsetWidth
          })
          prefs.widths = widths
          saveColPrefs(storageKey, prefs)
        }
      }
      document.addEventListener('mousemove', onMove)
      document.addEventListener('mouseup', onUp)
    })
  })
}
