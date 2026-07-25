import { useEffect, useState } from 'react'
import { loadMeta } from '../../shared/meta'

export interface IconEntry {
  name: string
  thumb: string
}

/** atlasKey → 아이콘 목록 */
type IconMap = Record<string, IconEntry[]>
/** "<FullName>" → 어드레서블 주소 목록 */
type ComponentMap = Record<string, string[]>

// 모듈 스코프 캐시 — 바닐라의 iconMap/componentMap 전역과 같은 역할.
// 화면을 다시 열어도 재요청하지 않는다.
let iconCache: IconMap | null = null
let componentCache: ComponentMap | null = null
let iconPromise: Promise<IconMap> | null = null
let componentPromise: Promise<ComponentMap> | null = null

// 서버 `/_icons` · `/_components` 대신 suparun_meta 에서 읽는다 (ADR-0004).
// Unity 가 **어드민을 여는 시점**에 SpriteAtlas 를 구워 넣는다 — 어드민을 한 번도 안 열었으면
// 비어 있고, 그때는 아이콘이 텍스트로 표시된다(graceful).

function loadIcons(): Promise<IconMap> {
  iconPromise ??= loadMeta(['icons'])
    .then((m) => (m.icons as IconMap | undefined) ?? {})
    .catch(() => ({}) as IconMap)
    .then((m) => {
      iconCache = m
      return m
    })
  return iconPromise
}

function loadComponents(): Promise<ComponentMap> {
  componentPromise ??= loadMeta(['components'])
    .then((m) => (m.components as ComponentMap | undefined) ?? {})
    .catch(() => ({}) as ComponentMap)
    .then((m) => {
      componentCache = m
      return m
    })
  return componentPromise
}

/**
 * `[Icon]` 아틀라스 썸네일 맵. 바닐라 ensureIconsLoaded() 를 대체한다.
 *
 * 바닐라는 로드가 끝나면 renderTable() 을 다시 불러 표 전체를 재조립했다.
 * 여기서는 상태가 바뀌면 이 훅을 쓰는 셀만 다시 그려진다.
 */
export function useIconMap(enabled: boolean): IconMap | null {
  const [map, setMap] = useState<IconMap | null>(iconCache)
  useEffect(() => {
    if (!enabled || map) return
    let alive = true
    void loadIcons().then((m) => {
      if (alive) setMap(m)
    })
    return () => {
      alive = false
    }
  }, [enabled, map])
  return map
}

/** `[Component]` 어드레서블 주소 맵. 바닐라 ensureComponentsLoaded() 를 대체한다. */
export function useComponentMap(enabled: boolean): ComponentMap | null {
  const [map, setMap] = useState<ComponentMap | null>(componentCache)
  useEffect(() => {
    if (!enabled || map) return
    let alive = true
    void loadComponents().then((m) => {
      if (alive) setMap(m)
    })
    return () => {
      alive = false
    }
  }, [enabled, map])
  return map
}
