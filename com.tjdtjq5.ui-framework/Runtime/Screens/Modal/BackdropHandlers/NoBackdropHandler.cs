using System.Threading;
using Cysharp.Threading.Tasks;

namespace Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers
{
    /// <summary>backdrop 프리팹이 null일 때 사용되는 no-op 핸들러. 모달이 overlay로만 표시됨.</summary>
    internal sealed class NoBackdropHandler : IModalBackdropHandler
    {
        public UniTask BeforeModalEnterAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
            => UniTask.CompletedTask;

        public void AfterModalEnter(Modal modal, int modalIndex, bool playAnimation) { }

        public UniTask BeforeModalExitAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
            => UniTask.CompletedTask;

        public void AfterModalExit(Modal modal, int modalIndex, bool playAnimation) { }
    }
}
