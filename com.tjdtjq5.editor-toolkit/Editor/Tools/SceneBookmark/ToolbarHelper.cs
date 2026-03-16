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
    public static class ToolbarHelper
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

        /// <summary>Play 모드 영역을 찾는다. Unity 6 Overlay 시스템 + 레거시 다단계 폴백.</summary>
        public static VisualElement FindPlayZone(VisualElement root)
        {
            // Unity 6: Overlay 기반 — name='PlayMode'
            var zone = root.Q("PlayMode");
            if (zone != null) return zone;

            // 레거시 폴백
            zone = root.Q("ToolbarZonePlayMode");
            if (zone != null) return zone;

            zone = root.Q(className: "unity-toolbar-zone-play-mode");
            if (zone != null) return zone;

            zone = root.Q("Play");
            if (zone?.parent != null) return zone.parent;

            zone = root.Q("ToolbarZoneLeftAlign");
            return zone;
        }

        /// <summary>Unity 6 Overlay 기반 middle 컨테이너를 찾는다.</summary>
        public static VisualElement FindMiddleContainer(VisualElement root)
        {
            return root.Q(className: "unity-overlay-container__middle-container");
        }

        /// <summary>Unity 6 Overlay 기반 before-spacer(좌측) 컨테이너를 찾는다.</summary>
        public static VisualElement FindBeforeSpacerContainer(VisualElement root)
        {
            return root.Q(className: "unity-overlay-container__before-spacer-container");
        }

        /// <summary>Unity 6 Overlay 기반 after-spacer(우측) 컨테이너를 찾는다.</summary>
        public static VisualElement FindAfterSpacerContainer(VisualElement root)
        {
            return root.Q(className: "unity-overlay-container__after-spacer-container");
        }
    }
}
