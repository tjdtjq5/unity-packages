#if UNITY_EDITOR
using UnityEditor;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>Project Settings 창에 AddrX 탭을 등록한다.</summary>
    public class AddrXSettingsProvider : SettingsProvider
    {
        SerializedObject _so;

        AddrXSettingsProvider()
            : base("Project/AddrX", SettingsScope.Project) { }

        public override void OnActivate(string searchContext,
            UnityEngine.UIElements.VisualElement rootElement)
        {
            _so = new SerializedObject(AddrXSettings.GetOrCreate());
        }

        public override void OnGUI(string searchContext)
        {
            if (_so == null || _so.targetObject == null)
                _so = new SerializedObject(AddrXSettings.GetOrCreate());

            _so.Update();

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Logging", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            AddrXGui.DrawProperty(_so, "_logLevel", "Log Level",
                "이 레벨 미만의 로그는 출력되지 않습니다.");

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Debugging", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            AddrXGui.DrawProperty(_so, "_enableTracking", "Enable Tracking",
                "Handle Tracker 활성화 (Debug/Development 빌드)");
            AddrXGui.DrawProperty(_so, "_enableLeakDetection", "Enable Leak Detection",
                "씬 전환 시 미해제 핸들 경고");

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("Initialization", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            AddrXGui.DrawProperty(_so, "_autoInitialize", "Auto Initialize",
                "RuntimeInitializeOnLoadMethod로 자동 초기화");

            EditorGUILayout.Space(16);

            if (EditorGUILayout.LinkButton("Open AddrX Manager"))
                EditorWindow.GetWindow<AddrXManagerWindow>("AddrX");

            if (_so.ApplyModifiedProperties())
            {
                var settings = (AddrXSettings)_so.targetObject;
                settings.Apply();
            }
        }

        [SettingsProvider]
        static SettingsProvider Create() => new AddrXSettingsProvider();
    }
}
#endif
