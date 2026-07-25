using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 노드의 입력칸. **상수이거나 다른 <see cref="PureNode{TCtx,TOut}"/> 의 출력**이다.
    ///
    /// 저장 형식에도 그대로 드러난다:
    /// <code>
    /// {"type":"DamageNode","amount":25}            // 상수
    /// {"type":"DamageNode","amount":{"$node":3}}   // 3번 PureNode 의 출력
    /// </code>
    ///
    /// 해석은 실행기가 한다(<see cref="NodeGraphRunner{TCtx}.Resolve{T}"/>) —
    /// 노드는 <see cref="source"/> 가 가리키는 이웃을 직접 찾아갈 수 없다.
    /// </summary>
    [Serializable]
    public struct NodeValue<T>
    {
        public T constant;

        /// <summary>-1 이면 <see cref="constant"/> 를 쓴다. 그 외에는 그 인덱스의 PureNode 출력을 쓴다.</summary>
        public int source;

        public static implicit operator NodeValue<T>(T value)
            => new NodeValue<T> { constant = value, source = -1 };
    }
}
