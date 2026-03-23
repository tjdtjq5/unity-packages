using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class FeaturesWindow : EditorWindow
    {
        static readonly Color COL_FEATURE = new(0.55f, 0.40f, 0.85f);

        List<FeatureInfo> _features;
        Vector2 _scrollPos;
        bool _showAddPopup;

        // 커스텀 Feature 생성
        bool _showCreateCustom;
        string _customId = "";
        string _customName = "";

        // 알림
        string _notification;
        EditorUI.NotificationType _notificationType;

        [MenuItem("Tools/GameServer/Features %#f")]
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
            EditorUI.DrawWindowBackground(position);
            EditorUI.DrawWindowHeader("Features", "", COL_FEATURE);
            EditorUI.DrawNotificationBar(ref _notification, _notificationType);

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
            EditorUI.DrawSectionHeader("설치된 Feature", EditorUI.COL_SUCCESS);
            GUILayout.Space(4);

            var installed = _features.Where(f => f.isInstalled).ToList();

            if (installed.Count == 0)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription("설치된 Feature가 없습니다.\n[+ Feature 추가]로 게임 기능을 추가하세요.");
                EditorUI.EndBody();
                return;
            }

            foreach (var feature in installed)
                DrawInstalledCard(feature);
        }

        void DrawInstalledCard(FeatureInfo feature)
        {
            EditorUI.BeginBody();

            using (new EditorGUILayout.HorizontalScope())
            {
                var label = feature.isCustom ? $"{feature.name} (커스텀)" : feature.name;
                EditorUI.DrawCellLabel(label, 0, COL_FEATURE);

                GUILayout.FlexibleSpace();

                // 코드 보기 버튼
                if (!string.IsNullOrEmpty(feature.installPath))
                {
                    if (EditorUI.DrawMiniButton("코드 보기"))
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
                EditorUI.DrawCellLabel($"  {feature.description}", 0, EditorUI.COL_MUTED);

            // 의존성 표시
            if (feature.dependencies != null && feature.dependencies.Length > 0)
            {
                var depNames = string.Join(", ", feature.dependencies);
                EditorUI.DrawCellLabel($"  의존: {depNames}", 0, EditorUI.COL_MUTED);
            }

            EditorUI.EndBody();
            GUILayout.Space(2);
        }

        // ── 액션 버튼 ──

        void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (EditorUI.DrawColorButton("+ Feature 추가", COL_FEATURE, 32))
                {
                    _showAddPopup = true;
                    _showCreateCustom = false;
                    Refresh();
                }

                GUILayout.Space(8);

                if (EditorUI.DrawColorButton("+ 커스텀 Feature 만들기", EditorUI.COL_MUTED, 32))
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
            if (EditorUI.DrawBackButton("← 돌아가기"))
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

            EditorUI.DrawSectionHeader("Feature 추가", COL_FEATURE);
            GUILayout.Space(4);

            var available = _features.Where(f => !f.isInstalled && !f.isCustom).ToList();

            if (available.Count == 0)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription("모든 Feature가 설치되어 있습니다.");
                EditorUI.EndBody();
                return;
            }

            foreach (var feature in available)
                DrawAvailableCard(feature);

            GUILayout.Space(12);
            if (EditorUI.DrawColorButton("+ 커스텀 Feature 만들기", EditorUI.COL_MUTED, 28))
            {
                _showCreateCustom = true;
                _customId = "";
                _customName = "";
            }
        }

        void DrawAvailableCard(FeatureInfo feature)
        {
            EditorUI.BeginBody();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorUI.DrawCellLabel(feature.name, 0, COL_FEATURE);

                GUILayout.FlexibleSpace();

                if (EditorUI.DrawColorButton("추가", COL_FEATURE, 24))
                    InstallFeature(feature);
            }

            // 설명 (전체 너비 사용)
            EditorUI.DrawCellLabel($"  {feature.description}", 0, EditorUI.COL_MUTED);

            if (feature.dependencies != null && feature.dependencies.Length > 0)
            {
                var (ok, missing) = FeatureRegistry.CheckDependencies(feature);
                if (!ok)
                {
                    var missingNames = string.Join(", ", missing);
                    EditorUI.DrawCellLabel($"  필요: {missingNames} (함께 설치됩니다)", 0, EditorUI.COL_WARN);
                }
            }

            EditorUI.EndBody();
            GUILayout.Space(2);
        }

        // ── 커스텀 Feature 생성 ──

        void DrawCreateCustom()
        {
            EditorUI.DrawSectionHeader("커스텀 Feature 만들기", EditorUI.COL_MUTED);
            GUILayout.Space(4);

            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "폴더와 feature.json이 자동 생성됩니다.\n" +
                "생성 후 [Table], [Service] 클래스를 직접 작성하세요.");
            GUILayout.Space(8);

            _customId = EditorUI.DrawTextField("ID (영문, 폴더명)", _customId, "예: daily-mission");
            _customName = EditorUI.DrawTextField("표시 이름", _customName, "예: 일일미션");

            GUILayout.Space(8);

            var valid = !string.IsNullOrEmpty(_customId) && !string.IsNullOrEmpty(_customName);
            EditorUI.BeginDisabled(!valid);
            if (EditorUI.DrawColorButton("만들기", COL_FEATURE, 28))
            {
                var path = FeatureInstaller.CreateCustom(_customId, _customName);
                if (path != null)
                {
                    _notification = $"'{_customName}' 생성 완료!";
                    _notificationType = EditorUI.NotificationType.Success;
                    _showAddPopup = false;
                    _showCreateCustom = false;
                    Refresh();
                }
            }
            EditorUI.EndDisabled();

            EditorUI.EndBody();
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
            _notificationType = EditorUI.NotificationType.Success;
            _showAddPopup = false;
            Refresh();
        }
    }
}
