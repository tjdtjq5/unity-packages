using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tjdtjq5.EditorToolkit.Editor.Tools
{
    /// <summary>
    /// Unity 6 메인 툴바 VisualElement 접근 헬퍼.
    /// Unity 6에서 Toolbar는 GUIView → ScriptableObject를 상속하므로
    /// EditorWindow.rootVisualElement 대신 GUIView.visualTree 리플렉션 사용.
    /// </summary>
    static class ToolbarHelper
    {
        static PropertyInfo _visualTreeProp;

        /// <summary>메인 툴바의 VisualElement 루트를 반환. 못 찾으면 null.</summary>
        public static VisualElement GetToolbarRoot()
        {
            var toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
            if (toolbarType == null) return null;

            var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
            if (toolbars == null || toolbars.Length == 0) return null;

            var toolbar = toolbars[0];

            if (toolbar is EditorWindow editorWindow)
                return editorWindow.rootVisualElement;

            // Unity 6: Toolbar → GUIView (EditorWindow 아님)
            _visualTreeProp ??= toolbar.GetType().GetProperty("visualTree",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return _visualTreeProp?.GetValue(toolbar) as VisualElement;
        }

        /// <summary>Play 모드 영역(ToolbarZonePlayMode)을 찾는다. 다단계 폴백.</summary>
        public static VisualElement FindPlayZone(VisualElement root)
        {
            // 1차: 이름
            var zone = root.Q("ToolbarZonePlayMode");
            if (zone != null) return zone;

            // 2차: USS 클래스
            zone = root.Q(className: "unity-toolbar-zone-play-mode");
            if (zone != null) return zone;

            // 3차: Play 버튼 부모
            zone = root.Q("Play");
            if (zone?.parent != null) return zone.parent;

            // 4차: 좌측 영역
            zone = root.Q("ToolbarZoneLeftAlign");
            return zone;
        }
    }
}
