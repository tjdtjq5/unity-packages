# NodeGraph Feature

- **상태**: 인프라 완료 (계층·실행기·역직렬화·카탈로그·어드민 캔버스)
- **용도**: 어드민 웹에서 저작하는 노드 그래프의 런타임 계층. 소비자가 노드를 정의하면
  카탈로그가 어드민으로 흘러가고, 저장된 JSON 이 다시 C# 객체로 돌아온다.
- **설계**: `docs/adr/0002-skill-node-graph.md` (소비 프로젝트)

## 의존성

**`Tjdtjq5.SupaRun.NodeGraph` 는 의존성이 0 이다** — `noEngineReferences: true` 라 `UnityEngine`
조차 참조하지 않는다. Quantum 시뮬레이션이 이 어셈블리를 참조해야 하기 때문이다
(`Quantum.Simulation.asmdef` 는 `references: []` 가 기본이라 무거운 어셈블리를 끌어들이면 안 된다).

JSON 을 다루는 둘(`NodeGraphSerializer` · `NodeValueConverter`)만 Newtonsoft 가 필요해
`Runtime/Serialization/` 으로 빼 **`Tjdtjq5.SupaRun.Runtime` 소속**으로 뒀다.
시뮬레이션에서 문자열 파싱을 할 일이 없기도 하다.

```
Quantum.Simulation ──▶ Tjdtjq5.SupaRun.NodeGraph  (순수 C#, 의존성 0)
                              ▲
Tjdtjq5.SupaRun.Runtime ──────┘   Serialization/ (Newtonsoft)
Tjdtjq5.SupaRun.Editor  ──────┘   카탈로그 생성
```

> ⚠ `Quantum.Simulation.asmdef` 는 Quantum SDK 를 재임포트하면 덮어써진다.
> 그러면 이 참조가 사라져 컴파일이 깨진다 — 한 줄 다시 넣으면 된다.

### 소비 프로젝트가 할 일

노드를 정의하는 코드가 **asmdef 안에 있으면 `Tjdtjq5.SupaRun.NodeGraph` 를 명시로 참조해야 한다.**
`autoReferenced: true` 는 predefined assembly(Assembly-CSharp)에만 적용되고, asmdef 어셈블리끼리는
자동으로 이어지지 않는다. 또 **asmdef 참조는 전이되지 않아** `Tjdtjq5.SupaRun.Runtime` 을
참조하고 있어도 NodeGraph 는 따로 넣어야 한다.

```jsonc
// 게임 asmdef
"references": [
  "Tjdtjq5.SupaRun.Runtime",
  "Tjdtjq5.SupaRun.NodeGraph",   // ← 이게 없으면 NodeValue<> 부터 CS0246
]
```

## 구조

```
Runtime/NodeGraph/               ← Tjdtjq5.SupaRun.NodeGraph (순수)
├── Node.cs                  # 뿌리. TCtx 가 그래프 종류를 가른다
├── ExecNode.cs              # 실행 흐름 위의 노드. 반환값 = 다음 인덱스, -1 = 종료
├── EntryNode.cs             # 진입점 (그래프당 1개)
├── ActionNode.cs            # 부작용. On() 만 채우면 된다
├── FlowNode.cs              # 흐름 제어 (아래 3종의 부모)
├── BranchNode.cs            # 한 갈래만 간다. Evaluate() 만 채운다
├── SequenceNode.cs          # 전부 간다 (팬아웃)
├── LoopNode.cs              # 본문을 N번. body / completed 포트
├── PureNode.cs              # 값 계산. 실행 흐름에 없다
├── NodeValue.cs             # 입력칸 — 상수 또는 PureNode 연결
├── NodeGraphRunner.cs       # 순회기. 재귀 없이 고정 스택
├── NodeGraphData.cs         # Parse 결과 (노드 배열 + 진입점 + 미복원 타입)
├── NodeGraphAttribute.cs    # [NodeGraph(typeof(TCtx))] — 컬럼을 캔버스로
└── NodeOutAttribute.cs      # [NodeOut] — int 필드가 실행 포트

Runtime/Serialization/           ← Tjdtjq5.SupaRun.Runtime (Newtonsoft)
├── NodeGraphSerializer.cs   # 컬럼 JSON ↔ C# 객체
│                            #  Parse/Serialize       — 그래프(노드 배열 + 진입점)
│                            #  ParseOne/SerializeOne — [Polymorphic] 값 하나
└── NodeValueConverter.cs    # NodeValue<T> ↔ `25` / `{"$node":3}`
```

> 카탈로그는 `suparun_meta` 의 **`type_catalog`** 한 키에 노드와 다형 타입이 함께 실린다.
> 둘은 같은 것이다 — "base 의 파생 중 하나를 고르고 필드를 채운다".
> 노드 그래프는 거기에 연결을 얹은 것이라 `outs` 를 쓰고, 다형 필드는 `outs` 가 빈 배열이다.

```
```

