using System.Threading;
using Cysharp.Threading.Tasks;

namespace Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers
{
    /// <summary>
    /// Modal backdrop 전략 추상화. ModalContainer가 push/pop의 4단계에서 호출.
    /// 구현체는 strategy에 따라 backdrop 인스턴스화·재사용·sibling index 이동을 담당.
    /// </summary>
    internal interface IModalBackdropHandler
    {
        /// <summary>모달 enter 직전. backdrop 생성/표시/이동.</summary>
        UniTask BeforeModalEnterAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct);

        /// <summary>모달 enter 직후. ChangeOrder 전략에서 사용.</summary>
        void AfterModalEnter(Modal modal, int modalIndex, bool playAnimation);

        /// <summary>모달 exit 직전. backdrop 숨김/이동.</summary>
        UniTask BeforeModalExitAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct);

        /// <summary>모달 exit 직후. backdrop 파괴/이동.</summary>
        void AfterModalExit(Modal modal, int modalIndex, bool playAnimation);
    }
}
