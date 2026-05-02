using Cysharp.Threading.Tasks;

namespace Tjdtjq5.UIFramework.Screens.Sheet
{
    /// <summary>
    /// Sheet 라이프사이클 (5-step). history 없이 단일 active 화면용 (탭 등).
    /// Page/Modal과 달리 push/pop 구분 없음.
    /// </summary>
    public interface ISheetLifecycle
    {
        /// <summary>등록(첫 로드) 시 1회 호출. 자원 준비.</summary>
        UniTask Initialize();

        /// <summary>표시 직전. UI 상태 셋업.</summary>
        UniTask WillEnter();

        /// <summary>표시 직후 (transition 완료 후).</summary>
        void DidEnter();

        /// <summary>숨김 직전 (transition 시작 전).</summary>
        UniTask WillExit();

        /// <summary>숨김 직후.</summary>
        void DidExit();

        /// <summary>컨테이너 파괴 또는 unregister 시 1회 호출. 자원 정리.</summary>
        UniTask Cleanup();
    }
}
