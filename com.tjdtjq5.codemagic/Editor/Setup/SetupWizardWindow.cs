#if UNITY_EDITOR
using Tjdtjq5.Codemagic.Editor.Setup.Steps;
using UnityEditor;

namespace Tjdtjq5.Codemagic.Editor.Setup
{
    /// <summary>SetupWizard 호스트 EditorWindow. 메뉴: Build → Codemagic → Setup Wizard.</summary>
    public sealed class SetupWizardWindow : EditorWindow
    {
        SetupWizard _wizard;

        [MenuItem("Build/Codemagic/Setup Wizard", priority = 100)]
        public static void Open()
        {
            var w = GetWindow<SetupWizardWindow>("Codemagic Setup");
            w.minSize = new UnityEngine.Vector2(560, 640);
            w.Show();
        }

        void OnEnable()
        {
            var ctx = new SetupContext();
            var steps = new ISetupStep[]
            {
                new Step0WelcomeStep(),
                new Step1PreflightStep(),
                new Step2TokenStep(),
                new Step3AppMatchStep(),
                new Step4LicenseStep(),
                new Step5KeystoreStep(),
                new Step6CompleteStep(),
            };
            _wizard = new SetupWizard(ctx, steps);
        }

        void OnGUI()
        {
            _wizard?.OnDraw();
        }

        // 비동기 호출(API 검증, 앱 목록 조회 등) 진행 상황을 IMGUI에 반영하기 위해
        // ~10Hz로 자동 Repaint. IMGUI 부담은 무시 가능.
        void OnInspectorUpdate() => Repaint();
    }
}
#endif
