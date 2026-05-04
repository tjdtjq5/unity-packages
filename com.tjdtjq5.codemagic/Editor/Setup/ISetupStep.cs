#if UNITY_EDITOR
namespace Tjdtjq5.Codemagic.Editor.Setup
{
    /// <summary>SetupWizard의 한 단계. cicd `IWizardStep` 패턴 + SetupContext 주입.</summary>
    public interface ISetupStep
    {
        /// <summary>Step 인디케이터에 표시될 짧은 라벨 (예: "라이선스").</summary>
        string Title { get; }

        /// <summary>다음 단계로 넘어갈 수 있는지 (필수 입력 완료 / 검증 통과).</summary>
        bool IsCompleted { get; }

        /// <summary>건너뛰기 불가하면 true. false면 [건너뛰기] 버튼 노출.</summary>
        bool IsRequired { get; }

        /// <summary>이 step 진입 시 1회 호출 (자동 검증/로드).</summary>
        void OnEnter(SetupContext ctx);

        /// <summary>IMGUI 매 프레임 호출. ctx에서 상태/Service 접근.</summary>
        void OnDraw(SetupContext ctx);

        /// <summary>다음 step으로 떠날 때 호출. 정리/저장 용도.</summary>
        void OnLeave(SetupContext ctx);
    }
}
#endif
