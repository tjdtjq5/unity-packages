#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Tjdtjq5.AddrX.Debug
{
    /// <summary>인게임 IMGUI 디버그 오버레이. F9로 토글.</summary>
    public class DebugHUD : MonoBehaviour
    {
        [SerializeField] bool _showDetails;
        [SerializeField] KeyCode _toggleKey = KeyCode.F9;

        bool _visible = true;
        Vector2 _scrollPos;

        void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
                _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible) return;

            var height = _showDetails ? 400f : 80f;
            var area = new Rect(10, 10, 340, height);
            GUI.Window(9999, area, DrawWindow, "AddrX Debug");
        }

        void DrawWindow(int windowId)
        {
            GUILayout.Label(
                $"Active: {HandleTracker.ActiveCount}  |  " +
                $"Loaded: {HandleTracker.TotalLoaded}  |  " +
                $"Released: {HandleTracker.TotalReleased}");

            _showDetails = GUILayout.Toggle(_showDetails, "Show Details");

            if (_showDetails)
            {
                _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(280));

                var handles = HandleTracker.ActiveHandles;
                for (int i = 0; i < handles.Count; i++)
                {
                    var h = handles[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"[{h.Id}]", GUILayout.Width(40));
                    GUILayout.Label(h.Address ?? "(null)", GUILayout.Width(170));
                    GUILayout.Label(h.AssetType?.Name ?? "?", GUILayout.Width(70));
                    GUILayout.Label($"{h.Age:F0}s", GUILayout.Width(40));
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();

                if (GUILayout.Button("Check Leaks"))
                {
                    var report = LeakDetector.CheckForLeaks();
                    AddrXLog.Info("DebugHUD",
                        $"누수 체크: {report.LeakCount}개 활성 핸들");
                }
            }

            GUI.DragWindow();
        }
    }
}
#endif
