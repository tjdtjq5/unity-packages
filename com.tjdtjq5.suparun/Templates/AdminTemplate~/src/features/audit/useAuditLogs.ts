import { useCallback, useEffect, useRef, useState } from 'react'
import { isPreview } from '../../shared/env'
import { sb } from '../../shared/supabase'
import type { AuditLog } from '../../shared/types'

const PAGE = 50

/** 3종 필터 (#26). 빈 문자열 = 전체. from/to 는 date input 값(YYYY-MM-DD)이다. */
export interface AuditFilter {
  configType: string
  adminId: string
  from: string
  to: string
}

export const EMPTY_FILTER: AuditFilter = { configType: '', adminId: '', from: '', to: '' }

/**
 * 'YYYY-MM-DD' → **로컬** 자정 epoch ms. `Date.parse` 는 날짜만 있으면 UTC 자정으로
 * 해석해서(KST 기준 09:00) 당일 첫 9시간이 필터에서 빠진다 — 표시는 로컬인데 필터만
 * UTC 면 화면과 결과가 서로 다른 말을 하게 된다.
 */
function localDayStart(ymd: string): number {
  const [y, m, d] = ymd.split('-').map(Number)
  return new Date(y, m - 1, d).getTime()
}

/**
 * 감사 이벤트 목록 — 필터는 서버(PostgREST)가 하고, 페이지는 [더 불러오기]로 붙인다
 * (#25·#26, Metaplay Load More 동형). 필터가 바뀌면 처음부터 다시 쌓는다.
 *
 * `operator_read` 정책이라 롤 보유자면 읽힌다. 쓰기 정책은 없다 — 기록은 트리거와
 * viewed RPC 만 하고, 사람이 고칠 수 있으면 감사가 아니다.
 */
export function useAuditLogs(filter: AuditFilter) {
  const [logs, setLogs] = useState<AuditLog[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [hasMore, setHasMore] = useState(false)
  const [loading, setLoading] = useState(false)
  /** 필터 변경과 늦게 도착한 이전 요청의 경합 방지 — 요청 세대 번호. */
  const gen = useRef(0)

  const fetchPage = useCallback(
    async (offset: number): Promise<AuditLog[]> => {
      if (isPreview()) {
        // 프리뷰 mock 은 필터·페이지가 없다 — 첫 페이지만 그대로 보여준다.
        return offset === 0 ? ((await window.__previewApi!('/admin_audit_log')) as AuditLog[]) : []
      }
      if (!sb) throw new Error('Supabase 연결이 설정되지 않았습니다.')

      let q = sb.from('admin_audit_log').select<AuditLog[]>('*')
      if (filter.configType) q = q.eq('config_type', filter.configType)
      if (filter.adminId) q = q.eq('admin_id', filter.adminId)
      if (filter.from) q = q.gte('created_at', localDayStart(filter.from))
      // to 는 그 날의 끝까지 — 같은 날짜를 넣으면 하루가 통째로 잡혀야 한다.
      if (filter.to) q = q.lte('created_at', localDayStart(filter.to) + 86_399_999)

      const r = await q.order('created_at', { ascending: false }).range(offset, offset + PAGE - 1)
      if (r.error) throw new Error(r.error.message)
      return r.data ?? []
    },
    [filter.configType, filter.adminId, filter.from, filter.to],
  )

  // 필터가 바뀌면 처음부터.
  useEffect(() => {
    const g = ++gen.current
    setLogs(null)
    setError(null)
    void (async () => {
      try {
        const page = await fetchPage(0)
        if (g !== gen.current) return
        setLogs(page)
        setHasMore(page.length === PAGE)
      } catch (e) {
        if (g !== gen.current) return
        setError(e instanceof Error ? e.message : String(e))
      }
    })()
  }, [fetchPage])

  const loadMore = useCallback(async () => {
    if (loading || !logs) return
    const g = gen.current
    setLoading(true)
    try {
      const page = await fetchPage(logs.length)
      if (g !== gen.current) return
      setLogs([...logs, ...page])
      setHasMore(page.length === PAGE)
    } catch (e) {
      if (g === gen.current) setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [fetchPage, loading, logs])

  return { logs, error, hasMore, loading, loadMore }
}
