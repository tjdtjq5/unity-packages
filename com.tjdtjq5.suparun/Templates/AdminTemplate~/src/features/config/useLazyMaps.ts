import { useEffect, useState } from 'react'
import { configApi } from '../../shared/api'

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

function loadIcons(): Promise<IconMap> {
  iconPromise ??= configApi<IconMap>('/_icons')
    .catch(() => ({}) as IconMap)
    .then((m) => {
      iconCache = m
      return m
    })
  return iconPromise
}

function loadComponents(): Promise<ComponentMap> {
  componentPromise ??= configApi<ComponentMap>('/_components')
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