어트리뷰트가 계층과 같은 어셈블리에 있는 이유는 `[NodeOut]` 이 **노드 클래스에 붙기** 때문이다.
시뮬레이션에 정의된 노드가 그 어트리뷰트를 보려면 같은 어셈블리이거나 참조 가능해야 한다.

## 두 축이 직교한다

| 축 | 가르는 것 | 예 |
|---|---|---|
| **그래프 종류** | 제네릭 인자 `TCtx` | `Node<SkillCtx>` vs `Node<TutorialCtx>` |
| **역할** | 상속 | `ActionNode<T>` vs `BranchNode<T>` |

단일 상속으로 둘 다 담을 수 없어 한쪽을 제네릭으로 뺐다.
그래프를 늘려도 역할 계층을 다시 만들 필요가 없다.

## 쓰는 법

```csharp
public struct SkillCtx { public Frame f; public EntityRef target; }

public class DamageNode : ActionNode<SkillCtx>
{
    public NodeValue<int> amount;                       // 상수 또는 PureNode 출력
    protected override void On(NodeGraphRunner<SkillCtx> r, ref SkillCtx c)
        => HealthApi.ApplyDamage(c.f, c.target, r.Resolve(amount, ref c));
}

public class ChanceNode : BranchNode<SkillCtx>
{
    public NodeValue<float> probability;
    protected override bool Evaluate(NodeGraphRunner<SkillCtx> r, ref SkillCtx c)
        => c.f.RNG->Next() < r.Resolve(probability, ref c);
}

[SpecData("InGame")]
public class SkillData
{
    [PrimaryKey] public string id;
    [NodeGraph(typeof(SkillCtx))] public string effect_graph;
}
```

읽을 때:

```csharp
var data = NodeGraphSerializer.Parse<SkillCtx>(row.effect_graph);
var runner = data.CreateRunner();       // 그래프당 1개 만들어 재사용
runner.Run(ref ctx);
```

> `Parse` 는 모르는 `type` 을 만나도 예외를 던지지 않고 그 자리를 null 로 둔다.
> 노드 클래스 이름을 바꾸면 기존 그래프가 옛 이름을 들고 있게 되는데, 그때
> **나머지는 읽히고 그 노드만 조용히 빠진다**. `UnknownTypes` 를 확인해야 드러난다.

## 저장 형식

```jsonc
{ "nodes":[ {"type":"DamageNode","amount":25,"next":1},
            {"type":"ChanceNode","probability":0.3,"onTrue":2,"onFalse":-1} ],
  "entry": 0,
  "layout":[ {"x":80,"y":40}, {"x":320,"y":40} ] }
```

- 연결이 **노드의 필드**다 — `edges` 배열이 따로 없어 한 겹으로 끝난다
- `layout` 은 캔버스 좌표라 `Parse` 가 무시한다. 노드를 옮긴 것만으로 게임이 달라지면 안 된다
- 모르는 `type` 은 예외 대신 그 자리를 null 로 두고 `UnknownTypes` 에 모은다

## 주의사항

- **`NodeGraphRunner` 는 재진입 불가.** 작업 스택이 인스턴스 필드라 중첩 실행하면 안 된다.
  그래프당 1개를 만들어 재사용한다.
- **실행기가 `SequenceNode`·`LoopNode` 를 타입으로 알아본다.** 반환값 하나로는 팬아웃과
  반복 복귀를 알릴 수 없어서다. 이 두 역할을 늘리려면 실행기도 함께 고쳐야 한다.
- **상한 3종** — 총 실행 횟수(256) · 중첩 깊이(16) · Pure 참조 깊이(8).
  걸리면 예외 대신 `Truncated` 가 서고 그 갈래만 잘린다. 결정론 시뮬레이션에서
  무한 루프는 프레임을 멈추므로 던지지 않고 끊는 쪽을 택했다.
- **카탈로그 수집은 정렬한다** — `TypeCache` 는 순서를 보장하지 않아 정렬을 빼면
  컴파일마다 "스키마 변경됨" 으로 뜨고 팔레트 순서가 흔들린다.
- **`[NodeGraph]` 는 서버로 안 간다** — `[SpecData]` 클래스는 소스째 서버로 복사돼 컴파일되는데,
  컨텍스트 타입(`TestCtx` 등)이 게임 어셈블리에만 있어 빌드가 깨진다.
  `DeployManager.StripForServer` 가 `[Icon]`·`[Component]` 와 함께 걷어낸다 —
  서버에게 그 컬럼은 그냥 `string` 이다. 노드 클래스 자체는 `[SpecData]` 가 아니라 복사되지 않는다.
- **런타임 타입 조회는 어셈블리 스캔**이다(TypeCache 는 에디터 전용). 컨텍스트당 1회 캐시된다.
- ⚠ **IL2CPP 미검증** — 타입을 이름으로 찾으므로 스트리핑되면 복원이 실패할 수 있다.
  `link.xml` 또는 `[Preserve]` 가 필요한지 실기 빌드에서 확인해야 한다.
