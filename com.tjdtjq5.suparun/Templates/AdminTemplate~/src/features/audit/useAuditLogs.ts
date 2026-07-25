import { useCallback, useEffect, useState } from 'react'
import { configApi } from '../../shared/api'
import type { AuditLog } from '../../shared/types'

const LIMIT = 100

/**
 * 변경 이력 목록. 바닐라 showAuditLog() 의 데이터 부분을 대체한다.
 *
 * 바닐라는 전역 `auditLogs` 배열에 담고 상세 버튼이 인덱스로 그 배열을 참조했다.
 * React 에서는 로그 객체를 그대로 넘기므로 전역이 필요 없다.
 */
export function useAuditLogs() {
  const [logs, setLogs] = useState<AuditLog[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setError(null)
      setLogs(await configApi<AuditLog[]>(`/_audit?limit=${LIMIT}`))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  return { logs, error, reload }
}
