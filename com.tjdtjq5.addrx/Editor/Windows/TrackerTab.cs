#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Tjdtjq5.EditorToolkit.Editor;
using Tjdtjq5.AddrX.Debug;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>핸들 추적 탭. 활성 핸들 목록 + 리사이징 테이블 + 누수 체크.</summary>
    public class TrackerTab : EditorTabBase
    {
        readonly Action _repaint;
        EditorUI.ResizableColumns _columns;
        string _search = "";
        Vector2 _scroll;
        new string _notification;
        new EditorUI.NotificationType _notificationType;

        public TrackerTab(Action repaint) => _repaint = repaint;

        public override string TabName => "Tracker";
        public override Color TabColor => EditorUI.COL_WARN;

        public override void OnEnable()
        {
            _columns = new EditorUI.ResizableColumns("AddrX_Tracker", new[]
            {
                new EditorUI.ColumnDef { Name = "ID", DefaultWidth = 50, Resizable = true, MinWidth = 30 },
                new EditorUI.ColumnDef { Name = "Address", DefaultWidth = 0, Resizable = true, MinWidth = 100 },
                new EditorUI.ColumnDef { Name = "Type", DefaultWidth = 80, Resizable = true, MinWidth = 50 },
                new EditorUI.ColumnDef { Name = "Age", DefaultWidth = 60, Resizable = false, MinWidth = 40 },
                new EditorUI.ColumnDef { Name = "", DefaultWidth = 50, Resizable = false, MinWidth = 50 }
            }, _repaint);

            HandleTracker.OnHandleCreated += OnChanged;
            HandleTracker.OnHandleReleased += OnChanged;
        }

        public override void OnDisable()
        {
            HandleTracker.OnHandleCreated -= OnChanged;
            HandleTracker.OnHandleReleased -= OnChanged;
        }

        void OnChanged(HandleInfo _) => _repaint?.Invoke();

        public override void OnDraw()
        {
            EditorUI.DrawNotificationBar(ref _notification, _notificationType);

            // ─── Stats ───
            EditorUI.BeginRow();
            EditorUI.DrawStatCard("Active", HandleTracker.ActiveCount.ToString(),
                HandleTracker.ActiveCount > 0 ? EditorUI.COL_INFO : EditorUI.COL_MUTED);
            EditorUI.DrawStatCard("Loaded", HandleTracker.TotalLoaded.ToString(), EditorUI.COL_SUCCESS);
            EditorUI.DrawStatCard("Released", HandleTracker.TotalReleased.ToString(), EditorUI.COL_MUTED);
            EditorUI.EndRow();

            EditorGUILayout.Space(8);

            // ─── Search + Actions ───
            EditorUI.BeginRow();
            _search = EditorUI.DrawTextField("", _search, "주소 또는 타입으로 검색");
            EditorUI.FlexSpace();
            if (EditorUI.DrawColorButton("Check Leaks", EditorUI.COL_WARN))
            {
                var report = LeakDetector.CheckForLeaks();
                _notification = $"누수 체크: {report.LeakCount}개 활성 핸들";
                _notificationType = report.LeakCount > 0
                    ? EditorUI.NotificationType.Error
                    : EditorUI.NotificationType.Success;
            }
            EditorUI.EndRow();

            EditorGUILayout.Space(4);

            // ─── Table ───
            _columns.DrawHeader();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            var handles = HandleTracker.ActiveHandles;
            int shown = 0;
            for (int i = 0; i < handles.Count; i++)
            {
                var h = handles[i];

                if (!string.IsNullOrEmpty(_search))
                {
                    var addr = h.Address ?? "";
                    var type = h.AssetType?.Name ?? "";
                    if (!addr.Contains(_search, StringComparison.OrdinalIgnoreCase)
                        && !type.Contains(_search, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                EditorUI.BeginRow();
                EditorUI.DrawCellLabel(h.Id.ToString(), _columns.GetWidth(0));
                EditorUI.DrawCellLabel(h.Address ?? "(null)", _columns.GetWidth(1));
                EditorUI.DrawCellLabel(h.AssetType?.Name ?? "?", _columns.GetWidth(2));
                EditorUI.DrawCellLabel($"{h.Age:F1}s", _columns.GetWidth(3), EditorUI.COL_MUTED);
                if (EditorUI.DrawMiniButton("Stack"))
                {
                    var msg = !string.IsNullOrEmpty(h.StackTrace)
                        ? $"[AddrX] Handle [{h.Id}] {h.Address} 할당 스택:\n{h.StackTrace}"
                        : $"[AddrX] Handle [{h.Id}] 스택 없음 (Tracking 비활성)";
                    UnityEngine.Debug.Log(msg);
                }
                EditorUI.EndRow();
                shown++;
            }

            if (shown == 0)
                EditorUI.DrawPlaceholder("활성 핸들이 없습니다");

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
