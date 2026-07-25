namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 조건에 따라 **한 갈래만** 간다.
    ///
    /// 포트와 골격은 여기서 고정하고 파생은 <see cref="Evaluate"/> 만 채운다 —
    /// 확률 분기든 처치 여부든 "조건을 판정한다"는 부분만 다르기 때문이다.
    ///
    /// <code>
    /// public class ChanceNode : BranchNode&lt;SkillCtx&gt;
    /// {
    ///     public NodeValue&lt;float&gt; probability;
    ///     protected override bool Evaluate(NodeGraphRunner&lt;SkillCtx&gt; r, ref SkillCtx c)
    ///         =&gt; c.NextFloat() &lt; r.Resolve(probability, ref c);
    /// }
    /// </code>
    /// </summary>
    public abstract class BranchNode<TCtx> : FlowNode<TCtx>
    {
        [NodeOut("참")] public int onTrue = -1;
        [NodeOut("거짓")] public int onFalse = -1;

        protected abstract bool Evaluate(NodeGraphRunner<TCtx> runner, ref TCtx ctx);

        public sealed override int Execute(NodeGraphRunner<TCtx> runner, ref TCtx ctx)
            => Evaluate(runner, ref ctx) ? onTrue : onFalse;
    }
}
