#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Tjdtjq5.AddrX.Debug;

namespace Tjdtjq5.AddrX.Editor
{
    /// <summary>핸들 추적 탭. 활성 핸들 목록 + 고정폭 테이블 + 누수 체크.</summary>
    internal class TrackerTab : AddrXTabBase
    {
        readonly Action _repaint;
        string _search = "";
        Vector2 _scroll;

        public TrackerTab(Action repaint) => _repaint = repaint;

        public override string TabName => "Tracker";
        public override Color TabColor => new(0.95f, 0.75f, 0.20f);

        public override void OnEnable()
        {
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
            AddrXGui.DrawNotificationBar(ref _notification, _notificationType);

            // ─── Stats ───
            EditorGUILayout.BeginHorizontal();
            AddrXGui.DrawStatCard("Active", HandleTracker.ActiveCount.ToString());
            AddrXGui.DrawStatCard("Loaded", HandleTracker.TotalLoaded.ToString());
            AddrXGui.DrawStatCard("Released", HandleTracker.TotalReleased.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // ─── Search + Actions ───
            EditorGUILayout.BeginHorizontal();
            _search = EditorGUILayout.TextField(
                new GUIContent("", "주소 또는 타입으로 검색"), _search);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Check Leaks"))
            {
                var report = LeakDetector.CheckForLeaks();
                _notification = $"누수 체크: {report.LeakCount}개 활성 핸들";
                _notificationType = report.LeakCount > 0
                    ? NotificationType.Error
                    : NotificationType.Success;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ─── Table (고정폭 헤더: ID 50 / Address 유동 / Type 80 / Age 60 / 버튼 50) ───
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.LabelField("Address", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Age", EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField("", EditorStyles.boldLabel, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

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

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(h.Id.ToString(), GUILayout.Width(50));
                EditorGUILayout.LabelField(h.Address ?? "(null)");
                EditorGUILayout.LabelField(h.AssetType?.Name ?? "?", GUILayout.Width(80));
                EditorGUILayout.LabelField($"{h.Age:F1}s", GUILayout.Width(60));
                if (GUILayout.Button("Stack", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    var msg = !string.IsNullOrEmpty(h.StackTrace)
                        ? $"[AddrX] Handle [{h.Id}] {h.Address} 할당 스택:\n{h.StackTrace}"
                        : $"[AddrX] Handle [{h.Id}] 스택 없음 (Tracking 비활성)";
                    UnityEngine.Debug.Log(msg);
                }
                EditorGUILayout.EndHorizontal();
                shown++;
            }

            if (shown == 0)
                EditorGUILayout.LabelField("활성 핸들이 없습니다", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
