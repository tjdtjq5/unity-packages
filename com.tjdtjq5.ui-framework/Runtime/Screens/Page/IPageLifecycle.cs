using Cysharp.Threading.Tasks;

namespace Tjdtjq5.UIFramework.Screens.Page
{
    /// <summary>
    /// Page 라이프사이클 (8-step). history stack을 가진 시퀀스 화면용.
    /// push/pop을 구분하므로 같은 화면이 push 진입과 pop 복귀 시 다른 동작 가능.
    /// </summary>
    public interface IPageLifecycle
    {
        /// <summary>등록(첫 로드) 시 1회 호출. 자원 준비.</summary>
        UniTask Initialize();

        /// <summary>push 진입 직전. UI 상태 셋업.</summary>
        UniTask WillPushEnter();

        /// <summary>push 진입 직후 (transition 완료 후).</summary>
        void DidPushEnter();

        /// <summary>push 시 이전 페이지가 나가기 직전.</summary>
        UniTask WillPushExit();

        /// <summary>push 시 이전 페이지가 나간 직후.</summary>
        void DidPushExit();

        /// <summary>pop 시 페이지가 다시 보이기 직전 (history에서 복귀).</summary>
        UniTask WillPopEnter();

        /// <summary>pop 시 페이지가 다시 보인 직후.</summary>
        void DidPopEnter();

        /// <summary>pop 시 현재 페이지가 나가기 직전.</summary>
        UniTask WillPopExit();

        /// <summary>pop 시 현재 페이지가 나간 직후.</summary>
        void DidPopExit();

        /// <summary>컨테이너 파괴 또는 page release 시 1회 호출. 자원 정리.</summary>
        UniTask Cleanup();
    }
}
