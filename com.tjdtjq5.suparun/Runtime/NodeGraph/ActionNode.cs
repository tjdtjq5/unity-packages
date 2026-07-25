namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 부작용을 일으키는 노드(피해·넉백·소환 등). 나가는 연결이 하나뿐이다.
    ///
    /// <see cref="Execute"/> 를 sealed 로 막고 파생은 <see cref="On"/> 만 채운다 —
    /// "실행하고 next 로 넘어간다"는 골격을 파생이 잘못 구현할 여지를 없앤다.
    /// </summary>
    public abstract class ActionNode<TCtx> : ExecNode<TCtx>
    {
        [NodeOut] public int next = -1;

        protected abstract void On(NodeGraphRunner<TCtx> runner, ref TCtx ctx);

        public sealed override int Execute(NodeGraphRunner<TCtx> runner, ref TCtx ctx)
        {
            On(runner, ref ctx);
            return next;
        }
    }
}
