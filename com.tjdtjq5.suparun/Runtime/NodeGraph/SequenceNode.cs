namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 여러 갈래를 **전부** 실행한다(팬아웃). `피해 → [넉백, 이펙트, 사운드]` 같은 형태.
    ///
    /// ⚠ 반환값이 하나뿐인 <see cref="ExecNode{TCtx}.Execute"/> 계약으로는 갈래를 하나밖에 못 알린다.
    ///   그래서 <see cref="NodeGraphRunner{TCtx}"/> 가 이 타입을 알아보고 steps 를 직접 순회한다.
    ///   `Execute` 는 실행기를 안 거치고 불렸을 때를 위한 fallback 으로 첫 갈래만 돌려준다.
    ///
    /// 연결을 노드가 들고 반환값으로 순회하는 설계의 대가다 — 갈래가 하나인 노드까지는 깔끔하지만
    /// 팬아웃부터는 실행기가 타입을 알아야 한다.
    /// </summary>
    public abstract class SequenceNode<TCtx> : FlowNode<TCtx>
    {
        [NodeOut] public int[] steps;

        public sealed override int Execute(NodeGraphRunner<TCtx> runner, ref TCtx ctx)
            => steps != null && steps.Length > 0 ? steps[0] : -1;
    }
}
