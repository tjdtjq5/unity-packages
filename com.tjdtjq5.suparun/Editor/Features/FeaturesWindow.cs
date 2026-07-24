using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class FeaturesWindow : EditorWindow
    {
        List<FeatureInfo> _features;
        Vector2 _scrollPos;
        bool _showAddPopup;

        // 커스텀 Feature 생성
        bool _showCreateCustom;
        string _customId = "";
        string _customName = "";

        // 알림
        string _notification;
        SupaRunUI.NotificationType _notificationType;

        [MenuItem("Tjdtjq/SupaRun/Features %#f")]
        public static void Open()
        {
            var wnd = GetWindow<FeaturesWindow>("Features");
            wnd.minSize = new Vector2(460, 400);
        }

        void OnEnable()
        {
            Refresh();
            // 설치된 Feature가 있으면 확인
        }

        void Refresh()
        {
            _features = FeatureRegistry.GetAll();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Features", EditorStyles.largeLabel);
            EditorGUILayout.Space();
            SupaRunUI.DrawNotificationBar(ref _notification, _notificationType);

            if (_showAddPopup)
            {
                DrawAddPopup();
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawInstalledSection();
            GUILayout.Space(12);
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        // ── 설치된 Feature 목록 ──

        void DrawInstalledSection()
        {
            EditorGUILayout.LabelField("설치된 Feature", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var installed = _features.Where(f => f.isInstalled).ToList();

            if (installed.Count == 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    "설치된 Feature가 없습니다.\n[+ Feature 추가]로 게임 기능을 추가하세요.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            foreach (var feature in installed)
                DrawInstalledCard(feature);
        }

        void DrawInstalledCard(FeatureInfo feature)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                var label = feature.isCustom ? $"{feature.name} (커스텀)" : feature.name;
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                // 코드 보기 버튼
                if (!string.IsNullOrEmpty(feature.installPath))
                {
                    if (GUILayout.Button("코드 보기", EditorStyles.miniButton))
                    {
                        var csFiles = Directory.GetFiles(feature.installPath, "*.cs");
                        if (csFiles.Length > 0)
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<Object>(csFiles[0]);
                            if (asset != null) EditorGUIUtility.PingObject(asset);
                        }
                    }
                }
            }

            // 설명 (전체 너비 사용)
            if (!string.IsNullOrEmpty(feature.description))
                EditorGUILayout.LabelField($"  {feature.description}");

            // 의존성 표시
            if (feature.dependencies != null && feature.dependencies.Length > 0)
            {
                var depNames = string.Join(", ", feature.dependencies);
                EditorGUILayout.LabelField($"  의존: {depNames}");
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        // ── 액션 버튼 ──

        void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Feature 추가", GUILayout.Height(32)))
                {
                    _showAddPopup = true;
                    _showCreateCustom = false;
                    Refresh();
                }

                GUILayout.Space(8);

                if (GUILayout.Button("+ 커스텀 Feature 만들기", GUILayout.Height(32)))
                {
                    _showCreateCustom = true;
                    _showAddPopup = true;
                    _customId = "";
                    _customName = "";
                }
            }
        }

        // ── 추가 팝업 ──

        void DrawAddPopup()
        {
            if (GUILayout.Button("← 돌아가기", EditorStyles.miniButton))
            {
                _showAddPopup = false;
                _showCreateCustom = false;
                Refresh();
                return;
            }

            if (_showCreateCustom)
            {
                DrawCreateCustom();
                return;
            }

            EditorGUILayout.LabelField("Feature 추가", EditorStyles.boldLabel);
            GUILayout.Space(4);

            var available = _features.Where(f => !f.isInstalled && !f.isCustom).ToList();

            if (available.Count == 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("모든 Feature가 설치되어 있습니다.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            foreach (var feature in available)
                DrawAvailableCard(feature);

            GUILayout.Space(12);
            if (GUILayout.Button("+ 커스텀 Feature 만들기", GUILayout.Height(28)))
            {
                _showCreateCustom = true;
                _customId = "";
                _customName = "";
            }
        }

        void DrawAvailableCard(FeatureInfo feature)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(feature.name, EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("추가", GUILayout.Height(24)))
                    InstallFeature(feature);
            }

            // 설명 (전체 너비 사용)
            EditorGUILayout.LabelField($"  {feature.description}");

            if (feature.dependencies != null && feature.dependencies.Length > 0)
            {
                var (ok, missing) = FeatureRegistry.CheckDependencies(feature);
                if (!ok)
                {
                    var missingNames = string.Join(", ", missing);
                    EditorGUILayout.LabelField($"  필요: {missingNames} (함께 설치됩니다)");
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(2);
        }

        // ── 커스텀 Feature 생성 ──

        void DrawCreateCustom()
        {
            EditorGUILayout.LabelField("커스텀 Feature 만들기", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "폴더와 feature.json이 자동 생성됩니다.\n" +
                "생성 후 [UserData], [Service] 클래스를 직접 작성하세요.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);

            _customId = EditorGUILayout.TextField(
                new GUIContent("ID (영문, 폴더명)", "예: daily-mission"), _customId);
            _customName = EditorGUILayout.TextField(
                new GUIContent("표시 이름", "예: 일일미션"), _customName);

            GUILayout.Space(8);

            var valid = !string.IsNullOrEmpty(_customId) && !string.IsNullOrEmpty(_customName);
            EditorGUI.BeginDisabledGroup(!valid);
            if (GUILayout.Button("만들기", GUILayout.Height(28)))
            {
                var path = FeatureInstaller.CreateCustom(_customId, _customName);
                if (path != null)
                {
                    _notification = $"'{_customName}' 생성 완료!";
                    _notificationType = SupaRunUI.NotificationType.Success;
                    _showAddPopup = false;
                    _showCreateCustom = false;
                    Refresh();
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        // ── 설치 실행 ──

        void InstallFeature(FeatureInfo feature)
        {
            // 의존성 확인 다이얼로그
            if (feature.dependencies != null && feature.dependencies.Length > 0)
            {
                var (ok, missing) = FeatureRegistry.CheckDependencies(feature);
                if (!ok)
                {
                    var missingNames = string.Join(", ", missing);
                    if (!EditorUtility.DisplayDialog("의존성 확인",
                        $"'{feature.name}'에는 다음 Feature가 필요합니다:\n{missingNames}\n\n함께 설치하시겠습니까?",
                        "함께 설치", "취소"))
                        return;
                }
            }

            var installed = FeatureInstaller.Install(feature);

            var names = string.Join(", ", installed);
            _notification = $"설치 완료: {names}";
            _notificationType = SupaRunUI.NotificationType.Success;
            _showAddPopup = false;
            Refresh();
        }
    }
}
