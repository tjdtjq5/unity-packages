import { useCallback, useEffect, useState } from 'react'
import { LoadingBlock, Spinner } from '../../shared/Spinner'
import { sb } from '../../shared/supabase'
import { toast } from '../../shared/toast'
import { recordViewed } from '../audit/viewed'

/**
 * 서버 로그 — 옛 Unity 대시보드 Monitor 탭.
 *
 * 옮긴 김에 **읽는 자격이 바뀌었다.** 대시보드는 anon key 로 읽었는데 그 키는 게임 빌드에 들어간다.
 * 그래서 `server_log` 는 RLS 를 켠 적이 없었고, 키만 뽑으면 누구나 request_body·player_id·
 * 스택트레이스를 읽을 수 있었다. 지금은 관리자 세션으로 읽으므로 그 표를 잠글 수 있다
 * (`admin_read` 정책 — ServerCodeGenerator.GenerateServerLogsMigration).
 *
 * 표가 없으면 "아직 배포된 적 없다" 는 뜻이다. 에러로 취급하지 않는다.
 */

interface LogEntry {
  id: string
  level: string
  message: string
  stack: string | null
  endpoint: string | null
  player_id: string | null
  service_name: string | null
  status_code: number | null
  request_body: string | null
  duration_ms: number | null
  createdat: number
}

type Level = 'all' | 'error' | 'warn'

const PAGE = 50

