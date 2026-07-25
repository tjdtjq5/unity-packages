namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 컬럼 JSON 을 푼 결과. 그래프를 돌리려면 <see cref="NodeGraphRunner{TCtx}"/> 에 넘긴다.
    /// </summary>
    public sealed class NodeGraphData<TCtx>
    {
        /// <summary>노드 배열. 카탈로그에 없는 타입이 섞여 있으면 그 자리는 null 이다.</summary>
        public Node<TCtx>[] Nodes;

        /// <summary>진입 노드 인덱스. 비어 있는 그래프면 -1.</summary>
        public int Entry;

        /// <summary>복원하지 못한 타입 이름들. 비어 있으면 온전히 복원된 것이다.</summary>
        public string[] UnknownTypes;

        public bool IsEmpty => Nodes == null || Nodes.Length == 0;

        /// <summary>구멍(null 노드) 없이 전부 복원됐는지.</summary>
        public bool IsComplete => UnknownTypes == null || UnknownTypes.Length == 0;

        public NodeGraphRunner<TCtx> CreateRunner() => new NodeGraphRunner<TCtx>(Nodes, Entry);
    }
}
