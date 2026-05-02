using Cysharp.Threading.Tasks;

namespace Tjdtjq5.UIFramework.Screens.Modal
{
    /// <summary>
    /// Modal 라이프사이클 (8-step). Page와 동일한 시그니처지만 의미가 다름:
    /// - Modal은 push 시 이전 modal이 그대로 보이는 상태 유지 (backdrop만 추가됨)
    /// - PushExit / PopEnter는 lifecycle hook 호출만 (시각 변화 없음)
    /// - PushEnter / PopExit만 실제 transition 애니메이션 동반
    /// </summary>
    public interface IModalLifecycle
    {
        /// <summary>등록(첫 로드) 시 1회 호출.</summary>
        UniTask Initialize();

        /// <summary>push 진입 직전.</summary>
        UniTask WillPushEnter();

        /// <summary>push 진입 직후.</summary>
        void DidPushEnter();

        /// <summary>push 시 이전 modal이 background로 가기 직전 (시각 변화 없음).</summary>
        UniTask WillPushExit();

        /// <summary>push 시 이전 modal이 background로 간 직후.</summary>
        void DidPushExit();

        /// <summary>pop 시 modal이 다시 top으로 올라오기 직전 (시각 변화 없음).</summary>
        UniTask WillPopEnter();

        /// <summary>pop 시 modal이 다시 top으로 올라온 직후.</summary>
        void DidPopEnter();

        /// <summary>pop 시 현재 modal이 닫히기 직전.</summary>
        UniTask WillPopExit();

        /// <summary>pop 시 현재 modal이 닫힌 직후.</summary>
        void DidPopExit();

        /// <summary>컨테이너 파괴 또는 release 시 1회 호출.</summary>
        UniTask Cleanup();
    }
}
