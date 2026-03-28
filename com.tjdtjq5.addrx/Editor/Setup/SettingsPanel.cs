#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using Tjdtjq5.EditorToolkit.Editor;

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
            if (EditorUI.DrawBackButton("← 돌아가기"))
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

            EditorUI.DrawSectionHeader("AddrX", EditorUI.COL_INFO);
            EditorGUILayout.Space(4);

            EditorUI.DrawProperty(_so, "_logLevel", "Log Level",
                "이 레벨 미만의 로그는 출력되지 않습니다.");

            EditorGUILayout.Space(8);
            EditorUI.DrawProperty(_so, "_enableTracking", "Enable Tracking",
                "Handle Tracker 활성화");
            EditorUI.DrawProperty(_so, "_enableLeakDetection", "Enable Leak Detection",
                "씬 전환 시 미해제 핸들 경고");

            EditorGUILayout.Space(8);
            EditorUI.DrawProperty(_so, "_autoInitialize", "Auto Initialize",
                "RuntimeInitializeOnLoadMethod로 자동 초기화");

            if (_so.ApplyModifiedProperties())
                ((AddrXSettings)_so.targetObject).Apply();
        }

        // ─── Addressables Settings ───

        void DrawAddressablesSection()
        {
            EditorUI.DrawSectionHeader("Addressables", EditorUI.COL_WARN);
            EditorGUILayout.Space(4);

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                EditorUI.DrawPlaceholder("Addressables Settings 없음");
                return;
            }

            var profile = settings.profileSettings;
            var activeId = settings.activeProfileId;
            EditorUI.DrawDescription($"Profile: {profile.GetProfileName(activeId)}");

            EditorGUILayout.Space(4);
            EditorUI.BeginSubBox();
            try
            {
                EditorUI.DrawCellLabel($"Build: {profile.GetValueByName(activeId, "LocalBuildPath")}");
                EditorUI.DrawCellLabel($"Load: {profile.GetValueByName(activeId, "LocalLoadPath")}");
            }
            catch (System.Exception)
            {
                EditorUI.DrawCellLabel("(Profile 변수를 읽을 수 없음)");
            }
            EditorUI.EndSubBox();

            EditorGUILayout.Space(8);

            if (EditorUI.DrawLinkButton("Open Addressables Groups"))
                EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Groups");
            if (EditorUI.DrawLinkButton("Open Addressables Profiles"))
                EditorApplication.ExecuteMenuItem("Window/Asset Management/Addressables/Profiles");
        }
    }
}
#endif
