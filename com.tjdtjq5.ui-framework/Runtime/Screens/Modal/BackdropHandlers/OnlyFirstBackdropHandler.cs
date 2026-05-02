using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers
{
    /// <summary>첫 번째 모달만 backdrop을 표시. 추가 모달은 overlay.</summary>
    internal sealed class OnlyFirstBackdropHandler : IModalBackdropHandler
    {
        readonly ModalBackdrop _prefab;

        public OnlyFirstBackdropHandler(ModalBackdrop prefab)
        {
            _prefab = prefab;
        }

        public UniTask BeforeModalEnterAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
        {
            if (modalIndex != 0) return UniTask.CompletedTask;

            var parent = (RectTransform)modal.transform.parent;
            var backdrop = Object.Instantiate(_prefab);
            backdrop.Setup(parent, modalIndex);
            backdrop.transform.SetSiblingIndex(0);
            return backdrop.EnterAsync(playAnimation, ct);
        }

        public void AfterModalEnter(Modal modal, int modalIndex, bool playAnimation) { }

        public UniTask BeforeModalExitAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
        {
            if (modalIndex != 0) return UniTask.CompletedTask;

            var backdrop = GetFirstBackdrop(modal);
            return backdrop != null ? backdrop.ExitAsync(playAnimation, ct) : UniTask.CompletedTask;
        }

        public void AfterModalExit(Modal modal, int modalIndex, bool playAnimation)
        {
            if (modalIndex != 0) return;
            var backdrop = GetFirstBackdrop(modal);
            if (backdrop != null) Object.Destroy(backdrop.gameObject);
        }

        static ModalBackdrop GetFirstBackdrop(Modal modal)
        {
            if (modal == null || modal.transform.parent == null || modal.transform.parent.childCount == 0)
                return null;
            return modal.transform.parent.GetChild(0).GetComponent<ModalBackdrop>();
        }
    }
}
