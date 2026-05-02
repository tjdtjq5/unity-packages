using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers
{
    /// <summary>
    /// 모달마다 backdrop 인스턴스 생성. sibling index = modalIndex * 2 (backdrop), modalIndex * 2 + 1 (modal).
    /// </summary>
    internal sealed class GeneratePerModalBackdropHandler : IModalBackdropHandler
    {
        readonly ModalBackdrop _prefab;

        public GeneratePerModalBackdropHandler(ModalBackdrop prefab)
        {
            _prefab = prefab;
        }

        public UniTask BeforeModalEnterAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
        {
            var parent = (RectTransform)modal.transform.parent;
            var backdrop = Object.Instantiate(_prefab);
            backdrop.Setup(parent, modalIndex);
            backdrop.transform.SetSiblingIndex(modalIndex * 2);
            return backdrop.EnterAsync(playAnimation, ct);
        }

        public void AfterModalEnter(Modal modal, int modalIndex, bool playAnimation) { }

        public UniTask BeforeModalExitAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
        {
            var backdrop = GetBackdrop(modal, modalIndex);
            return backdrop != null ? backdrop.ExitAsync(playAnimation, ct) : UniTask.CompletedTask;
        }

        public void AfterModalExit(Modal modal, int modalIndex, bool playAnimation)
        {
            var backdrop = GetBackdrop(modal, modalIndex);
            if (backdrop != null) Object.Destroy(backdrop.gameObject);
        }

        static ModalBackdrop GetBackdrop(Modal modal, int modalIndex)
        {
            if (modal == null || modal.transform.parent == null) return null;
            var backdropSiblingIndex = modalIndex * 2;
            if (backdropSiblingIndex >= modal.transform.parent.childCount) return null;
            return modal.transform.parent.GetChild(backdropSiblingIndex).GetComponent<ModalBackdrop>();
        }
    }
}
