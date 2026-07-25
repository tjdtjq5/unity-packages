import type { ConfigField, ConfigRow, FieldCondition } from '../../shared/types'

/**
 * `[VisibleIf]` / `[HiddenIf]` 판정. 바닐라 isFieldDisabled() / isJsonFieldDisabled() 와 동일한 규칙이다.
 * 표 셀과 JSON 모달 행이 같은 로직을 쓰므로 조건만 받는 형태로 일반화했다.
 *
 * 바닐라에서는 조건이 바뀔 때마다 `refreshRowConditions()` 가 DOM 을 찾아
 * 해당 셀만 innerHTML 로 갈아끼웠다. React 에서는 이 함수가 렌더 중에 호출되므로
 * 상태가 바뀌면 자동으로 반영된다 — refreshRowConditions 는 통째로 사라진다.
 */
export function isConditionDisabled(
  row: Record<string, unknown>,
  visibleIf?: FieldCondition,
  hiddenIf?: FieldCondition,
): boolean {
  if (visibleIf) {
    const v = row[visibleIf.field]
    const values = visibleIf.values
    if (values && values.length > 0) {
      if (!values.includes(String(v))) return true
    } else if (!v) {
      return true
    }
  }
  if (hiddenIf) {
    const v = row[hiddenIf.field]
    const values = hiddenIf.values
    if (values && values.length > 0) {
      if (values.includes(String(v))) return true
    } else if (v) {
      return true
    }
  }
  return false
}

export function isFieldDisabled(row: ConfigRow, field: ConfigField): boolean {
  return isConditionDisabled(row, field.visibleIf, field.hiddenIf)
}
