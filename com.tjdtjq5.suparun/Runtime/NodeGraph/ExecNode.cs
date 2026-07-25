namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 실행 흐름 위에 놓이는 노드. **반환값이 다음 노드의 배열 인덱스**이고 -1 이면 체인이 끝난다.
    ///
    /// 연결을 노드가 직접 들고 있는 것(`[NodeOut] int`)이 이 설계의 핵심이다 —
    /// 별도 edges 배열이 없어 저장 형식이 한 겹이고, 인덱스 하나로 순회가 끝난다.
    ///
    /// runner 를 인자로 받는 이유는 입력칸을 값으로 풀기 위해서다
    /// (<see cref="NodeGraphRunner{TCtx}.Resolve{T}"/>). 노드는 자기 이웃 배열을 모르므로
    /// 실행기를 거쳐야 다른 노드의 출력을 읽을 수 있다.
    /// </summary>
    public abstract class ExecNode<TCtx> : Node<TCtx>
    {
        /// <summary>이 노드를 실행하고 다음 노드 인덱스를 반환한다. -1 = 종료.</summary>
        public abstract int Execute(NodeGraphRunner<TCtx> runner, ref TCtx ctx);
    }
}