export function LogsPage() {
  // 민감 화면(플레이어 ID·request body 노출) — 진입을 감사에 자기기록한다 (#27).
  useEffect(() => recordViewed('server_log'), [])

  const [logs, setLogs] = useState<LogEntry[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [missing, setMissing] = useState(false)
  const [level, setLevel] = useState<Level>('all')
  const [limit, setLimit] = useState(PAGE)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(false)
  const [open, setOpen] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!sb) return setError('Supabase 설정이 필요합니다.')
    setLoading(true)
    try {
      // 한 줄 더 받아 "더 있는가" 를 판단한다 — count 를 따로 세는 왕복보다 싸다.
      let q = sb
        .from('server_log')
        .select<LogEntry[]>('*')
        .order('createdat', { ascending: false })
        .limit(limit + 1)
      if (level !== 'all') q = q.eq('level', level)

      const { data, error: err } = await q
      if (err) {
        // PGRST205 = 스키마 캐시에 표가 없다. 42P01 = 표가 없다.
        const notFound = /does not exist|PGRST205|42P01/i.test(err.message + (err.code ?? ''))
        if (notFound) {
          setMissing(true)
          setLogs([])
        } else setError(err.message)
        return
      }

      const rows = data ?? []
      setHasMore(rows.length > limit)
      setLogs(rows.slice(0, limit))
      setMissing(false)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [level, limit])

  useEffect(() => {
    void load()
  }, [load])

  if (error) {
    return (
      <div className="empty-state">
        <i className="ti ti-alert-triangle" />
        <h3>로그를 불러오지 못했습니다</h3>
        <p>{error}</p>
      </div>
    )
  }

  if (!logs) return <LoadingBlock label="서버 로그 불러오는 중" />

  return (
    <div>
      <div className="appset-row">
        <div className="appset-row-fields">
          {(['all', 'error', 'warn'] as Level[]).map((l) => (
            <button
              key={l}
              className={`btn btn-sm${level === l ? ' btn-primary' : ''}`}
              onClick={() => {
                setLevel(l)
                setLimit(PAGE)
              }}
            >
              {l === 'all' ? '전체' : l}
            </button>
          ))}
          <button className="btn btn-sm" disabled={loading} onClick={() => void load()}>
            {loading ? <Spinner size={11} /> : '새로고침'}
          </button>
        </div>
      </div>

      <table className="table table-vcenter card-table table-striped">
        <thead>
          <tr>
            <th style={{ width: 60 }}>레벨</th>
            <th>엔드포인트</th>
            <th>메시지</th>
            <th style={{ width: 140 }}>메타</th>
            <th style={{ width: 90 }}>시간</th>
          </tr>
        </thead>
        <tbody>
          {logs.map((l) => (
            <LogRow
              key={l.id}
              log={l}
              open={open === l.id}
              onToggle={() => setOpen(open === l.id ? null : l.id)}
            />
          ))}
          {logs.length === 0 && (
            <tr>
              <td colSpan={5} className="text-center text-muted py-4">
                <i className="ti ti-file-text me-1" />
                {missing
                  ? '아직 server_log 표가 없습니다 — 서버를 한 번 배포하면 생깁니다.'
                  : '기록된 로그가 없습니다.'}
              </td>
            </tr>
          )}
        </tbody>
      </table>

      {hasMore && (
        <div className="appset-row">
          <div className="appset-row-main">
            <div className="appset-row-key">{logs.length}개 표시 중</div>
          </div>
          <div className="appset-row-fields">
            <button className="btn btn-sm" disabled={loading} onClick={() => setLimit(limit + PAGE)}>
              더 불러오기
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

function LogRow({
  log,
  open,
  onToggle,
}: {
  log: LogEntry
  open: boolean
  onToggle: () => void
}) {
  const meta = [
    log.player_id && `player ${log.player_id}`,
    log.status_code ? String(log.status_code) : null,
    log.duration_ms ? `${log.duration_ms}ms` : null,
  ].filter(Boolean)

  return (
    <>
      <tr style={{ cursor: 'pointer' }} onClick={onToggle}>
        <td>
          <span className={`badge ${log.level === 'error' ? 'bg-red' : 'bg-orange'}`}>{log.level}</span>
        </td>
        <td>
          <code>{log.endpoint ?? '-'}</code>
        </td>
        <td style={{ maxWidth: 420, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {log.message}
        </td>
        <td className="text-muted">{meta.join(' · ') || log.service_name || ''}</td>
        <td className="text-muted">{relativeTime(log.createdat)}</td>
      </tr>
      {open && (
        <tr>
          <td colSpan={5}>
            {log.stack && (
              <>
                <div className="appset-row-name">Stack Trace</div>
                <pre style={{ whiteSpace: 'pre-wrap', marginBottom: 8 }}>{log.stack}</pre>
              </>
            )}
            {log.request_body && (
              <>
                <div className="appset-row-name">Request Body</div>
                <pre style={{ whiteSpace: 'pre-wrap', marginBottom: 8 }}>{log.request_body}</pre>
              </>
            )}
            <button
              className="btn btn-sm"
              onClick={(e) => {
                e.stopPropagation()
                void navigator.clipboard.writeText(formatForCopy(log))
                toast('로그를 복사했습니다', 'success')
              }}
            >
              로그 복사
            </button>
          </td>
        </tr>
      )}
    </>
  )
}

function relativeTime(unixMs: number): string {
  if (!unixMs) return ''
  const diff = Date.now() - unixMs
  const s = diff / 1000
  if (s < 60) return `${Math.floor(s)}초 전`
  if (s < 3600) return `${Math.floor(s / 60)}분 전`
  if (s < 86400) return `${Math.floor(s / 3600)}시간 전`
  if (s < 86400 * 30) return `${Math.floor(s / 86400)}일 전`
  return new Date(unixMs).toLocaleDateString('ko-KR')
}

/** 붙여넣어 그대로 공유할 수 있는 형태. 옛 MonitorTab.FormatLogForCopy 와 같은 항목들이다. */
function formatForCopy(l: LogEntry): string {
  return [
    `[${l.level?.toUpperCase()}] ${l.endpoint ?? ''}`,
    `Message: ${l.message}`,
    l.player_id && `Player: ${l.player_id}`,
    l.service_name && `Service: ${l.service_name}`,
    l.status_code && `Status: ${l.status_code}`,
    l.duration_ms && `Duration: ${l.duration_ms}ms`,
    l.request_body && `Request: ${l.request_body}`,
    l.stack && `Stack Trace:\n${l.stack}`,
    l.createdat && `Time: ${new Date(l.createdat).toLocaleString('ko-KR')}`,
  ]
    .filter(Boolean)
    .join('\n')
}
