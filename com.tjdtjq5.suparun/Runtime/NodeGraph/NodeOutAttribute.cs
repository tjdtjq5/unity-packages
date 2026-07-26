using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 `int` 필드가 **나가는 실행 포트**임을 표시한다. 값은 대상 노드의 배열 인덱스, -1 은 종료.
    /// `int[]` 에 붙으면 포트가 가변 개수라는 뜻이다(<see cref="SequenceNode{TCtx}"/>).
    ///
    /// 어트리뷰트가 필요한 이유는 리플렉션만으로 구분이 안 되기 때문이다 —
    /// `public int next = -1;` 과 `public int count = 3;` 은 타입이 같아서
    /// 어느 쪽이 연결이고 어느 쪽이 값인지 알 방법이 없다.
    ///
    /// 카탈로그는 이 필드를 `fields` 에서 빼고 `outs` 로 옮긴다 —
    /// 포트는 캔버스 연결이지 사람이 채우는 입력칸이 아니다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NodeOutAttribute : Attribute
    {
        /// <summary>캔버스 포트 표시명. 비우면 필드명을 쓴다.</summary>
        public string Label { get; }

        public NodeOutAttribute(string label = null) => Label = label;
    }
}
