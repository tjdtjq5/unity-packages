import { useState } from 'react'
import { Modal } from '../../shared/Modal'
import type { JsonSchemaField } from '../../shared/types'
import {
  JsonEditorModal,
  PolymorphicForm,
  describePolyValue,
  splitPolyValue,
} from './JsonEditor'

/**
 * `[Polymorphic]` 컬럼 편집기 — 타입을 고르고 그 타입의 필드만 채운다.
 *
 * 컬럼 하나가 행마다 다른 뜻을 갖던 구조를 대체한다. 예전에는 공용 컬럼에
 * `[VisibleIf]` 를 달아 가렸다면, 이제 타입마다 자기 이름의 필드를 갖는다.
 *
 * 저장 형태는 노드 하나와 같다: `{"type":"GunPatternData","range":10}`.
 * 실제로 다형 필드는 연결 없는 노드 하나라 카탈로그를 노드 그래프와 공유한다.
 *
 * 폼 자체는 <see cref="PolymorphicForm"/> 이 그린다 — 중첩 layer 로 들어올 때와 같은 화면이다.
 */
export function PolymorphicEditor({
  title,
  base,
  initialJson,
  onSave,
  onClose,
}: {
  title: string
  base: string
  initialJson: string
  onSave(json: string): void
  onClose(): void
}) {
  const initial = splitPolyValue(initialJson)
  const [value, setValue] = useState<Record<string, unknown>>({
    ...initial.values,
    type: initial.type,
  })
  /** 이 값 안의 중첩 필드로 파고들었을 때. 있으면 이 모달 대신 중첩 편집기를 그린다. */
  const [nested, setNested] = useState<JsonSchemaField | null>(null)

  // 표의 다른 셀(토글·드롭다운·FK)이 전부 즉시 저장이라 여기만 확인 버튼을 두면 어긋난다.
  const commit = (next: Record<string, unknown>) => {
    setValue(next)
    const type = String(next.type ?? '')
    if (!type) {
      onSave('')
      return
    }
    const { type: _drop, ...rest } = next
    onSave(JSON.stringify({ type, ...rest }))
  }

  // 다형 값 안의 JSON 리스트·다형 필드 — 모달을 겹치지 않고 갈아 끼운다.
  // 그쪽은 layer 스택이라 더 깊이 들어가도 화면은 하나다.
  if (nested) {
    return (
      <JsonEditorModal
        title={`${title} · ${nested.name}`}
        rootLabel={nested.name}
        schema={nested.jsonSchema ?? []}
        polyBase={nested.polymorphic}
        initialJson={value[nested.name]}
        onSave={(json) => commit({ ...value, [nested.name]: json })}
        onClose={() => setNested(null)}
      />
    )
  }

  return (
    <Modal title={<span className="fw-bold px-2">{title}</span>} maxWidth={560} onClose={onClose}>
      <PolymorphicForm
        base={base}
        value={value}
        onChange={commit}
        onEnterNested={(f) => setNested(f)}
      />
    </Modal>
  )
}

/** 셀에 보일 짧은 요약. 타입명이 없으면 비어 있는 것이다. */
export function describePolymorphic(json: string): string {
  return describePolyValue(json)
}
