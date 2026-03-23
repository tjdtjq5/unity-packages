using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    public class ServicesTab
    {
        const string GuideUrl = "https://github.com/tjdtjq5/unity-packages/blob/main/com.tjdtjq5.gameserver/Documentation~/GUIDE.md";

        List<ServiceInfo> _cachedInfos;
        double _lastScanTime;
        Vector2 _scrollPos;

        public void OnDraw()
        {
            ScanIfNeeded();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            if (_cachedInfos == null || _cachedInfos.Count == 0)
            {
                DrawEmpty();
            }
            else
            {
                DrawList();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawEmpty()
        {
            EditorUI.DrawSectionHeader("Services", GameServerDashboard.COL_PRIMARY);
            GUILayout.Space(20);

            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "아직 서비스가 없습니다.\n\n" +
                "[Service] 클래스를 작성하면\n" +
                "여기에 자동으로 표시됩니다.");
            EditorUI.EndBody();

            GUILayout.Space(12);

            EditorUI.BeginBody();
            EditorUI.DrawDescription(
                "예시:\n\n" +
                "[Service]\n" +
                "public class PlayerService\n" +
                "{\n" +
                "    readonly IGameDB _db;\n" +
                "    public PlayerService(IGameDB db) => _db = db;\n\n" +
                "    [API]\n" +
                "    public async Task<Player> CreatePlayer(string id, string nickname)\n" +
                "    {\n" +
                "        var player = new Player { id = id, nickname = nickname, level = 1 };\n" +
                "        await _db.Save(player);\n" +
                "        return player;\n" +
                "    }\n" +
                "}");
            EditorUI.EndBody();

            GUILayout.Space(12);

            if (EditorUI.DrawLinkButton("가이드 보기", GameServerDashboard.COL_PRIMARY))
                Application.OpenURL(GuideUrl);
        }

        void DrawList()
        {
            EditorUI.DrawSectionHeader("Services", GameServerDashboard.COL_PRIMARY);
            GUILayout.Space(4);

            foreach (var info in _cachedInfos)
            {
                DrawServiceInfo(info);
                GUILayout.Space(4);
            }

            GUILayout.Space(8);
            if (EditorUI.DrawLinkButton("가이드 보기", GameServerDashboard.COL_PRIMARY))
                Application.OpenURL(GuideUrl);
        }

        void DrawServiceInfo(ServiceInfo info)
        {
            EditorUI.BeginBody();

            // 서비스 이름 + 배포 상태
            bool anyDeployed = info.Methods.Any(m =>
                DeployRegistry.IsDeployed($"{info.ClassName}/{m}"));
            bool allDeployed = info.Methods.All(m =>
                DeployRegistry.IsDeployed($"{info.ClassName}/{m}"));

            string status;
            Color statusColor;
            if (allDeployed)
            {
                status = "● 배포됨";
                statusColor = EditorUI.COL_SUCCESS;
            }
            else if (anyDeployed)
            {
                status = "◐ 일부 배포";
                statusColor = EditorUI.COL_WARN;
            }
            else
            {
                status = "○ 미배포 (로컬)";
                statusColor = EditorUI.COL_MUTED;
            }

            EditorUI.DrawCellLabel($"  {info.ClassName}  {status}", 0, statusColor);

            // 메서드 목록
            foreach (var method in info.Methods)
            {
                bool deployed = DeployRegistry.IsDeployed($"{info.ClassName}/{method}");
                string icon = deployed ? "  ✓" : "  ·";
                Color col = deployed ? EditorUI.COL_SUCCESS : EditorUI.COL_MUTED;
                EditorUI.DrawCellLabel($"    {icon} {method}", 0, col);
            }

            EditorUI.EndBody();
        }

        void ScanIfNeeded()
        {
            // 10초마다 재스캔
            if (_cachedInfos != null && EditorApplication.timeSinceStartup - _lastScanTime < 10)
                return;

            _lastScanTime = EditorApplication.timeSinceStartup;
            _cachedInfos = new List<ServiceInfo>();

            // Assembly-CSharp에서 [Service] 클래스 스캔
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("Assembly-CSharp")) continue;

                foreach (var type in assembly.GetTypes())
                {
                    var attr = type.GetCustomAttribute<ServiceAttribute>();
                    if (attr == null) continue;

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(m => !m.IsSpecialName)
                        .Select(m => m.Name)
                        .ToList();

                    if (methods.Count > 0)
                    {
                        _cachedInfos.Add(new ServiceInfo
                        {
                            ClassName = type.Name,
                            Methods = methods
                        });
                    }
                }
            }
        }

        class ServiceInfo
        {
            public string ClassName;
            public List<string> Methods;
        }
    }
}
