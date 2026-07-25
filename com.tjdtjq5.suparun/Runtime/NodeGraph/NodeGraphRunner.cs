namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 노드 배열을 진입점부터 순회한다. 그래프 구조가 노드 안에 있으므로(`[NodeOut] int`)
    /// 실행기는 배열과 진입 인덱스만 있으면 된다.
    ///
    /// **재귀를 쓰지 않는다** — 작업 스택을 생성 시 1회 할당하고 반복문으로 돈다.
    /// 결정론 시뮬레이션(Quantum 등)에서 실행 중 할당이 생기지 않게 하기 위해서다.
    ///
    /// ⚠ **재진입 불가.** 스택이 인스턴스 필드라 같은 Runner 로 중첩 실행하면 안 된다.
    ///   Runner 는 상태를 갖지 않는 그래프 1개당 1개로 만들어 재사용한다.
    /// </summary>
    public sealed class NodeGraphRunner<TCtx>
    {
        /// <summary>총 노드 실행 횟수 상한. 사이클이 없어도 중첩 반복으로 불어날 수 있다.</summary>
        public const int DefaultMaxSteps = 256;

        /// <summary>Sequence·Loop 중첩 깊이 상한.</summary>
        public const int DefaultMaxDepth = 16;

        /// <summary>PureNode 가 다른 PureNode 를 참조하는 깊이 상한(순환 방어).</summary>
        public const int MaxResolveDepth = 8;

        const int Halt = int.MinValue;

        // 단순 복귀(Sequence 의 남은 갈래) 와 루프 프레임을 같은 스택에 담는다.
        struct StackEntry
        {
            public bool IsLoop;
            public int Target;   // 단순 복귀: 갈 곳 / 루프: 루프 노드 인덱스
            public int Iter;     // 루프: 다음 회차
            public int Count;    // 루프: 총 횟수
        }

        readonly Node<TCtx>[] _nodes;
        readonly int _entry;
        readonly int _maxSteps;
        readonly StackEntry[] _stack;

        int _sp;
        int _budget;
        int _resolveDepth;

        /// <summary>상한에 걸려 잘려나간 흐름이 있었는지. 진단용이며 <see cref="Run"/> 마다 초기화된다.</summary>
        public bool Truncated { get; private set; }

        public NodeGraphRunner(Node<TCtx>[] nodes, int entry,
            int maxSteps = DefaultMaxSteps, int maxDepth = DefaultMaxDepth)
        {
            _nodes = nodes ?? new Node<TCtx>[0];
            _entry = entry;
            _maxSteps = maxSteps;
            _stack = new StackEntry[maxDepth];
        }

        public void Run(ref TCtx ctx)
        {
            _sp = 0;
            _budget = _maxSteps;
            _resolveDepth = 0;
            Truncated = false;

            int cur = _entry;
            while (true)
            {
                if (cur < 0)
                {
                    cur = PopNext(ref ctx);
                    if (cur == Halt) return;
                    continue;
                }

                if (cur >= _nodes.Length) return;   // 손상된 인덱스 — 조용히 멈춘다
                if (_budget-- <= 0) { Truncated = true; return; }

                var node = _nodes[cur];

                // 팬아웃 — 남은 갈래를 스택에 쌓고 첫 갈래로 간다.
                if (node is SequenceNode<TCtx> seq)
                {
                    cur = EnterSequence(seq);
                    continue;
                }

                // 반복 — 본문으로 들어가고 복귀 프레임을 남긴다.
                if (node is LoopNode<TCtx> loop)
                {
                    cur = EnterLoop(cur, loop, ref ctx);
                    continue;
                }

                // PureNode 가 실행 흐름에 놓였다면 그래프 구성 오류다. 그 갈래만 끝낸다.
                var exec = node as ExecNode<TCtx>;
                cur = exec != null ? exec.Execute(this, ref ctx) : -1;
            }
        }

        int EnterSequence(SequenceNode<TCtx> seq)
        {
            var steps = seq.steps;
            if (steps == null || steps.Length == 0) return -1;

            // LIFO 라 뒤쪽부터 쌓아야 앞쪽이 먼저 나온다.
            for (int i = steps.Length - 1; i >= 1; i--)
                Push(new StackEntry { IsLoop = false, Target = steps[i] });

            return steps[0];
        }

        int EnterLoop(int index, LoopNode<TCtx> loop, ref TCtx ctx)
        {
            int count = loop.Count(this, ref ctx);
            if (count <= 0) return loop.completed;

            loop.Enter(this, ref ctx, 0);
            Push(new StackEntry { IsLoop = true, Target = index, Iter = 1, Count = count });
            return loop.body;
        }

        /// <summary>체인이 끝났을 때 스택에서 다음 할 일을 꺼낸다. 스택이 비면 <see cref="Halt"/>.</summary>
        int PopNext(ref TCtx ctx)
        {
            if (_sp <= 0) return Halt;

            var e = _stack[--_sp];
            if (!e.IsLoop) return e.Target;

            var loop = _nodes[e.Target] as LoopNode<TCtx>;
            if (loop == null) return -1;

            if (e.Iter >= e.Count) return loop.completed;

            loop.Enter(this, ref ctx, e.Iter);
            e.Iter++;
            Push(e);
            return loop.body;
        }

        void Push(StackEntry e)
        {
            if (_sp >= _stack.Length) { Truncated = true; return; }   // 깊이 초과 — 그 갈래를 버린다
            _stack[_sp++] = e;
        }

        /// <summary>입력칸 하나를 값으로 푼다. 상수면 그대로, 연결이면 해당 PureNode 를 평가한다.</summary>
        public T Resolve<T>(NodeValue<T> value, ref TCtx ctx)
        {
            if (value.source < 0 || value.source >= _nodes.Length) return value.constant;
            if (_resolveDepth >= MaxResolveDepth) { Truncated = true; return value.constant; }

            var pure = _nodes[value.source] as PureNode<TCtx, T>;
            if (pure == null) return value.constant;

            _resolveDepth++;
            var result = pure.Evaluate(this, ref ctx);
            _resolveDepth--;
            return result;
        }
    }
}
