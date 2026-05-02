using System;

namespace Tjdtjq5.UIFramework.Screens.Modal.BackdropHandlers
{
    internal static class ModalBackdropHandlerFactory
    {
        /// <summary>strategy + prefab 조합으로 적절한 핸들러 생성. prefab이 null이면 NoBackdropHandler.</summary>
        public static IModalBackdropHandler Create(ModalBackdropStrategy strategy, ModalBackdrop prefab)
        {
            if (prefab == null) return new NoBackdropHandler();

            return strategy switch
            {
                ModalBackdropStrategy.GeneratePerModal => new GeneratePerModalBackdropHandler(prefab),
                ModalBackdropStrategy.OnlyFirstBackdrop => new OnlyFirstBackdropHandler(prefab),
                ModalBackdropStrategy.ChangeOrderBeforeAnimation =>
                    new ChangeOrderBackdropHandler(prefab, ChangeOrderBackdropHandler.ChangeTiming.BeforeAnimation),
                ModalBackdropStrategy.ChangeOrderAfterAnimation =>
                    new ChangeOrderBackdropHandler(prefab, ChangeOrderBackdropHandler.ChangeTiming.AfterAnimation),
                _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
            };
        }
    }
}
