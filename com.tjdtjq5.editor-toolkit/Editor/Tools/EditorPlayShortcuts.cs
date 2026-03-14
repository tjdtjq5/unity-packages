using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor.Tools
{
    /// <summary>
    /// 에디터 Play/Speed 글로벌 단축키.
    /// F10: 감속(-0.5x), F11: Play/Stop 토글, F12: 가속(+0.5x).
    /// globalEventHandler를 사용하여 Game View 포커스 중에도 동작.
    /// </summary>
    [InitializeOnLoad]
    static class EditorPlayShortcuts
    {
        const float SpeedStep = 0.5f;

        static EditorPlayShortcuts()
        {
            var field = typeof(EditorApplication).GetField("globalEventHandler",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) return;

            var current = (EditorApplication.CallbackFunction)field.GetValue(null);
            field.SetValue(null, current + OnGlobalEvent);
        }

        static void OnGlobalEvent()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;

            switch (e.keyCode)
            {
                case KeyCode.F10:
                    if (!EditorApplication.isPlaying) return;
                    GameSpeedToolbar.AdjustSpeed(-SpeedStep);
                    e.Use();
                    break;

                case KeyCode.F11:
                    EditorApplication.isPlaying = !EditorApplication.isPlaying;
                    e.Use();
                    break;

                case KeyCode.F12:
                    if (!EditorApplication.isPlaying) return;
                    GameSpeedToolbar.AdjustSpeed(SpeedStep);
                    e.Use();
                    break;
            }
        }
    }
}
