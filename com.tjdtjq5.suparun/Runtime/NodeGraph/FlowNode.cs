namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 흐름을 조작하는 노드. 나가는 연결이 여럿이고 어디로 갈지는 파생이 정한다.
    ///
    /// 파생 3종의 차이는 **나가는 포트를 어떻게 쓰느냐**다:
    ///   <see cref="BranchNode{TCtx}"/>   — 여러 포트 중 하나만 간다
    ///   <see cref="SequenceNode{TCtx}"/> — 여러 포트를 전부 간다
    ///   <see cref="LoopNode{TCtx}"/>     — 한 포트(본문)를 N번 간 뒤 다른 포트(완료)로 빠진다
    ///
    /// 겉모습(포트 여러 개)은 같고 의미만 다르므로 캔버스는 포트 개수만 알면 되고,
    /// 해석은 노드와 실행기가 갖는다.
    /// </summary>
    public abstract class FlowNode<TCtx> : ExecNode<TCtx>
    {
    }
}
