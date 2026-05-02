namespace Tjdtjq5.UIFramework.Screens.Core
{
    /// <summary>
    /// Page / Modal / Sheet 컨테이너의 공통 인터페이스.
    /// </summary>
    public interface IScreenContainer
    {
        /// <summary>현재 전환(push/pop/show/hide) 진행 중 여부.</summary>
        bool IsInTransition { get; }

        /// <summary>컨테이너 입력 활성 여부 (CanvasGroup.interactable).</summary>
        bool Interactable { get; set; }
    }
}
