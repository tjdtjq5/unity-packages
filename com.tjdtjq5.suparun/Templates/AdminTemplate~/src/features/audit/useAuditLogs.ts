import { useCallback, useEffect, useState } from 'react'
import { selectAll } from '../../shared/db'
import type { AuditLog } from '../../shared/types'

const LIMIT = 100

/**
 * 변경 이력 목록. 서버 `/_audit` 대신 admin_audit_log 를 직접 읽는다 (ADR-0004).
 *
 * `admin_read` 정책(is_admin)이 걸려 있어 관리자만 보인다. 쓰기 정책은 없다 —
 * 기록은 suparun_audit() 트리거(SECURITY DEFINER)만 하고, 사람이 고칠 수 있으면 감사가 아니다.
 */
export function useAuditLogs() {
  const [logs, setLogs] = useState<AuditLog[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setError(null)
      setLogs(
        await selectAll<AuditLog>('admin_audit_log', {
          orderBy: 'created_at',
          ascending: false,
          limit: LIMIT,
        }),
      )
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  return { logs, error, reload }
}
