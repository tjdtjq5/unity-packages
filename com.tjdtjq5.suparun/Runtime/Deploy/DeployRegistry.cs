using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 배포된 엔드포인트 목록 관리.
    /// v0.5.4부터: ProjectSettings/SupaRunDeployedEndpoints.json (git 공유 — 모든 PC가 자동 동기화).
    /// 이전: PlayerPrefs (PC별, 멀티 개발 환경에서 새 PC가 LocalDB로 폴백되는 문제).
    /// 첫 로드 시 PlayerPrefs 레거시 데이터를 ProjectSettings로 자동 마이그레이션 후 PlayerPrefs 키를 삭제.
    /// </summary>
    public static class DeployRegistry
    {
        static HashSet<string> _deployed;

        // v0.5.3 이전: PlayerPrefs (PC별)
        const string LegacyPrefsKey = "SupaRun_DeployedEndpoints";

        // v0.5.4+: ProjectSettings 파일 (git 공유)
        const string ProjectSettingsPath = "ProjectSettings/SupaRunDeployedEndpoints.json";

        /// <summary>해당 엔드포인트가 배포되었는지 확인.</summary>
        public static bool IsDeployed(string endpoint)
        {
            EnsureLoaded();
            return _deployed.Contains(endpoint);
        }

        /// <summary>배포 시 호출. 엔드포인트 목록을 배포됨으로 등록.</summary>
        public static void MarkDeployed(string[] endpoints)
        {
            EnsureLoaded();
            foreach (var ep in endpoints)
                _deployed.Add(ep);
            Save();
        }

        /// <summary>전체 배포 목록 반환.</summary>
        public static string[] GetDeployedEndpoints()
        {
            EnsureLoaded();
            var arr = new string[_deployed.Count];
            _deployed.CopyTo(arr);
            return arr;
        }

        /// <summary>배포 목록 초기화 (테스트용).</summary>
        public static void Clear()
        {
            _deployed = new HashSet<string>();
            Save();
        }

        static void EnsureLoaded()
        {
            if (_deployed != null) return;

            // 1. ProjectSettings 파일에서 로드 시도 (v0.5.4+ 정상 경로)
            if (TryLoadFromFile())
            {
                MigratePascalToSnake();
                return;
            }

            // 2. ProjectSettings 파일 없으면 PlayerPrefs 레거시 데이터 마이그레이션 (v0.5.3 이하 → v0.5.4)
            var legacyJson = PlayerPrefs.GetString(LegacyPrefsKey, "");
            if (!string.IsNullOrEmpty(legacyJson))
            {
                try
                {
                    var data = JsonUtility.FromJson<DeployData>(legacyJson);
                    _deployed = new HashSet<string>(data.endpoints ?? Array.Empty<string>());
                    MigratePascalToSnake();
                    Save(); // ProjectSettings에 저장
                    PlayerPrefs.DeleteKey(LegacyPrefsKey);
                    PlayerPrefs.Save();
                    Debug.Log($"[SupaRun] DeployRegistry: PlayerPrefs → ProjectSettings 마이그레이션 완료 ({_deployed.Count}개)");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] DeployRegistry 레거시 로드 실패: {ex.Message}");
                }
            }

            _deployed = new HashSet<string>();
        }

        static bool TryLoadFromFile()
        {
            try
            {
                if (!File.Exists(ProjectSettingsPath)) return false;
                var json = File.ReadAllText(ProjectSettingsPath);
                if (string.IsNullOrEmpty(json)) return false;
                var data = JsonUtility.FromJson<DeployData>(json);
                _deployed = new HashSet<string>(data.endpoints ?? Array.Empty<string>());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun] DeployRegistry 파일 로드 실패: {ex.Message}");
                return false;
            }
        }

        static void Save()
        {
            try
            {
                var data = new DeployData();
                data.endpoints = new string[_deployed.Count];
                _deployed.CopyTo(data.endpoints);
                Array.Sort(data.endpoints, StringComparer.Ordinal); // diff 안정성

                var dir = Path.GetDirectoryName(ProjectSettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(ProjectSettingsPath, JsonUtility.ToJson(data, prettyPrint: true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun] DeployRegistry 저장 실패: {ex.Message}");
            }
        }

        // v0.5.2 hotfix: PascalCase 키 → snake_case 자동 변환 (ServiceGenerator/DeployManager가 PascalCase로 저장하던 시기 호환)
        static void MigratePascalToSnake()
        {
            if (_deployed == null || _deployed.Count == 0) return;
            var migrated = new HashSet<string>();
            bool changed = false;
            foreach (var ep in _deployed)
            {
                int slash = ep.IndexOf('/');
                if (slash <= 0) { migrated.Add(ep); continue; }
                string head = ep.Substring(0, slash);
                string tail = ep.Substring(slash);
                if (NeedsSnakeMigration(head))
                {
                    migrated.Add(ToSnakeCase(head) + tail);
                    changed = true;
                }
                else
                {
                    migrated.Add(ep);
                }
            }
            if (changed)
            {
                _deployed = migrated;
                Save();
                Debug.Log($"[SupaRun] DeployRegistry: PascalCase → snake_case 마이그레이션 완료 ({_deployed.Count}개)");
            }
        }

        static bool NeedsSnakeMigration(string head)
        {
            if (string.IsNullOrEmpty(head)) return false;
            if (head.Contains('_')) return false;
            if (!char.IsUpper(head[0])) return false;
            for (int i = 1; i < head.Length; i++)
                if (char.IsUpper(head[i])) return true;
            return false;
        }

        static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }

        [Serializable]
        class DeployData
        {
            public string[] endpoints;
        }
    }
}
