#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>톱니바퀴 Settings 패널. AddrX + Addressables 설정. 본문 대체 방식.</summary>
    public class SettingsPanel
    {
        readonly Action _onBack;
        SerializedObject _so;
        Vector2 _scroll;

        public SettingsPanel(Action onBack)
        {
            _onBack = onBack;
            Refresh();
        }

        void Refresh()
        {
            _so = new SerializedObject(AddrXSettings.GetOrCreate());
        }

        public void OnDraw()
        {
            EditorGUILayout.BeginHorizontal();
            bool back = GUILayout.Button("← 돌아가기", EditorStyles.miniButton,
                GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (back)
            {
                _onBack?.Invoke();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawAddrXSection();
            EditorGUILayout.Space(12);
            DrawAddressablesSection();

            EditorGUILayout.EndScrollView();
        }

        // ─── AddrX Settings ───

        void DrawAddrXSection()
        {
            if (_so == null || _so.targetObject == null) Refresh();
            _so.Update();

            EditorGUILayout.LabelField("AddrX", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            AddrXGui.DrawProperty(_so, "_logLevel", "Log Level",
                "이 레벨 미만의 로그는 출력되지 않습니다.");

            EditorGUILayout.Space(8);
            AddrXGui.DrawProperty(_so, "_enableTracking", "Enable Tracking",
                "Handle Tracker 활성화");
            AddrXGui.DrawProperty(_so, "_enableLeakDetection", "Enable Leak Detection",
                "씬 전환 시 미해제 핸들 경고");

            EditorGUILayout.Space(8);
            AddrXGui.DrawProperty(_so, "_autoInitialize", "Auto Initialize",
                "RuntimeInitializeOnLoadMethod로 자동 초기화");

            if (_so.ApplyModifiedProperties())
                ((AddrXSettings)_so.targetObject).Apply();
        }

        // ─── Addressables Settings ───

        void DrawAddressablesSection()
        {
            EditorGUILayout.LabelField("Addressables", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorGUILayout.LabelField("Addressables Settings 없음",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var profile = settings.profileSettings;
            var activeId = settings.activeProfileId;
            EditorGUILayout.LabelField($"Profile: {profile.GetProfileName(activeId)}",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            try
            {
                EditorGUILayout.LabelField($"Build: {profile.GetValueByName(activeId, "LocalBuildPath")}");
                EditorGUILayout.LabelField($"Load: {profile.GetValueByName(activeId, "LocalLoadPath")}");
            }
            catch (System.Exception)
            {
                EditorGUILayout.LabelField("(Profile 변수를 읽을 수 없음)");
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            if (EditorGUILayout.LinkButton("Open Addressables Groups"))
                EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Groups");
            if (EditorGUILayout.LinkButton("Open Addressables Profiles"))
                EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Profiles");
        }
    }
}
#endif
