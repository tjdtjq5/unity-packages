import { useEffect, useState } from 'react'
import { isPreview } from '../../shared/env'
import { sb } from '../../shared/supabase'

/**
 * 행위자 명단 — 감사 로그의 admin_id(uid)를 사람이 읽는 이메일로 바꾼다 (#25).
 * `admin_user` 는 operator_read 정책이라 롤 보유자면 읽힌다. 실패해도 화면은 uid 로
 * 동작해야 하므로 에러를 던지지 않는다.
 */

export interface Actor {
  user_id: string
  email: string | null
}

/** 모듈 캐시 — 명단은 화면 전환마다 다시 받을 만큼 자주 안 바뀐다. 새로고침이 갱신이다. */
let cached: Actor[] | null = null

export function useActors(): { actors: Actor[]; emailOf: (uid: string | null | undefined) => string } {
  const [actors, setActors] = useState<Actor[]>(cached ?? [])

  useEffect(() => {
    if (cached || isPreview() || !sb) return
    let alive = true
    void sb
      .from('admin_user')
      .select<Actor[]>('user_id,email')
      .then((r) => {
        if (!alive || r.error) return
        cached = (r.data ?? []).filter((a) => a.user_id)
        setActors(cached)
      })
    return () => {
      alive = false
    }
  }, [])

  const emailOf = (uid: string | null | undefined): string => {
    if (!uid) return '?'
    if (uid === 'server') return 'server'   // PAT·서버 경유 쓰기 — 사람 세션이 아니다
    return actors.find((a) => a.user_id === uid)?.email ?? uid
  }

  return { actors, emailOf }
}
