namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 본문을 N번 반복한 뒤 완료 갈래로 빠진다. 연쇄·확산이 여기 속한다.
    ///
    /// 포트를 둘로 나눈 이유(Blueprint ForLoop 와 같은 형태):
    ///   body      — 반복할 본문. 매 회차 여기부터 다시 시작한다
    ///   completed — 반복이 다 끝난 뒤 이어갈 곳
    /// "뒤따르는 체인 전체가 본문" 규칙이면 반복 후 마무리를 표현할 수 없다.
    ///
    /// ⚠ <see cref="SequenceNode{TCtx}"/> 와 같은 이유로 실행기가 이 타입을 알아본다 —
    ///   "본문이 끝나면 여기로 돌아오라"를 반환값 하나로는 알릴 수 없다.
    /// </summary>
    public abstract class LoopNode<TCtx> : FlowNode<TCtx>
    {
        [NodeOut("본문")] public int body = -1;
        [NodeOut("완료")] public int completed = -1;

        /// <summary>본문을 몇 번 돌지. 실행기가 반복 시작 시 한 번만 묻는다.</summary>
        public abstract int Count(NodeGraphRunner<TCtx> runner, ref TCtx ctx);

        /// <summary>
        /// 회차마다 컨텍스트를 갈아끼운다(연쇄 대상 교체 등). index 는 0부터.
        ///
        /// **false 를 돌려주면 그 회차부터 반복을 멈추고 완료 갈래로 빠진다.**
        /// 횟수는 시작할 때 정해지지만 도중에 대상이 떨어질 수 있기 때문이다 —
        /// 연쇄 5회를 걸어도 주변에 적이 둘뿐이면 3회차부터는 갈 곳이 없고,
        /// 폭발이 적을 죽이면 다음 회차의 후보가 사라진다.
        /// </summary>
        public abstract bool Enter(NodeGraphRunner<TCtx> runner, ref TCtx ctx, int index);

        public sealed override int Execute(NodeGraphRunner<TCtx> runner, ref TCtx ctx) => body;
    }
}
