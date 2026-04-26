using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 배포된 엔드포인트 목록 관리.
    /// 배포 시 목록에 추가, 호출 시 IsDeployed로 확인.
    /// </summary>
    public static class DeployRegistry
    {
        static HashSet<string> _deployed;

        // PlayerPrefs에 JSON으로 저장 (Editor + Build 공통)
        const string PrefsKey = "SupaRun_DeployedEndpoints";

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

            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonUtility.FromJson<DeployData>(json);
                    _deployed = new HashSet<string>(data.endpoints ?? Array.Empty<string>());
                    MigratePascalToSnake();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] DeployRegistry 로드 실패: {ex.Message}");
                    _deployed = new HashSet<string>();
                }
            }
            else
            {
                _deployed = new HashSet<string>();
            }
        }

        // v0.5.3 마이그레이션: 이전 버전 endpoint 키 ServiceName/Method → service_name/Method
        // ServiceGenerator/DeployManager가 PascalCase로 저장하던 시기의 PlayerPrefs를 자동 변환
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
            if (head.Contains('_')) return false; // 이미 snake_case
            if (!char.IsUpper(head[0])) return false; // PascalCase 시작 아님
            for (int i = 1; i < head.Length; i++)
                if (char.IsUpper(head[i])) return true; // 두 번째 이후 대문자 = PascalCase
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

        static void Save()
        {
            var data = new DeployData();
            data.endpoints = new string[_deployed.Count];
            _deployed.CopyTo(data.endpoints);
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        [Serializable]
        class DeployData
        {
            public string[] endpoints;
        }
    }
}
