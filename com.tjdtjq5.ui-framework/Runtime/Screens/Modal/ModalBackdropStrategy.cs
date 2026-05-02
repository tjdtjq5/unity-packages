namespace Tjdtjq5.UIFramework.Screens.Modal
{
    /// <summary>
    /// Modal backdrop 처리 전략. ModalContainer Inspector에서 선택.
    /// </summary>
    public enum ModalBackdropStrategy
    {
        /// <summary>모달마다 새 backdrop 생성. 기본값. 모달별 다른 시각 효과 가능.</summary>
        GeneratePerModal,

        /// <summary>첫 번째 모달만 backdrop. 추가 모달은 overlay.</summary>
        OnlyFirstBackdrop,

        /// <summary>단일 backdrop을 stack에 따라 sibling index 이동 (animation 직전).</summary>
        ChangeOrderBeforeAnimation,

        /// <summary>단일 backdrop을 stack에 따라 sibling index 이동 (animation 직후).</summary>
        ChangeOrderAfterAnimation,
    }
}
