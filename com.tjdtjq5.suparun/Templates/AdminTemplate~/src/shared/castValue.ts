/**
 * 셀 입력 문자열을 필드 타입에 맞는 값으로 변환한다. 바닐라 castValue 를 그대로 옮겼다.
 *
 * `float` 가 없는 것은 원본 그대로다 — 소수 입력은 문자열로 남아 서버가 파싱한다.
 * (입력 중인 "1." 같은 중간 상태를 0 으로 뭉개지 않으려는 것으로 보인다.)
 */
export function castValue(v: string, type: string): unknown {
  if (type === 'int' || type === 'long') return parseInt(v) || 0
  if (type === 'number') return parseFloat(v) || 0
  return v
}
