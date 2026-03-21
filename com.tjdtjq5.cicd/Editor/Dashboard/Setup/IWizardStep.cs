#if UNITY_EDITOR
namespace Tjdtjq5.CICD.Editor
{
    /// <summary>위저드 스텝 공통 인터페이스</summary>
    public interface IWizardStep
    {
        string StepLabel { get; }
        void OnDraw();
        bool IsCompleted { get; }
        bool IsRequired { get; }
    }
}
#endif
