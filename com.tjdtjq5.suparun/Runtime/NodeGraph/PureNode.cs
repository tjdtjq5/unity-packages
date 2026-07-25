namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 값을 계산해 다른 노드의 **입력칸**으로 흘려보내는 노드. 실행 흐름에 놓이지 않는다.
    ///
    /// 그래서 <see cref="ExecNode{TCtx}"/> 가 아니라 <see cref="Node{TCtx}"/> 직속이다 —
    /// 나가는 실행 포트도 `Execute` 도 없다. 값이 필요할 때 실행기가 거슬러 올라와 평가한다(pull).
    ///
    /// 출력은 **하나**로 제한한다. 다중 출력은 포트 이름 규약이 하나 더 필요해지는데
    /// 얻는 것에 비해 카탈로그·캔버스·타입검사가 모두 복잡해진다.
    /// TOut 이 곧 포트 타입이라 `NodeValue&lt;float&gt;` 칸에는 `PureNode&lt;TCtx,float&gt;` 만 꽂힌다.
    /// </summary>
    public abstract class PureNode<TCtx, TOut> : Node<TCtx>
    {
        public abstract TOut Evaluate(NodeGraphRunner<TCtx> runner, ref TCtx ctx);
    }
}
