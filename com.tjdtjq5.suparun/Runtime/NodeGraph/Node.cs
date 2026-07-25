namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 모든 노드의 뿌리. 카탈로그 수집과 <see cref="NodeGraphAttribute"/> 컬럼의 팔레트가
    /// 이 타입을 기준으로 정해진다.
    ///
    /// TCtx 가 그래프 종류를 가른다 — 스킬 효과 그래프와 튜토리얼 그래프는 서로 다른 TCtx 를 쓰므로
    /// 팔레트가 섞이지 않는다. 역할(Entry/Action/Flow/Pure)은 상속이 가르므로 두 축이 직교한다.
    ///
    /// 실행 계약은 여기 두지 않는다 — 실행 흐름에 놓이는 노드(<see cref="ExecNode{TCtx}"/>)와
    /// 값만 계산하는 노드(<see cref="PureNode{TCtx,TOut}"/>)는 계약이 다르다.
    /// </summary>
    public abstract class Node<TCtx>
    {
    }
}
