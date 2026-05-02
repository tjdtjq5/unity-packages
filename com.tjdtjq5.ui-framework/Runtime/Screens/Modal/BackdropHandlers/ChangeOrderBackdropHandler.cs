using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers
{
    /// <summary>
    /// 단일 backdrop을 재사용. modal stack 순서에 맞춰 sibling index 이동.
    /// 첫 모달 push/마지막 모달 pop 시에만 backdrop 인스턴스화/파괴.
    /// </summary>
    internal sealed class ChangeOrderBackdropHandler : IModalBackdropHandler
    {
        public enum ChangeTiming { BeforeAnimation, AfterAnimation }

        readonly ModalBackdrop _prefab;
        readonly ChangeTiming _changeTiming;
        ModalBackdrop _instance;

        public ChangeOrderBackdropHandler(ModalBackdrop prefab, ChangeTiming changeTiming)
        {
            _prefab = prefab;
            _changeTiming = changeTiming;
        }

        public UniTask BeforeModalEnterAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
        {
            var parent = (RectTransform)modal.transform.parent;

            // 첫 모달 — backdrop 새로 생성
            if (modalIndex == 0)
            {
                var backdrop = Object.Instantiate(_prefab);
                backdrop.Setup(parent, modalIndex);
                backdrop.transform.SetSiblingIndex(0);
                _instance = backdrop;
                return backdrop.EnterAsync(playAnimation, ct);
            }

            // 두번째+ — backdrop sibling index 이동
            if (_changeTiming == ChangeTiming.BeforeAnimation && _instance != null)
                _instance.transform.SetSiblingIndex(modalIndex);

            return UniTask.CompletedTask;
        }

        public void AfterModalEnter(Modal modal, int modalIndex, bool playAnimation)
        {
            if (modalIndex == 0) return;
            if (_changeTiming == ChangeTiming.AfterAnimation && _instance != null)
                _instance.transform.SetSiblingIndex(modalIndex);
        }

        public UniTask BeforeModalExitAsync(Modal modal, int modalIndex, bool playAnimation, CancellationToken ct)
        {
            // 마지막 모달이 닫힐 때만 backdrop 애니메이션
            if (modalIndex == 0)
                return _instance != null ? _instance.ExitAsync(playAnimation, ct) : UniTask.CompletedTask;

            // 두번째+ — backdrop을 한 단계 뒤로
            if (_changeTiming == ChangeTiming.BeforeAnimation && _instance != null)
                _instance.transform.SetSiblingIndex(modalIndex - 1);

            return UniTask.CompletedTask;
        }

        public void AfterModalExit(Modal modal, int modalIndex, bool playAnimation)
        {
            if (modalIndex == 0)
            {
                if (_instance != null)
                {
                    Object.Destroy(_instance.gameObject);
                    _instance = null;
                }
                return;
            }

            if (_changeTiming == ChangeTiming.AfterAnimation && _instance != null)
                _instance.transform.SetSiblingIndex(modalIndex - 1);
        }
    }
}
