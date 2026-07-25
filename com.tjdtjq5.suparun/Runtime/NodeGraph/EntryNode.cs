namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 그래프의 시작점. 그래프당 정확히 1개이고 들어오는 연결이 없다.
    /// 파생 타입은 "언제 시작하는가"를 이름으로 구분한다(OnHit / OnCast 등).
    /// </summary>
    public abstract class EntryNode<TCtx> : ExecNode<TCtx>
    {
        [NodeOut] public int next = -1;

        public override int Execute(NodeGraphRunner<TCtx> runner, ref TCtx ctx) => next;
    }
}
